using Npgsql;

namespace StarterApp.Api.Infrastructure.Persistence;

// Query-time retry policy for Dapper/ADO.NET calls against PostgreSQL.
//
// Why this exists: EF Core's EnableRetryOnFailure is scoped to ApplicationDbContext.
// Dapper reads go through plain ADO.NET commands, so this helper closes the retry
// asymmetry between EF writes and read-model queries.
//
// The operation Func is invoked per attempt; Dapper reopens connections from the
// pool as needed, so broken connections are recycled automatically.
//
// Backoff is jittered and capped by a total delay budget. A deterministic ladder makes every
// saturated reader retry in lockstep and hold its request open for the whole ladder, which
// amplifies the very exhaustion (53300) it is retrying; the EF write path already jitters via
// NpgsqlRetryingExecutionStrategy, so reads now match it. Deliberate trade: the read-retry
// window shrank from ~61 s to the 10 s budget, so a failover longer than that surfaces as an
// error instead of a request held open — which the common 60 s ingress read timeout would have
// cut off anyway.
public static class PostgresRetryPolicy
{
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 500;
    private const int MaxDelayMs = 5_000;
    internal static readonly TimeSpan DefaultTotalDelayBudget = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> TransientSqlStates =
    [
        "40001", // serialization_failure
        "40P01", // deadlock_detected
        "55P03", // lock_not_available
        "08000", // connection_exception
        "08003", // connection_does_not_exist
        "08006", // connection_failure
        "08001", // sqlclient_unable_to_establish_sqlconnection
        "08004", // rejected connection establishment
        "08007", // transaction_resolution_unknown
        "57P01", // admin_shutdown
        "57P02", // crash_shutdown
        "57P03", // cannot_connect_now
        "53300"  // too_many_connections
    ];

    public static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(operation, IsTransientException, MaxRetries, cancellationToken);
    }

    // Test-friendly overload: the retry predicate, retry count and delay budget are injected so
    // unit tests don't have to fabricate provider-specific exceptions or wait out real backoff.
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> shouldRetry,
        int maxRetries,
        CancellationToken cancellationToken,
        TimeSpan? totalDelayBudget = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldRetry);

        var budget = totalDelayBudget ?? DefaultTotalDelayBudget;
        var totalDelay = TimeSpan.Zero;
        var attempt = 0;
        while (true)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (shouldRetry(ex) && attempt < maxRetries && totalDelay < budget)
            {
                attempt++;
                var delay = ComputeBackoff(attempt);
                if (totalDelay + delay > budget)
                    delay = budget - totalDelay;

                totalDelay += delay;
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    internal static bool IsTransientException(Exception ex)
    {
        return ex is NpgsqlException { IsTransient: true } ||
            ex is PostgresException postgresException && IsTransientSqlState(postgresException.SqlState);
    }

    internal static bool IsTransientSqlState(string sqlState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlState);
        return TransientSqlStates.Contains(sqlState);
    }

    internal static bool IsTransientSqlStateForTesting(string sqlState) => TransientSqlStates.Contains(sqlState);

    // Full jitter on an exponential ceiling: each delay lands uniformly in [ceiling / 2, ceiling],
    // so concurrent retries spread out instead of hitting the server in the same instant.
    internal static TimeSpan ComputeBackoff(int attempt)
    {
        var ceiling = ComputeBackoffCeiling(attempt);
        // Not a security decision, but RandomNumberGenerator is what the analyzer set admits and
        // it is cheap at this call rate (one draw per retry, never per request).
        var factor = 0.5 + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1001) / 2000.0;
        return TimeSpan.FromMilliseconds(ceiling.TotalMilliseconds * factor);
    }

    internal static TimeSpan ComputeBackoffCeiling(int attempt)
    {
        var delayMs = Math.Min(BaseDelayMs * Math.Pow(2, attempt - 1), MaxDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
