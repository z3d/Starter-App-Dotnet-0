using FsCheck;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class ProductFuzzTests
{
    private static Arbitrary<int> NonNegativeStock() =>
        Gen.Choose(0, 100_000).ToArbitrary();

    [Property]
    public Property UpdateStock_RoundTrip_ReturnsToOriginal()
    {
        var smallPositiveInt = Gen.Choose(1, 10_000).ToArbitrary();
        return Prop.ForAll(NonNegativeStock(), smallPositiveInt,
            (initialStock, delta) =>
            {
                var product = new Product("Test", "Desc", Money.Create(10m), initialStock);
                product.UpdateStock(delta);
                product.UpdateStock(-delta);
                return product.Stock == initialStock;
            });
    }

    [Property]
    public Property UpdateStock_NegativeBeyondStock_AlwaysThrows()
    {
        return Prop.ForAll(NonNegativeStock(),
            initialStock =>
            {
                var product = new Product("Test", "Desc", Money.Create(10m), initialStock);
                var excess = initialStock + 1;
                try
                { product.UpdateStock(-excess); return false; }
                catch (InvalidOperationException) { return true; }
            });
    }

    [Property]
    public Property ValidInputs_AlwaysCreateProduct()
    {
        var names = Gen.Elements("Widget", "Gadget", "Thing", "Device").ToArbitrary();
        var amounts = Gen.Choose(0, 999_999).Select(i => (decimal)i / 100m).ToArbitrary();
        var stocks = NonNegativeStock();
        return Prop.ForAll(names, amounts, stocks,
            (name, amount, stock) =>
            {
                var product = new Product(name, "Desc", Money.Create(amount), stock);
                return product.Name == name && product.Stock == stock;
            });
    }
}
