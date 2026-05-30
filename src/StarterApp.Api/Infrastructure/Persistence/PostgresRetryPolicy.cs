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
public static class PostgresRetryPolicy
{
    private const int MaxRetries = 6;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30_000;

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

    // Test-friendly overload: the retry predicate and retry count are injected so unit tests
    // don't have to fabricate provider-specific exceptions.
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> shouldRetry,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldRetry);

        var attempt = 0;
        while (true)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (shouldRetry(ex) && attempt < maxRetries)
            {
                attempt++;
                var delay = ComputeBackoff(attempt);
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

    internal static TimeSpan ComputeBackoff(int attempt)
    {
        var delayMs = Math.Min(BaseDelayMs * Math.Pow(2, attempt - 1), MaxDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
