using FsCheck;
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
                var order = new Order(1);
                foreach (var status in path)
                {
                    order.UpdateStatus(status);
                }
                return Enum.IsDefined(typeof(OrderStatus), order.Status);
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
            pair =>
            {
                var (current, target) = pair;
                var order = Order.Reconstitute(1, 1, DateTime.UtcNow, current, DateTime.UtcNow, []);
                try
                { order.UpdateStatus(target); return false; }
                catch (InvalidOperationException) { return true; }
            });
    }

    [Property]
    public Property Reconstitute_PreservesAllProperties()
    {
        var ids = Gen.Choose(1, 10_000).ToArbitrary();
        var statuses = Gen.Elements(Enum.GetValues<OrderStatus>()).ToArbitrary();
        return Prop.ForAll(ids, ids, statuses,
            (id, customerId, status) =>
            {
                var orderDate = DateTime.UtcNow.AddDays(-1);
                var lastUpdated = DateTime.UtcNow;
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
            Gen.ArrayOf(count,
                Gen.Choose(1, 10000).Select(i => (decimal)i / 100m)
                    .SelectMany(amount => Gen.Choose(1, 10)
                        .SelectMany(qty => Gen.Elements(0.00m, 0.05m, 0.10m, 0.15m, 0.20m)
                            .Select(gst => (Amount: amount, Quantity: qty, GstRate: gst))))));

        return Prop.ForAll(itemsGen.ToArbitrary(),
            itemSpecs =>
            {
                var order = new Order(1);
                for (var i = 0; i < itemSpecs.Length; i++)
                {
                    var spec = itemSpecs[i];
                    var item = new OrderItem(1, i + 1, $"Product {i + 1}", spec.Quantity,
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
}
