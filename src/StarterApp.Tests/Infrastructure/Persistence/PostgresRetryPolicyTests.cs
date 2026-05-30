using StarterApp.Api.Infrastructure.Persistence;

namespace StarterApp.Tests.Infrastructure.Persistence;

public class PostgresRetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsResult_OnSuccessFirstAttempt()
    {
        var callCount = 0;
        var result = await PostgresRetryPolicy.ExecuteAsync(_ =>
        {
            callCount++;
            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_WhenExceptionIsNotTransientPostgresException()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresRetryPolicy.ExecuteAsync<int>(_ =>
            {
                callCount++;
                throw new InvalidOperationException("not transient");
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
    }

    // The overload below takes an explicit predicate so the test can simulate transient
    // behaviour without fabricating provider-specific exceptions.

    [Fact]
    public async Task ExecuteAsync_RetriesUpToMaxAttempts_WhenPredicateMatches()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresRetryPolicy.ExecuteAsync<int>(
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

        var result = await PostgresRetryPolicy.ExecuteAsync(
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
            PostgresRetryPolicy.ExecuteAsync<int>(
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

        var task = PostgresRetryPolicy.ExecuteAsync<int>(
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
    [InlineData("40001")] // serialization_failure
    [InlineData("40P01")] // deadlock_detected
    [InlineData("55P03")] // lock_not_available
    [InlineData("08000")] // connection_exception
    [InlineData("08006")] // connection_failure
    [InlineData("57P03")] // cannot_connect_now
    [InlineData("53300")] // too_many_connections
    public void IsTransientSqlState_RecognizesKnownTransientCodes(string sqlState)
    {
        Assert.True(PostgresRetryPolicy.IsTransientSqlStateForTesting(sqlState),
            $"SQLSTATE {sqlState} should be classified as transient.");
    }

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("23503")] // foreign_key_violation
    [InlineData("22001")] // string_data_right_truncation
    [InlineData("00000")] // successful_completion
    public void IsTransientSqlState_RejectsNonTransientCodes(string sqlState)
    {
        Assert.False(PostgresRetryPolicy.IsTransientSqlStateForTesting(sqlState),
            $"SQLSTATE {sqlState} must not be classified as transient; retrying would mask a real logical error.");
    }
}
