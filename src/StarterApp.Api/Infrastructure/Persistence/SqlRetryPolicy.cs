using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Infrastructure.Persistence;

// Query-time retry policy for Dapper/ADO.NET calls against Azure SQL.
//
// Why this exists: EF Core's EnableRetryOnFailure is scoped to ApplicationDbContext —
// it does not protect Dapper reads because Dapper creates its own SqlCommands with
// RetryLogicProvider = null. Without this helper, a single transient fault on a read
// (mid-query failover, throttling, network blip) surfaces as a 500 to the client,
// while writes (EF) are transparently retried. This closes that asymmetry.
//
// Semantics mirror EF's SqlServerRetryingExecutionStrategy: up to 6 attempts,
// exponential backoff capped at 30s, same transient error numbers as Microsoft's
// connection-resiliency guidance.
//
// The operation Func is invoked per attempt — Dapper reopens the connection from
// the pool on each call, so broken connections are recycled automatically.
public static class SqlRetryPolicy
{
    private const int MaxRetries = 6;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30_000;

    // Transient errors classified by Microsoft.EntityFrameworkCore.SqlServer's default
    // retry strategy. Source: dotnet/efcore SqlServerTransientExceptionDetector.
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        49920, 49919, 49918,              // "Cannot process request" / throttling
        41839, 41325, 41305, 41302, 41301, // In-memory OLTP transaction conflicts
        40613, 40501, 40197, 40143,       // Azure SQL service unavailable / busy / failover
        11001,                            // Host unknown
        10929, 10928,                     // Resource limits reached
        10060, 10054, 10053,              // Network transport errors
        233, 121, 64,                     // Connection broken
        20,                               // Instance does not support encryption
        -2,                               // Timeout
        4060,                             // Cannot open database requested by login
        4221,                             // Login to secondary failed
        615, 926,                         // Database unavailable / in recovery
    ];

    public static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(operation, IsTransientException, MaxRetries, cancellationToken);
    }

    // Test-friendly overload: the retry predicate and retry count are injected so unit tests
    // don't have to fabricate SqlException instances (which have no public constructors).
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
        return ex is SqlException sqlEx && IsTransientSqlException(sqlEx);
    }

    internal static bool IsTransientSqlException(SqlException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        foreach (SqlError error in ex.Errors)
            if (TransientErrorNumbers.Contains(error.Number))
                return true;
        return false;
    }

    internal static bool IsTransientErrorNumber(int number) => TransientErrorNumbers.Contains(number);

    internal static TimeSpan ComputeBackoff(int attempt)
    {
        var delayMs = Math.Min(BaseDelayMs * Math.Pow(2, attempt - 1), MaxDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
