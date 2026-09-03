namespace StarterApp.Tests.Domain;

public class OrderLineTotalTests
{
    // Before the guard these surfaced as a bare ArgumentOutOfRangeException from Money.Create
    // inside OrderCreatedDomainEvent during outbox capture, after stock had been reserved. The
    // event computes the GST-inclusive ORDER total, so the guard must check that, not a single
    // ex-GST line: quantity 1 at the ceiling already overflows once GST is added.
    [Fact]
    public void AddItem_WhenGstInclusiveTotalExceedsMaxAmount_ShouldThrowDomainRuleException()
    {
        var order = TestEntities.Order(1);

        var ex = Assert.Throws<DomainRuleException>(() => order.AddItem(1, "Bulk", 1, Money.Create(Money.MaxAmount)));

        Assert.Contains("order total", ex.Message, StringComparison.Ordinal);
        Assert.Empty(order.Items);
    }

    [Fact]
    public void AddItem_WhenSecondLineTakesTheOrderPastMaxAmount_ShouldThrowAndLeaveTheOrderUntouched()
    {
        var order = TestEntities.Order(1);
        var half = Money.Create(5_000_000_000_000_000m);
        order.AddItem(1, "First", 1, half);

        Assert.Throws<DomainRuleException>(() => order.AddItem(2, "Second", 1, half));

        Assert.Single(order.Items);
        Assert.Equal(1, order.Items[0].ProductId);
    }

    [Fact]
    public void AddItem_WithinMaxAmount_ShouldSucceed()
    {
        var order = TestEntities.Order(1);

        order.AddItem(1, "Fine", 2, Money.Create(1_000m));

        Assert.Single(order.Items);
        Assert.True(order.GetTotalIncludingGst().Amount <= Money.MaxAmount);
    }
}
