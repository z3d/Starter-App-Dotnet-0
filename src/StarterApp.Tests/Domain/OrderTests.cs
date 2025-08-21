namespace StarterApp.Tests.Domain;

public class OrderTests
{
    [Fact]
    public void Constructor_WithValidCustomerId_ShouldCreateOrder()
    {
        // Arrange
        var customerId = 1;

        // Act
        var order = new Order(customerId);

        // Assert
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(order.Items);
        Assert.True(order.OrderDate <= DateTime.UtcNow);
        Assert.True(order.LastUpdated <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidCustomerId_ShouldThrowArgumentOutOfRangeException(int customerId)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new Order(customerId));
    }

    [Fact]
    public void AddItem_WithValidItem_ShouldAddItemToOrder()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var orderItem = new OrderItem(1, 1, "Test Product", 2, money);

        // Act
        order.AddItem(orderItem);

        // Assert
        Assert.Single(order.Items);
        Assert.Contains(orderItem, order.Items);
    }

    [Fact]
    public void AddItem_WithNullItem_ShouldThrowArgumentNullException()
    {
        // Arrange
        var order = new Order(1);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => order.AddItem(null!));
    }

    [Fact]
    public void AddItem_WhenOrderIsNotPending_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var order = new Order(1);
        order.UpdateStatus(OrderStatus.Confirmed);
        var money = Money.Create(10.00m, "USD");
        var orderItem = new OrderItem(1, 1, "Test Product", 2, money);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => order.AddItem(orderItem));
        Assert.Equal("Cannot add items to a non-pending order", exception.Message);
    }

    [Fact]
    public void AddItem_WithSameProductId_ShouldReplaceExistingItem()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var firstItem = new OrderItem(1, 1, "Test Product", 2, money);
        var secondItem = new OrderItem(1, 1, "Test Product", 3, money);

        // Act
        order.AddItem(firstItem);
        order.AddItem(secondItem);

        // Assert
        Assert.Single(order.Items);
        Assert.Equal(3, order.Items.First().Quantity);
        Assert.DoesNotContain(firstItem, order.Items);
        Assert.Contains(secondItem, order.Items);
    }

    [Fact]
    public void RemoveItem_WithValidProductId_ShouldRemoveItem()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var orderItem = new OrderItem(1, 1, "Test Product", 2, money);
        order.AddItem(orderItem);

        // Act
        order.RemoveItem(1);

        // Assert
        Assert.Empty(order.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RemoveItem_WithInvalidProductId_ShouldThrowArgumentOutOfRangeException(int productId)
    {
        // Arrange
        var order = new Order(1);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => order.RemoveItem(productId));
    }

    [Fact]
    public void RemoveItem_WhenOrderIsNotPending_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var order = new Order(1);
        order.UpdateStatus(OrderStatus.Confirmed);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => order.RemoveItem(1));
        Assert.Equal("Cannot remove items from a non-pending order", exception.Message);
    }

    [Fact]
    public void RemoveItem_WithNonExistentProductId_ShouldNotThrow()
    {
        // Arrange
        var order = new Order(1);

        // Act & Assert - Should not throw
        order.RemoveItem(999);
        Assert.Empty(order.Items);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Processing, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Processing, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Processing, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, false)]
    public void UpdateStatus_WithValidAndInvalidTransitions_ShouldBehaveCorrectly(
        OrderStatus currentStatus, OrderStatus newStatus, bool shouldSucceed)
    {
        // Arrange
        var order = new Order(1);
        if (currentStatus != OrderStatus.Pending)
        {
            // Manually set up the order in the desired state
            order.LoadFromDatabase(DateTime.UtcNow, currentStatus, DateTime.UtcNow, []);
        }

        // Act & Assert
        if (shouldSucceed)
        {
            order.UpdateStatus(newStatus);
            Assert.Equal(newStatus, order.Status);
        }
        else
        {
            var exception = Assert.Throws<InvalidOperationException>(() => order.UpdateStatus(newStatus));
            Assert.Contains($"Cannot transition from {currentStatus} to {newStatus}", exception.Message);
        }
    }

    [Fact]
    public void Confirm_WithItems_ShouldConfirmOrder()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var orderItem = new OrderItem(1, 1, "Test Product", 2, money);
        order.AddItem(orderItem);

        // Act
        order.Confirm();

        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_WithNoItems_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var order = new Order(1);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => order.Confirm());
        Assert.Equal("Cannot confirm an order with no items", exception.Message);
    }

    [Fact]
    public void Cancel_FromPendingStatus_ShouldCancelOrder()
    {
        // Arrange
        var order = new Order(1);

        // Act
        order.Cancel();

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_FromDeliveredStatus_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var order = new Order(1);
        order.LoadFromDatabase(DateTime.UtcNow, OrderStatus.Delivered, DateTime.UtcNow, []);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => order.Cancel());
        Assert.Equal("Cannot cancel a delivered order", exception.Message);
    }

    [Fact]
    public void GetTotalExcludingGst_WithNoItems_ShouldReturnZero()
    {
        // Arrange
        var order = new Order(1);

        // Act
        var total = order.GetTotalExcludingGst();

        // Assert
        Assert.Equal(Money.Create(0), total);
    }

    [Fact]
    public void GetTotalExcludingGst_WithItems_ShouldCalculateCorrectTotal()
    {
        // Arrange
        var order = new Order(1);
        var money1 = Money.Create(10.00m, "USD");
        var money2 = Money.Create(20.00m, "USD");
        var item1 = new OrderItem(1, 1, "Product 1", 2, money1); // 2 * 10.00 = 20.00
        var item2 = new OrderItem(1, 2, "Product 2", 1, money2); // 1 * 20.00 = 20.00
        
        order.AddItem(item1);
        order.AddItem(item2);

        // Act
        var total = order.GetTotalExcludingGst();

        // Assert
        Assert.Equal(Money.Create(40.00m, "USD"), total);
    }

    [Fact]
    public void GetTotalIncludingGst_WithItems_ShouldCalculateCorrectTotal()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var item = new OrderItem(1, 1, "Product 1", 2, money, 0.10m); // 2 * 10.00 * 1.10 = 22.00
        
        order.AddItem(item);

        // Act
        var total = order.GetTotalIncludingGst();

        // Assert
        Assert.Equal(Money.Create(22.00m, "USD"), total);
    }

    [Fact]
    public void GetTotalGstAmount_WithItems_ShouldCalculateCorrectGstAmount()
    {
        // Arrange
        var order = new Order(1);
        var money = Money.Create(10.00m, "USD");
        var item = new OrderItem(1, 1, "Product 1", 2, money, 0.10m); // 2 * 10.00 * 0.10 = 2.00
        
        order.AddItem(item);

        // Act
        var gstTotal = order.GetTotalGstAmount();

        // Assert
        Assert.Equal(Money.Create(2.00m, "USD"), gstTotal);
    }

    [Fact]
    public void SetId_ShouldSetOrderId()
    {
        // Arrange
        var order = new Order(1);

        // Act
        order.SetId(123);

        // Assert
        Assert.Equal(123, order.Id);
    }

    [Fact]
    public void LoadFromDatabase_ShouldSetAllPropertiesCorrectly()
    {
        // Arrange
        var order = new Order(1);
        var orderDate = DateTime.UtcNow.AddDays(-1);
        var lastUpdated = DateTime.UtcNow;
        var money = Money.Create(10.00m, "USD");
        var items = new List<OrderItem>
        {
            new OrderItem(1, 1, "Product 1", 2, money)
        };

        // Act
        order.LoadFromDatabase(orderDate, OrderStatus.Confirmed, lastUpdated, items);

        // Assert
        Assert.Equal(orderDate, order.OrderDate);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(lastUpdated, order.LastUpdated);
        Assert.Single(order.Items);
        Assert.Equal("Product 1", order.Items.First().ProductName);
    }
}
