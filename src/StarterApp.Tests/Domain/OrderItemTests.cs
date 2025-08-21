namespace StarterApp.Tests.Domain;

public class OrderItemTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateOrderItem()
    {
        // Arrange
        var orderId = 1;
        var productId = 2;
        var productName = "Test Product";
        var quantity = 3;
        var unitPrice = Money.Create(10.50m, "USD");
        var gstRate = 0.15m;

        // Act
        var orderItem = new OrderItem(orderId, productId, productName, quantity, unitPrice, gstRate);

        // Assert
        Assert.Equal(orderId, orderItem.OrderId);
        Assert.Equal(productId, orderItem.ProductId);
        Assert.Equal(productName, orderItem.ProductName);
        Assert.Equal(quantity, orderItem.Quantity);
        Assert.Equal(unitPrice, orderItem.UnitPriceExcludingGst);
        Assert.Equal(gstRate, orderItem.GstRate);
    }

    [Fact]
    public void Constructor_WithDefaultGstRate_ShouldUseDefaultValue()
    {
        // Arrange
        var orderId = 1;
        var productId = 2;
        var productName = "Test Product";
        var quantity = 3;
        var unitPrice = Money.Create(10.50m, "USD");

        // Act
        var orderItem = new OrderItem(orderId, productId, productName, quantity, unitPrice);

        // Assert
        Assert.Equal(OrderItem.DefaultGstRate, orderItem.GstRate);
        Assert.Equal(0.10m, orderItem.GstRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidOrderId_ShouldThrowArgumentOutOfRangeException(int orderId)
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderItem(orderId, 1, "Test Product", 1, unitPrice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidProductId_ShouldThrowArgumentOutOfRangeException(int productId)
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderItem(1, productId, "Test Product", 1, unitPrice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidProductName_ShouldThrowArgumentException(string? productName)
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(1, 1, productName!, 1, unitPrice));
    }

    [Fact]
    public void Constructor_WithNullProductName_ShouldThrowArgumentNullException()
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OrderItem(1, 1, null!, 1, unitPrice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidQuantity_ShouldThrowArgumentOutOfRangeException(int quantity)
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderItem(1, 1, "Test Product", quantity, unitPrice));
    }

    [Fact]
    public void Constructor_WithNullUnitPrice_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OrderItem(1, 1, "Test Product", 1, null!));
    }

    [Fact]
    public void Constructor_WithNegativeGstRate_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var unitPrice = Money.Create(10.50m, "USD");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderItem(1, 1, "Test Product", 1, unitPrice, -0.1m));
    }

    [Fact]
    public void GetUnitPriceIncludingGst_ShouldCalculateCorrectPrice()
    {
        // Arrange
        var unitPrice = Money.Create(100.00m, "USD");
        var gstRate = 0.10m;
        var orderItem = new OrderItem(1, 1, "Test Product", 1, unitPrice, gstRate);

        // Act
        var priceIncludingGst = orderItem.GetUnitPriceIncludingGst();

        // Assert
        Assert.Equal(Money.Create(110.00m, "USD"), priceIncludingGst);
    }

    [Fact]
    public void GetTotalPriceExcludingGst_ShouldCalculateCorrectTotal()
    {
        // Arrange
        var unitPrice = Money.Create(25.00m, "USD");
        var quantity = 4;
        var orderItem = new OrderItem(1, 1, "Test Product", quantity, unitPrice);

        // Act
        var totalPrice = orderItem.GetTotalPriceExcludingGst();

        // Assert
        Assert.Equal(Money.Create(100.00m, "USD"), totalPrice);
    }

    [Fact]
    public void GetTotalPriceIncludingGst_ShouldCalculateCorrectTotal()
    {
        // Arrange
        var unitPrice = Money.Create(50.00m, "USD");
        var quantity = 2;
        var gstRate = 0.20m;
        var orderItem = new OrderItem(1, 1, "Test Product", quantity, unitPrice, gstRate);

        // Act
        var totalPriceIncludingGst = orderItem.GetTotalPriceIncludingGst();

        // Assert
        // Unit price including GST: 50.00 * 1.20 = 60.00
        // Total: 60.00 * 2 = 120.00
        Assert.Equal(Money.Create(120.00m, "USD"), totalPriceIncludingGst);
    }

    [Fact]
    public void GetTotalGstAmount_ShouldCalculateCorrectGstAmount()
    {
        // Arrange
        var unitPrice = Money.Create(100.00m, "USD");
        var quantity = 3;
        var gstRate = 0.15m;
        var orderItem = new OrderItem(1, 1, "Test Product", quantity, unitPrice, gstRate);

        // Act
        var totalGst = orderItem.GetTotalGstAmount();

        // Assert
        // GST per unit: 100.00 * 0.15 = 15.00
        // Total GST: 15.00 * 3 = 45.00
        Assert.Equal(Money.Create(45.00m, "USD"), totalGst);
    }

    [Fact]
    public void SetId_ShouldSetOrderItemId()
    {
        // Arrange
        var unitPrice = Money.Create(10.00m, "USD");
        var orderItem = new OrderItem(1, 1, "Test Product", 1, unitPrice);

        // Act
        orderItem.SetId(456);

        // Assert
        Assert.Equal(456, orderItem.Id);
    }

    [Fact]
    public void DefaultGstRate_ShouldBeCorrectValue()
    {
        // Assert
        Assert.Equal(0.10m, OrderItem.DefaultGstRate);
    }
}

