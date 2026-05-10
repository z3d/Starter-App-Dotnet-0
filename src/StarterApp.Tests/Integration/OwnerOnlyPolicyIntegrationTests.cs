namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class OwnerOnlyPolicyIntegrationTests : IAsyncLifetime
{
    private const string OtherSubject = "other-user-01";
    private const string TenantId = "test-tenant-01";

    private readonly ApiTestFixture _fixture;

    public OwnerOnlyPolicyIntegrationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Customers_ShouldBeScopedToOwningIdentity()
    {
        var command = new CreateCustomerCommand
        {
            Name = "Owner Scoped Customer",
            Email = "owner-scoped@example.com"
        };
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/customers", command);
        createResponse.EnsureSuccessStatusCode();
        var customer = await createResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);

        using var otherClient = _fixture.CreateUnauthenticatedClient();

        var hiddenResponse = await SendAsAsync(otherClient, HttpMethod.Get, $"/api/v1/customers/{customer.Id}", subject: OtherSubject);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);

        var listResponse = await SendAsAsync(otherClient, HttpMethod.Get, "/api/v1/customers", subject: OtherSubject);
        listResponse.EnsureSuccessStatusCode();
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResponse<CustomerReadModel>>();
        Assert.NotNull(page);
        Assert.Empty(page.Data);

        var duplicateEmailResponse = await SendAsJsonAsync(otherClient, HttpMethod.Post, "/api/v1/customers", command, subject: OtherSubject);
        Assert.Equal(HttpStatusCode.Created, duplicateEmailResponse.StatusCode);
    }

    [Fact]
    public async Task Products_ShouldHideReadsAndForbidMutationsForDifferentOwner()
    {
        var command = new CreateProductCommand
        {
            Name = "Owner Scoped Product",
            Description = "Owned by the default test identity",
            Price = 12.50m,
            Currency = "USD",
            Stock = 10
        };
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/products", command);
        createResponse.EnsureSuccessStatusCode();
        var product = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        using var otherClient = _fixture.CreateUnauthenticatedClient();

        var hiddenResponse = await SendAsAsync(otherClient, HttpMethod.Get, $"/api/v1/products/{product.Id}", subject: OtherSubject);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);

        var updateResponse = await SendAsJsonAsync(otherClient, HttpMethod.Put, $"/api/v1/products/{product.Id}", new UpdateProductCommand
        {
            Id = product.Id,
            Name = "Cross Owner Update",
            Description = "Should be forbidden",
            Price = 15.00m,
            Currency = "USD",
            Stock = 5
        }, subject: OtherSubject);

        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Orders_ShouldHideReadsAndForbidMutationsForDifferentOwner()
    {
        var customer = await CreateCustomerAsync();
        var product = await CreateProductAsync();

        var createOrderResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        });
        createOrderResponse.EnsureSuccessStatusCode();
        var order = await createOrderResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);

        using var otherClient = _fixture.CreateUnauthenticatedClient();

        var hiddenResponse = await SendAsAsync(otherClient, HttpMethod.Get, $"/api/v1/orders/{order.Id}", subject: OtherSubject);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);

        var statusListResponse = await SendAsAsync(otherClient, HttpMethod.Get, "/api/v1/orders/status/Pending", subject: OtherSubject);
        statusListResponse.EnsureSuccessStatusCode();
        var page = await statusListResponse.Content.ReadFromJsonAsync<PagedResponse<OrderReadModel>>();
        Assert.NotNull(page);
        Assert.Empty(page.Data);

        var cancelResponse = await SendAsAsync(otherClient, HttpMethod.Post, $"/api/v1/orders/{order.Id}/cancel", subject: OtherSubject);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithCustomerOwnedByDifferentIdentity_ShouldReturnForbidden()
    {
        var customer = await CreateCustomerAsync();
        var product = await CreateProductAsync();

        using var otherClient = _fixture.CreateUnauthenticatedClient();
        var response = await SendAsJsonAsync(otherClient, HttpMethod.Post, "/api/v1/orders", new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        }, subject: OtherSubject);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<CustomerDto> CreateCustomerAsync()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerCommand
        {
            Name = "Order Owner Customer",
            Email = $"order-owner-{Guid.NewGuid():N}@example.com"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private async Task<ProductDto> CreateProductAsync()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/products", new CreateProductCommand
        {
            Name = $"Order Owner Product {Guid.NewGuid():N}",
            Description = "Product for owner scoped order tests",
            Price = 20.00m,
            Currency = "USD",
            Stock = 20
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static Task<HttpResponseMessage> SendAsAsync(HttpClient client, HttpMethod method, string uri, string subject)
    {
        var request = new HttpRequestMessage(method, uri);
        TestGatewayIdentity.AddSignedHeaders(request, subject: subject, tenantId: TenantId);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendAsJsonAsync(HttpClient client, HttpMethod method, string uri, object body, string subject)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body)
        };
        TestGatewayIdentity.AddSignedHeaders(request, subject: subject, tenantId: TenantId);
        return client.SendAsync(request);
    }
}
