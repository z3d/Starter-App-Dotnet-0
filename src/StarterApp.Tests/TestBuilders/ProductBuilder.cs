namespace StarterApp.Tests.TestBuilders;

public class ProductBuilder
{
    private string _name = "Default Product";
    private string _description = "Default Description";
    private Money _price = Money.Create(9.99m);
    private int _stock = 10;

    public ProductBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ProductBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ProductBuilder WithPrice(Money price)
    {
        _price = price;
        return this;
    }

    public ProductBuilder WithPrice(decimal amount)
    {
        _price = Money.Create(amount);
        return this;
    }

    public ProductBuilder WithStock(int stock)
    {
        _stock = stock;
        return this;
    }

    public Product Build()
    {
        return new Product(_name, _description, _price, _stock);
    }

    // Common product states
    public static ProductBuilder AValidProduct() => new ProductBuilder();

    public static ProductBuilder AnOutOfStockProduct() => new ProductBuilder().WithStock(0);

    public static ProductBuilder AnExpensiveProduct() => new ProductBuilder().WithPrice(999.99m);
}