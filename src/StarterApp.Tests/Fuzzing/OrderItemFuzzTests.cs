using FsCheck;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class OrderItemFuzzTests
{
    private static Arbitrary<int> PositiveQuantity() =>
        Gen.Choose(1, 1_000).ToArbitrary();

    private static Arbitrary<decimal> ValidGstRate() =>
        Gen.Elements(0.00m, 0.05m, 0.10m, 0.15m, 0.20m, 0.25m, 1.0m)
           .ToArbitrary();

    private static Arbitrary<decimal> ValidPrice() =>
        Gen.Choose(0, 9_999_900).Select(i => (decimal)i / 100m).ToArbitrary();

    [Property]
    public Property PriceInvariant_TotalIncGst_Equals_UnitIncGst_Times_Quantity()
    {
        return Prop.ForAll(PositiveQuantity(), ValidPrice(), ValidGstRate(),
            (quantity, amount, gstRate) =>
            {
                var unitPrice = Money.Create(amount, "USD");
                var item = new OrderItem(1, 1, "Product", quantity, unitPrice, gstRate);

                var totalIncGst = item.GetTotalPriceIncludingGst().Amount;
                var unitIncGst = item.GetUnitPriceIncludingGst().Amount;

                return (totalIncGst == unitIncGst * quantity)
                    .Label($"TotalIncGst={totalIncGst} != UnitIncGst={unitIncGst} * Qty={quantity}");
            });
    }

    [Property]
    public Property QuantityTimesUnitPrice_EqualsTotalExcGst()
    {
        return Prop.ForAll(PositiveQuantity(), ValidPrice(),
            (quantity, amount) =>
            {
                var unitPrice = Money.Create(amount, "USD");
                var item = new OrderItem(1, 1, "Product", quantity, unitPrice);
                return item.GetTotalPriceExcludingGst().Amount == unitPrice.Amount * quantity;
            });
    }

    [Property]
    public Property GstRateOverOne_AlwaysThrows()
    {
        var invalidGstRate = Gen.Choose(101, 1000).Select(i => (decimal)i / 100m).ToArbitrary();
        return Prop.ForAll(invalidGstRate,
            gstRate =>
            {
                var unitPrice = Money.Create(10m, "USD");
                try
                { new OrderItem(1, 1, "Product", 1, unitPrice, gstRate); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Property]
    public Property ZeroOrNegativeIds_AlwaysThrow()
    {
        var invalidId = Gen.Choose(-10_000, 0).ToArbitrary();
        return Prop.ForAll(invalidId,
            id =>
            {
                var unitPrice = Money.Create(10m, "USD");
                try
                { new OrderItem(id, 1, "Product", 1, unitPrice); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Property]
    public Property ZeroOrNegativeQuantity_AlwaysThrows()
    {
        var invalidQty = Gen.Choose(-10_000, 0).ToArbitrary();
        return Prop.ForAll(invalidQty,
            qty =>
            {
                var unitPrice = Money.Create(10m, "USD");
                try
                { new OrderItem(1, 1, "Product", qty, unitPrice); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }
}
