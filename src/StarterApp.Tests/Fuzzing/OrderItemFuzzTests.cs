using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class OrderItemFuzzTests
{
    private static readonly Guid TestOrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Arbitrary<int> PositiveQuantity() =>
        Gen.Choose(1, 1_000).ToArbitrary();

    private static Arbitrary<decimal> ValidGstRate() =>
        Gen.Elements(0.00m, 0.05m, 0.10m, 0.15m, 0.20m, 0.25m, 1.0m)
           .ToArbitrary();

    private static Arbitrary<decimal> ValidPrice() =>
        Gen.Choose(0, 9_999_900).Select(i => (decimal)i / 100m).ToArbitrary();

    // Full DECIMAL(5,4) rate range (0..1.0000) so sub-cent GST residues are exercised.
    private static Arbitrary<decimal> AnyValidGstRate() =>
        Gen.Choose(0, 10_000).Select(i => (decimal)i / 10_000m).ToArbitrary();

    private static bool IsWholeCents(decimal amount) => amount == decimal.Round(amount, 2);

    [Property]
    public Property AllComputedMoney_IsAlwaysWholeCents()
    {
        return Prop.ForAll(PositiveQuantity(), ValidPrice(), AnyValidGstRate(),
            (quantity, amount, gstRate) =>
            {
                var item = new OrderItem(TestOrderId, 1, "Product", quantity, Money.Create(amount, "USD"), gstRate);
                return (IsWholeCents(item.GetUnitPriceIncludingGst().Amount)
                        && IsWholeCents(item.GetTotalPriceExcludingGst().Amount)
                        && IsWholeCents(item.GetTotalPriceIncludingGst().Amount)
                        && IsWholeCents(item.GetTotalGstAmount().Amount))
                    .Label($"Sub-cent value produced for amount={amount}, gstRate={gstRate}, qty={quantity}");
            });
    }

    [Property]
    public Property LineTotalInclusive_EqualsExclusivePlusGst()
    {
        return Prop.ForAll(PositiveQuantity(), ValidPrice(), AnyValidGstRate(),
            (quantity, amount, gstRate) =>
            {
                var item = new OrderItem(TestOrderId, 1, "Product", quantity, Money.Create(amount, "USD"), gstRate);
                var incl = item.GetTotalPriceIncludingGst().Amount;
                var excl = item.GetTotalPriceExcludingGst().Amount;
                var gst = item.GetTotalGstAmount().Amount;
                return (incl == excl + gst).Label($"Incl={incl} != Excl={excl} + Gst={gst}");
            });
    }

    [Property]
    public Property PriceInvariant_TotalIncGst_Equals_UnitIncGst_Times_Quantity()
    {
        return Prop.ForAll(PositiveQuantity(), ValidPrice(), ValidGstRate(),
            (quantity, amount, gstRate) =>
            {
                var unitPrice = Money.Create(amount, "USD");
                var item = new OrderItem(TestOrderId, 1, "Product", quantity, unitPrice, gstRate);

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
                var item = new OrderItem(TestOrderId, 1, "Product", quantity, unitPrice);
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
                { _ = new OrderItem(TestOrderId, 1, "Product", 1, unitPrice, gstRate); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Property]
    public Property ZeroOrNegativeProductId_AlwaysThrows()
    {
        var invalidId = Gen.Choose(-10_000, 0).ToArbitrary();
        return Prop.ForAll(invalidId,
            productId =>
            {
                var unitPrice = Money.Create(10m, "USD");
                try
                { _ = new OrderItem(TestOrderId, productId, "Product", 1, unitPrice); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Fact]
    public void EmptyOrderId_Throws()
    {
        var unitPrice = Money.Create(10m, "USD");
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.Empty, 1, "Product", 1, unitPrice));
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
                { _ = new OrderItem(TestOrderId, 1, "Product", qty, unitPrice); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }
}
