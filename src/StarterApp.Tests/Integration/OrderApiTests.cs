using StarterApp.Tests.TestBuilders;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class OrderApiTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OrderApiTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _output.WriteLine("Resetting database for order test");
            await _fixture.ResetDatabaseAsync();
            _output.WriteLine("Database reset complete");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during database reset: {ex.GetType().Name}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Cleanup error: {ex.Message}");
        }
    }

    private async Task<(CustomerDto customer, ProductDto product)> CreateTestData()
    {
        // Create customer
        var customerCommand = CustomerBuilder.SimpleCustomer();
        var customerResponse = await _fixture.Client.PostAsJsonAsync("/api/customers", customerCommand);
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);

        // Create product
        var productCommand = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "A test product for order testing",
            Price = 19.99m,
            Currency = "USD",
            Stock = 100
        };
        var productResponse = await _fixture.Client.PostAsJsonAsync("/api/products", productCommand);
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        return (customer, product);
    }

    [Fact]
    public async Task GetOrder_WithNonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = 999999;

        // Act
        var response = await _fixture.Client.GetAsync($"/api/orders/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_WithValidId_ShouldReturnOrder()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        // Create order
        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        _output.WriteLine($"Created order with ID: {createdOrder.Id}");

        // Act
        var response = await _fixture.Client.GetAsync($"/api/orders/{createdOrder.Id}");

        // Debug: Log the response details if it fails
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"GET order failed with status: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        response.EnsureSuccessStatusCode();
        var retrievedOrder = await response.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(retrievedOrder);
        Assert.Equal(createdOrder.Id, retrievedOrder.Id);
        Assert.Equal(customer.Id, retrievedOrder.CustomerId);
        Assert.Equal(OrderStatus.Pending.ToString(), retrievedOrder.Status);
        Assert.Single(retrievedOrder.Items);
    }



    [Fact]
    public async Task GetOrdersByCustomer_WithNonExistentCustomer_ShouldReturnEmptyList()
    {
        // Arrange
        var nonExistentCustomerId = 999999;

        // Act
        var response = await _fixture.Client.GetAsync($"/api/orders/customer/{nonExistentCustomerId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task GetOrdersByCustomer_WithValidCustomer_ShouldReturnOrders()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        // Create multiple orders for the customer
        var order1 = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var order2 = OrderBuilder.SimpleOrder(customer.Id, product.Id);

        await _fixture.Client.PostAsJsonAsync("/api/orders", order1);
        await _fixture.Client.PostAsJsonAsync("/api/orders", order2);

        // Act
        var response = await _fixture.Client.GetAsync($"/api/orders/customer/{customer.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.All(orders, order => Assert.Equal(customer.Id, order.CustomerId));
    }

    [Fact]
    public async Task GetOrdersByStatus_WithValidStatus_ShouldReturnMatchingOrders()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        // Create order
        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Act
        var response = await _fixture.Client.GetAsync("/api/orders/status/Pending");

        // Assert
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        Assert.NotNull(orders);
        Assert.True(orders.Count >= 1);
        Assert.All(orders, order => Assert.Equal("Pending", order.Status));
    }

    [Fact]
    public async Task GetOrdersByStatus_WithNonExistentStatus_ShouldReturnEmptyList()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/orders/status/NonExistentStatus");

        // Assert
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldCreateOrder()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.Default()
            .WithCustomerId(customer.Id)
            .WithItem(product.Id, 2, 19.99m)
            .Build();

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);
        Assert.True(createdOrder.Id > 0);
        Assert.Equal(customer.Id, createdOrder.CustomerId);
        Assert.Equal(OrderStatus.Pending.ToString(), createdOrder.Status);
        Assert.Single(createdOrder.Items);

        var orderItem = createdOrder.Items.First();
        Assert.Equal(2, orderItem.Quantity);
        Assert.Equal(19.99m, orderItem.UnitPriceExcludingGst);
    }

    [Fact]
    public async Task CreateOrder_WithNonExistentCustomer_ShouldReturnNotFound()
    {
        // Arrange - Need to create a product first since order creation validates products exist
        var productCommand = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "A test product",
            Price = 19.99m,
            Currency = "USD",
            Stock = 100
        };
        var productResponse = await _fixture.Client.PostAsJsonAsync("/api/products", productCommand);
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var orderCommand = OrderBuilder.SimpleOrder(999999, product.Id); // Non-existent customer

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected NotFound but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithMultipleItems_ShouldCreateOrderWithAllItems()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        // Create second product
        var product2Command = new CreateProductCommand
        {
            Name = "Test Product 2",
            Description = "Second test product",
            Price = 25.00m,
            Currency = "USD",
            Stock = 50
        };
        var product2Response = await _fixture.Client.PostAsJsonAsync("/api/products", product2Command);
        product2Response.EnsureSuccessStatusCode();
        var product2 = await product2Response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product2);

        var orderCommand = OrderBuilder.MultipleItemsOrder(customer.Id, product.Id, product2.Id);

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Assert
        response.EnsureSuccessStatusCode();
        var createdOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);
        Assert.Equal(2, createdOrder.Items.Count);

        var item1 = createdOrder.Items.FirstOrDefault(i => i.Quantity == 1);
        Assert.NotNull(item1);
        Assert.Equal(10.00m, item1.UnitPriceExcludingGst);

        var item2 = createdOrder.Items.FirstOrDefault(i => i.Quantity == 3);
        Assert.NotNull(item2);
        Assert.Equal(25.00m, item2.UnitPriceExcludingGst);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithValidData_ShouldUpdateStatus()
    {
        // Arrange - Create test data and order
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        var updateCommand = new UpdateOrderStatusCommand
        {
            OrderId = createdOrder.Id,
            Status = OrderStatus.Confirmed.ToString()
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status", updateCommand);

        // Debug: Log the response details if it fails
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Update order status failed with status: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
            _output.WriteLine($"Update command: {System.Text.Json.JsonSerializer.Serialize(updateCommand)}");
            _output.WriteLine($"URL: /api/orders/{createdOrder.Id}/status");
        }

        // Assert
        response.EnsureSuccessStatusCode();
        var updatedOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Confirmed.ToString(), updatedOrder.Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentOrderId = 999999;
        var updateCommand = new UpdateOrderStatusCommand
        {
            OrderId = nonExistentOrderId,
            Status = OrderStatus.Processing.ToString()
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"/api/orders/{nonExistentOrderId}/status", updateCommand);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithMismatchedIds_ShouldReturnBadRequest()
    {
        // Arrange
        var updateCommand = new UpdateOrderStatusCommand
        {
            OrderId = 1,
            Status = OrderStatus.Processing.ToString()
        };

        // Act - URL ID doesn't match command ID
        var response = await _fixture.Client.PutAsJsonAsync("/api/orders/2/status", updateCommand);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_WithValidOrder_ShouldCancelOrder()
    {
        // Arrange - Create test data and order
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        // Act
        var response = await _fixture.Client.PostAsync($"/api/orders/{createdOrder.Id}/cancel", null);

        // Debug: Log the response details if it fails
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Cancel order failed with status: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
            _output.WriteLine($"URL: /api/orders/{createdOrder.Id}/cancel");
        }

        // Assert
        response.EnsureSuccessStatusCode();
        var cancelledOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(cancelledOrder);
        Assert.Equal(OrderStatus.Cancelled.ToString(), cancelledOrder.Status);
    }

    [Fact]
    public async Task CancelOrder_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentOrderId = 999999;

        // Act
        var response = await _fixture.Client.PostAsync($"/api/orders/{nonExistentOrderId}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithNonExistentProduct_ShouldReturnNotFound()
    {
        // Arrange - Create customer but use non-existent product
        var customerCommand = CustomerBuilder.SimpleCustomer();
        var customerResponse = await _fixture.Client.PostAsJsonAsync("/api/customers", customerCommand);
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, 999999); // Non-existent product

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected NotFound but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_ShouldCreateDraftOrder()
    {
        // Arrange - Create customer
        var customerCommand = CustomerBuilder.SimpleCustomer();
        var customerResponse = await _fixture.Client.PostAsJsonAsync("/api/customers", customerCommand);
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);

        var orderCommand = OrderBuilder.Default()
            .WithCustomerId(customer.Id)
            .Build(); // No items added - creates draft order

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);
        Assert.True(createdOrder.Id > 0);
        Assert.Equal(customer.Id, createdOrder.CustomerId);
        Assert.Equal(OrderStatus.Pending.ToString(), createdOrder.Status);
        Assert.Empty(createdOrder.Items); // Draft order with no items
        Assert.Equal(0m, createdOrder.TotalExcludingGst);
        Assert.Equal(0m, createdOrder.TotalIncludingGst);
        Assert.Equal(0m, createdOrder.TotalGstAmount);
    }

    [Fact]
    public async Task CreateOrder_WithNegativeQuantity_ShouldReturnBadRequest()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.Default()
            .WithCustomerId(customer.Id)
            .WithItem(product.Id, -1, 19.99m) // Negative quantity
            .Build();

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected BadRequest but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithNegativePrice_ShouldReturnBadRequest()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.Default()
            .WithCustomerId(customer.Id)
            .WithItem(product.Id, 1, -10.00m) // Negative price
            .Build();

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected BadRequest but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithInvalidStatus_ShouldReturnBadRequest()
    {
        // Arrange - Create test data and order
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        var updateCommand = new UpdateOrderStatusCommand
        {
            OrderId = createdOrder.Id,
            Status = "InvalidStatus"
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status", updateCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected BadRequest but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrderStatus_WithInvalidTransition_ShouldReturnBadRequest()
    {
        // Arrange - Create test data and order, then update to Delivered
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        // First, move to Confirmed -> Processing -> Shipped -> Delivered
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Confirmed.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Processing.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Shipped.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Delivered.ToString() });

        // Now try to change from Delivered to Pending (invalid transition)
        var invalidUpdateCommand = new UpdateOrderStatusCommand
        {
            OrderId = createdOrder.Id,
            Status = OrderStatus.Pending.ToString()
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status", invalidUpdateCommand);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected BadRequest but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderIsDelivered_ShouldReturnBadRequest()
    {
        // Arrange - Create test data and order, then move to Delivered
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        // Move order to Delivered status
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Confirmed.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Processing.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Shipped.ToString() });
        await _fixture.Client.PutAsJsonAsync($"/api/orders/{createdOrder.Id}/status",
            new UpdateOrderStatusCommand { OrderId = createdOrder.Id, Status = OrderStatus.Delivered.ToString() });

        // Act
        var response = await _fixture.Client.PostAsync($"/api/orders/{createdOrder.Id}/cancel", null);

        // Debug: Log the response details
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Expected BadRequest but got: {response.StatusCode}");
            _output.WriteLine($"Error response: {errorContent}");
        }

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ShouldCalculateCorrectTotals()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();

        var orderCommand = OrderBuilder.Default()
            .WithCustomerId(customer.Id)
            .WithItem(product.Id, 2, 10.00m, "USD", 0.10m) // 2 * 10.00 = 20.00 excl, 22.00 incl
            .Build();

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);

        // Assert
        response.EnsureSuccessStatusCode();
        var createdOrder = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        var item = createdOrder.Items.Single();
        Assert.Equal(2, item.Quantity);
        Assert.Equal(10.00m, item.UnitPriceExcludingGst);
        Assert.Equal(0.10m, item.GstRate);

        // Verify calculations (these depend on the DTO structure)
        Assert.Equal(20.00m, item.TotalPriceExcludingGst);
        Assert.Equal(22.00m, item.TotalPriceIncludingGst);

        // Check order-level totals
        Assert.Equal(20.00m, createdOrder.TotalExcludingGst);
        Assert.Equal(22.00m, createdOrder.TotalIncludingGst);
        Assert.Equal(2.00m, createdOrder.TotalGstAmount);
    }

    [Fact]
    public async Task GetOrder_ShouldIncludeCorrectTimestamps()
    {
        // Arrange - Create test data
        var (customer, product) = await CreateTestData();
        var startTime = DateTime.UtcNow;

        // Create order
        var orderCommand = OrderBuilder.SimpleOrder(customer.Id, product.Id);
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/orders", orderCommand);
        createResponse.EnsureSuccessStatusCode();
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(createdOrder);

        var endTime = DateTime.UtcNow;

        // Act
        var response = await _fixture.Client.GetAsync($"/api/orders/{createdOrder.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var retrievedOrder = await response.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(retrievedOrder);
        Assert.True(retrievedOrder.OrderDate >= startTime);
        Assert.True(retrievedOrder.OrderDate <= endTime);
        Assert.True(retrievedOrder.LastUpdated >= startTime);
        Assert.True(retrievedOrder.LastUpdated <= endTime);
    }
}



