namespace StarterApp.Tests.Domain;

public class OrderLineTotalTests
{
    [Fact]
    public void AddItem_WhenLineTotalExceedsMaxAmount_ShouldThrowDomainRuleException()
    {
        // Before the guard this surfaced as a bare ArgumentOutOfRangeException from Money.Create
        // inside OrderCreatedDomainEvent during outbox capture, after stock had been reserved.
        var order = TestEntities.Order(1);
        var unitPrice = Money.Create(Money.MaxAmount);

        var ex = Assert.Throws<DomainRuleException>(() => order.AddItem(1, "Bulk", 2, unitPrice));

        Assert.Contains("Line total", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderItem_WithQuantityAboveMaxQuantity_ShouldThrowArgumentOutOfRangeException()
    {
        var order = TestEntities.Order(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.AddItem(1, "Bulk", OrderItem.MaxQuantity + 1, Money.Create(1m)));
    }
}
