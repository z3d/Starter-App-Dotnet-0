using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadArchiveCleanupDrainTests
{
    [Fact]
    public async Task DrainAsync_KeepsPagingUntilAShortPage()
    {
        var pages = new Queue<int>([3, 3, 1]);

        var (deleted, budgetExhausted) = await PayloadArchiveCleanupDrain.DrainAsync(
            _ => Task.FromResult(pages.Dequeue()), pageSize: 3, budgetExhausted: static () => false, CancellationToken.None);

        Assert.Equal(7, deleted);
        Assert.False(budgetExhausted);
        Assert.Empty(pages);
    }

    [Fact]
    public async Task DrainAsync_StopsAndReportsWhenTheBudgetIsSpent()
    {
        var calls = 0;

        var (deleted, budgetExhausted) = await PayloadArchiveCleanupDrain.DrainAsync(
            _ => Task.FromResult(++calls >= 0 ? 3 : 0), pageSize: 3, budgetExhausted: () => calls >= 2, CancellationToken.None);

        Assert.Equal(6, deleted);
        Assert.True(budgetExhausted);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DrainAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PayloadArchiveCleanupDrain.DrainAsync(
            _ => Task.FromResult(3), pageSize: 3, budgetExhausted: static () => false, cts.Token));
    }
}
