namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class CustomerApiTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public CustomerApiTests(ApiTestFixture fixture)
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
    public async Task UpdateCustomer_WithValidId_ShouldUpdateCustomer()
    {
        var customer = await CreateCustomerAsync("Original Customer", "original@example.com");
        var command = new UpdateCustomerCommand
        {
            Id = customer.Id,
            Name = "Updated Customer",
            Email = "updated@example.com"
        };

        var response = await _fixture.Client.PutAsJsonAsync($"/api/v1/customers/{customer.Id}", command);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _fixture.Client.GetAsync($"/api/v1/customers/{customer.Id}");
        getResponse.EnsureSuccessStatusCode();
        var updated = await getResponse.Content.ReadFromJsonAsync<CustomerReadModel>();

        Assert.NotNull(updated);
        Assert.Equal(command.Id, updated.Id);
        Assert.Equal(command.Name, updated.Name);
        Assert.Equal(command.Email, updated.Email);
    }

    [Fact]
    public async Task UpdateCustomer_WithMismatchedIds_ShouldReturnBadRequest()
    {
        var customer = await CreateCustomerAsync("Mismatch Customer", "mismatch@example.com");
        var command = new UpdateCustomerCommand
        {
            Id = customer.Id + 1,
            Name = "Should Not Persist",
            Email = "should-not-persist@example.com"
        };

        var response = await _fixture.Client.PutAsJsonAsync($"/api/v1/customers/{customer.Id}", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _fixture.Client.GetAsync($"/api/v1/customers/{customer.Id}");
        getResponse.EnsureSuccessStatusCode();
        var persisted = await getResponse.Content.ReadFromJsonAsync<CustomerReadModel>();

        Assert.NotNull(persisted);
        Assert.Equal(customer.Name, persisted.Name);
        Assert.Equal(customer.Email, persisted.Email);
    }

    [Fact]
    public async Task DeleteCustomer_WithValidId_ShouldRemoveCustomer()
    {
        var customer = await CreateCustomerAsync("Delete Customer", "delete@example.com");

        var response = await _fixture.Client.DeleteAsync($"/api/v1/customers/{customer.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _fixture.Client.GetAsync($"/api/v1/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomer_WithExistingOrders_ShouldReturnConflict()
    {
        var customer = await CreateCustomerAsync("Ordered Customer", "ordered@example.com");
        var product = await CreateProductAsync();

        var orderResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        });
        orderResponse.EnsureSuccessStatusCode();

        var response = await _fixture.Client.DeleteAsync($"/api/v1/customers/{customer.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var getResponse = await _fixture.Client.GetAsync($"/api/v1/customers/{customer.Id}");
        getResponse.EnsureSuccessStatusCode();
    }

    private async Task<CustomerDto> CreateCustomerAsync(string name, string email)
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerCommand
        {
            Name = name,
            Email = email
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private async Task<ProductDto> CreateProductAsync()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/products", new CreateProductCommand
        {
            Name = "Customer Delete Product",
            Description = "Product used to create an order before customer deletion.",
            Price = 10.00m,
            Currency = "USD",
            Stock = 10
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }
}
