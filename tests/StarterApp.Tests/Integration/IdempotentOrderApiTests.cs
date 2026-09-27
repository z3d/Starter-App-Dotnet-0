namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class IdempotentOrderApiTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public IdempotentOrderApiTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrder_RetriedWithTheSameKey_ReturnsTheFirstOrderAndReservesStockOnce()
    {
        var (customerId, productId) = await CreateCustomerAndProductAsync(stock: 10);
        var key = Guid.NewGuid().ToString();

        var first = await PostOrderAsync(customerId, productId, quantity: 3, key);
        var retry = await PostOrderAsync(customerId, productId, quantity: 3, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var firstOrder = await first.Content.ReadFromJsonAsync<OrderDto>();
        var retryOrder = await retry.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal(firstOrder!.Id, retryOrder!.Id);
        Assert.Equal(7, await GetStockAsync(productId));
    }

    [Fact]
    public async Task CreateOrder_SameKeyWithADifferentRequest_IsRefusedWith422()
    {
        var (customerId, productId) = await CreateCustomerAndProductAsync(stock: 10);
        var key = Guid.NewGuid().ToString();

        var first = await PostOrderAsync(customerId, productId, quantity: 3, key);
        var reused = await PostOrderAsync(customerId, productId, quantity: 4, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal(7, await GetStockAsync(productId));
    }

    [Fact]
    public async Task CreateOrder_ConcurrentRequestsWithTheSameKey_CreateOneOrder()
    {
        var (customerId, productId) = await CreateCustomerAndProductAsync(stock: 100);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostOrderAsync(customerId, productId, quantity: 2, key)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var orderIds = await Task.WhenAll(responses.Select(async response => (await response.Content.ReadFromJsonAsync<OrderDto>())!.Id));
        Assert.Single(orderIds.Distinct());
        Assert.Equal(98, await GetStockAsync(productId));
    }

    [Fact]
    public async Task CreateOrder_WithoutAKey_CreatesAnOrderPerRequest()
    {
        var (customerId, productId) = await CreateCustomerAndProductAsync(stock: 10);

        var first = await PostOrderAsync(customerId, productId, quantity: 1, idempotencyKey: null);
        var second = await PostOrderAsync(customerId, productId, quantity: 1, idempotencyKey: null);

        var firstOrder = await first.Content.ReadFromJsonAsync<OrderDto>();
        var secondOrder = await second.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotEqual(firstOrder!.Id, secondOrder!.Id);
        Assert.Equal(8, await GetStockAsync(productId));
    }

    [Fact]
    public async Task CreateOrder_WithAMalformedKey_IsRefusedWith400()
    {
        var (customerId, productId) = await CreateCustomerAndProductAsync(stock: 10);

        var response = await PostOrderAsync(customerId, productId, quantity: 1, new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostOrderAsync(int customerId, int productId, int quantity, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new CreateOrderCommand
            {
                CustomerId = customerId,
                Items = [new CreateOrderItemCommand { ProductId = productId, Quantity = quantity }]
            })
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await _fixture.Client.SendAsync(request);
    }

    private async Task<(int CustomerId, int ProductId)> CreateCustomerAndProductAsync(int stock)
    {
        var customerResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerCommand { Name = "Idempotent Customer", Email = "idempotent@example.com" });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDto>();

        var productResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/products", new CreateProductCommand
        {
            Name = "Idempotent Product",
            Description = "Stock must be reserved once per key",
            Price = 10m,
            Currency = "USD",
            Stock = stock
        });
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        return (customer!.Id, product!.Id);
    }

    private async Task<int> GetStockAsync(int productId)
    {
        var product = await _fixture.Client.GetFromJsonAsync<ProductReadModel>($"/api/v1/products/{productId}");
        return product!.Stock;
    }
}
