using StarterApp.Api.Infrastructure.Persistence;

namespace StarterApp.Tests.Infrastructure.Persistence;

public class SqlRetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsResult_OnSuccessFirstAttempt()
    {
        var callCount = 0;
        var result = await SqlRetryPolicy.ExecuteAsync(_ =>
        {
            callCount++;
            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_WhenExceptionIsNotTransientSqlException()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlRetryPolicy.ExecuteAsync<int>(_ =>
            {
                callCount++;
                throw new InvalidOperationException("not transient");
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
    }

    // The overload below takes an explicit predicate so the test can simulate transient
    // behaviour without fabricating a SqlException (which has no public constructors).

    [Fact]
    public async Task ExecuteAsync_RetriesUpToMaxAttempts_WhenPredicateMatches()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlRetryPolicy.ExecuteAsync<int>(
                _ => { callCount++; throw new InvalidOperationException(); },
                _ => true,
                maxRetries: 3,
                CancellationToken.None));

        // Initial attempt + 3 retries = 4 total.
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAfterRecovery_MidRetry()
    {
        var callCount = 0;

        var result = await SqlRetryPolicy.ExecuteAsync(
            _ =>
            {
                callCount++;
                if (callCount < 3)
                    throw new InvalidOperationException("transient");
                return Task.FromResult("recovered");
            },
            _ => true,
            maxRetries: 6,
            CancellationToken.None);

        Assert.Equal("recovered", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_WhenPredicateReturnsFalse()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlRetryPolicy.ExecuteAsync<int>(
                _ => { callCount++; throw new InvalidOperationException("not classified transient"); },
                _ => false,
                maxRetries: 6,
                CancellationToken.None));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_DuringBackoff()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        var task = SqlRetryPolicy.ExecuteAsync<int>(
            _ =>
            {
                callCount++;
                if (callCount == 1)
                    cts.CancelAfter(TimeSpan.FromMilliseconds(50));
                throw new InvalidOperationException("transient");
            },
            _ => true,
            maxRetries: 6,
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData(40613)] // Azure SQL: Database not currently available
    [InlineData(40501)] // Azure SQL: Service busy
    [InlineData(40197)] // Azure SQL: Service error
    [InlineData(10928)] // Resource limit reached
    [InlineData(10929)] // Resource limit reached (session count)
    [InlineData(-2)]    // Timeout
    [InlineData(4060)]  // Cannot open database
    [InlineData(233)]   // Connection broken
    public void IsTransientErrorNumber_RecognizesKnownTransientCodes(int errorNumber)
    {
        Assert.True(SqlRetryPolicy.IsTransientErrorNumber(errorNumber),
            $"Error number {errorNumber} should be classified as transient (matches EF's SqlServerRetryingExecutionStrategy).");
    }

    [Theory]
    [InlineData(2627)]  // UNIQUE KEY violation — not transient
    [InlineData(547)]   // Foreign key constraint — not transient
    [InlineData(8152)]  // String would be truncated — not transient
    [InlineData(0)]     // Unknown
    public void IsTransientErrorNumber_RejectsNonTransientCodes(int errorNumber)
    {
        Assert.False(SqlRetryPolicy.IsTransientErrorNumber(errorNumber),
            $"Error number {errorNumber} must not be classified as transient — retrying would mask a real logical error.");
    }
}
