using FsCheck;
using FsCheck.Fluent;
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
                var product = TestEntities.Product("Test", "Desc", Money.Create(10m), initialStock);
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
                var product = TestEntities.Product("Test", "Desc", Money.Create(10m), initialStock);
                var excess = initialStock + 1;
                try
                { product.UpdateStock(-excess); return false; }
                catch (DomainRuleException) { return true; }
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
                var product = TestEntities.Product(name, "Desc", Money.Create(amount), stock);
                return product.Name == name && product.Stock == stock;
            });
    }

    // ---- P1b: name / description length boundaries ----

    [Fact]
    public void NameAtMaxLength_IsAccepted()
    {
        var name = new string('n', Product.MaxNameLength);
        var product = TestEntities.Product(name, "Desc", Money.Create(10m), 1);
        Assert.Equal(name, product.Name);
    }

    [Property(MaxTest = 500)]
    public Property NameOverMaxLength_AlwaysThrows()
    {
        var overLength = Gen.Choose(Product.MaxNameLength + 1, Product.MaxNameLength + 1_000).ToArbitrary();
        return Prop.ForAll(overLength,
            length =>
            {
                var name = new string('n', length);
                try
                { TestEntities.Product(name, "Desc", Money.Create(10m), 1); return false; }
                catch (ArgumentException) { return true; }
            });
    }

    [Fact]
    public void DescriptionAtMaxLength_IsAccepted()
    {
        var description = new string('d', Product.MaxDescriptionLength);
        var product = TestEntities.Product("Name", description, Money.Create(10m), 1);
        Assert.Equal(description, product.Description);
    }

    [Property(MaxTest = 500)]
    public Property DescriptionOverMaxLength_AlwaysThrows()
    {
        var overLength = Gen.Choose(Product.MaxDescriptionLength + 1, Product.MaxDescriptionLength + 1_000).ToArbitrary();
        return Prop.ForAll(overLength,
            length =>
            {
                var description = new string('d', length);
                try
                { TestEntities.Product("Name", description, Money.Create(10m), 1); return false; }
                catch (ArgumentException) { return true; }
            });
    }
}
