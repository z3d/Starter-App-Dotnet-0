using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class OrderStateMachineFuzzTests
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> ValidTransitions = new()
    {
        { OrderStatus.Pending, [OrderStatus.Confirmed, OrderStatus.Cancelled] },
        { OrderStatus.Confirmed, [OrderStatus.Processing, OrderStatus.Cancelled] },
        { OrderStatus.Processing, [OrderStatus.Shipped, OrderStatus.Cancelled] },
        { OrderStatus.Shipped, [OrderStatus.Delivered] },
        { OrderStatus.Delivered, [] },
        { OrderStatus.Cancelled, [] },
    };

    private static Gen<List<OrderStatus>> ValidTransitionPath()
    {
        return Gen.Choose(1, 5).SelectMany(steps => BuildPath([], OrderStatus.Pending, steps));
    }

    private static Gen<List<OrderStatus>> BuildPath(List<OrderStatus> path, OrderStatus current, int remaining)
    {
        if (remaining <= 0 || ValidTransitions[current].Length == 0)
            return Gen.Constant(path);

        return Gen.Elements(ValidTransitions[current]).SelectMany(next =>
        {
            var newPath = new List<OrderStatus>(path) { next };
            return BuildPath(newPath, next, remaining - 1);
        });
    }

    [Property]
    public Property ValidTransitionPaths_NeverThrow()
    {
        return Prop.ForAll(ValidTransitionPath().ToArbitrary(),
            path =>
            {
                var order = TestEntities.Order(1);
                foreach (var status in path)
                {
                    order.UpdateStatus(status);
                }
                return Enum.IsDefined(order.Status);
            });
    }

    [Property]
    public Property InvalidTransitions_AlwaysThrow()
    {
        var allStatuses = Enum.GetValues<OrderStatus>();

        var scenario = Gen.Elements(allStatuses)
            .Where(s => ValidTransitions[s].Length < allStatuses.Length - 1)
            .SelectMany(current =>
            {
                var invalid = allStatuses
                    .Where(s => !ValidTransitions[current].Contains(s) && s != current)
                    .ToArray();
                return Gen.Elements(invalid).Select(target => (current, target));
            })
            .ToArbitrary();

        return Prop.ForAll(scenario,
            (ValueTuple<OrderStatus, OrderStatus> pair) =>
            {
                var (current, target) = pair;
                var order = Order.Reconstitute(Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow, current, DateTimeOffset.UtcNow, []);
                try
                { order.UpdateStatus(target); return false; }
                catch (InvalidOperationException) { return true; }
            });
    }

    [Property]
    public Property Reconstitute_PreservesAllProperties()
    {
        var customerIds = Gen.Choose(1, 10_000).ToArbitrary();
        var statuses = Gen.Elements(Enum.GetValues<OrderStatus>()).ToArbitrary();
        return Prop.ForAll(customerIds, statuses,
            (customerId, status) =>
            {
                var id = Guid.CreateVersion7();
                var orderDate = DateTimeOffset.UtcNow.AddDays(-1);
                var lastUpdated = DateTimeOffset.UtcNow;
                var items = new List<OrderItem>
                {
                    new(id, 1, "Fuzz Product", 1, Money.Create(10m, "USD"))
                };

                var order = Order.Reconstitute(id, customerId, orderDate, status, lastUpdated, items);

                return order.Id == id
                    && order.CustomerId == customerId
                    && order.OrderDate == orderDate
                    && order.Status == status
                    && order.LastUpdated == lastUpdated
                    && order.Items.Count == 1;
            });
    }

    [Property]
    public Property OrderTotals_EqualSumOfItemTotals()
    {
        var itemsGen = Gen.Choose(1, 5).SelectMany(count =>
            Gen.ArrayOf(
                Gen.Choose(1, 10000).Select(i => (decimal)i / 100m)
                    .SelectMany(amount => Gen.Choose(1, 10)
                        .SelectMany(qty => Gen.Elements(0.00m, 0.05m, 0.10m, 0.15m, 0.20m)
                            .Select(gst => (Amount: amount, Quantity: qty, GstRate: gst)))),
                count));

        return Prop.ForAll(itemsGen.ToArbitrary(),
            itemSpecs =>
            {
                var order = TestEntities.Order(1);
                for (var i = 0; i < itemSpecs.Length; i++)
                {
                    var spec = itemSpecs[i];
                    var item = new OrderItem(order.Id, i + 1, $"Product {i + 1}", spec.Quantity,
                        Money.Create(spec.Amount, "USD"), spec.GstRate);
                    order.AddItem(item);
                }

                var expectedExcGst = order.Items.Sum(i => i.GetTotalPriceExcludingGst().Amount);
                var expectedIncGst = order.Items.Sum(i => i.GetTotalPriceIncludingGst().Amount);
                var expectedGst = order.Items.Sum(i => i.GetTotalGstAmount().Amount);

                return order.GetTotalExcludingGst().Amount == expectedExcGst
                    && order.GetTotalIncludingGst().Amount == expectedIncGst
                    && order.GetTotalGstAmount().Amount == expectedGst;
            });
    }

    // ---- P1b: MaxItems boundary ----

    [Fact]
    public void AddingUpToMaxItems_Succeeds_AndOneMoreThrows()
    {
        var order = TestEntities.Order(1);
        for (var i = 1; i <= Order.MaxItems; i++)
            order.AddItem(i, $"Product {i}", 1, Money.Create(10m, "USD"));

        Assert.Equal(Order.MaxItems, order.Items.Count);

        // The (MaxItems + 1)-th DISTINCT product must throw (AddItem replaces same-ProductId items,
        // so a new ProductId is required to actually exceed the cap).
        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(Order.MaxItems + 1, "Overflow", 1, Money.Create(10m, "USD")));
    }

    [Property(MaxTest = 500)]
    public Property AddingMoreThanMaxDistinctItems_AlwaysThrows()
    {
        var overflow = Gen.Choose(1, 50).ToArbitrary();
        return Prop.ForAll(overflow,
            extra =>
            {
                var order = TestEntities.Order(1);
                for (var i = 1; i <= Order.MaxItems; i++)
                    order.AddItem(i, $"Product {i}", 1, Money.Create(10m, "USD"));

                try
                {
                    // Add `extra` further DISTINCT products beyond the cap; the very first must throw.
                    order.AddItem(Order.MaxItems + extra, "Overflow", 1, Money.Create(10m, "USD"));
                    return false;
                }
                catch (InvalidOperationException) { return true; }
            });
    }

    // ---- P2a: currency mismatch across order items ----

    [Property(MaxTest = 500)]
    public Property AddingItemWithMismatchedCurrency_AlwaysThrows()
    {
        var currencyPair = Gen.Elements("USD", "EUR", "GBP", "AUD", "NZD")
            .SelectMany(first => Gen.Elements("USD", "EUR", "GBP", "AUD", "NZD")
                .Where(second => second != first)
                .Select(second => (First: first, Second: second)))
            .ToArbitrary();

        return Prop.ForAll(currencyPair,
            pair =>
            {
                var order = TestEntities.Order(1);
                order.AddItem(1, "First", 1, Money.Create(10m, pair.First));
                try
                {
                    order.AddItem(2, "Second", 1, Money.Create(10m, pair.Second));
                    return false;
                }
                catch (InvalidOperationException) { return true; }
            });
    }
}
