using Npgsql;

namespace StarterApp.Api.Infrastructure.Persistence;

// EF's retry policy covers only the DbContext. Dapper reads go through plain ADO.NET, so the
// query handlers wrap them in this helper instead. Dapper reopens a pooled connection on each
// attempt.
// The backoff is randomized so that saturated readers do not retry in lockstep. The total
// delay budget is 10 seconds, down from about 61: a longer failover now fails the request
// instead of holding it open, which the usual 60-second ingress timeout would have done anyway.
// The budget counts the waits between attempts, not the time spent running the query.
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

    // The delay is a random point between half the exponential ceiling and the ceiling, so
    // concurrent retries land at different moments.
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
