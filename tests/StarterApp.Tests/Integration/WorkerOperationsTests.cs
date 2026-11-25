namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class WorkerOperationsTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private IHost _worker = null!;

    public WorkerOperationsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private IWorkerOperations Operations => _worker.Services.GetRequiredService<IWorkerOperations>();

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration["ConnectionStrings:database"] = _fixture.WorkerConnectionString;
        builder.AddWorkerOperations();
        _worker = builder.Build();
        await _worker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _worker.StopAsync();
        _worker.Dispose();
    }

    [Fact]
    public async Task GetOrderConfirmation_ReadsTheOrderAndItsCustomer_AsTheWorker()
    {
        var orderId = await PlaceOrderAsync("confirm@example.com");

        var confirmation = await Operations.GetOrderConfirmationAsync(TestJwtIdentity.DefaultTenantId, TestJwtIdentity.DefaultSubject, orderId, CancellationToken.None);

        Assert.Equal(orderId, confirmation.OrderId);
        Assert.Equal("confirm@example.com", confirmation.CustomerEmail);
        Assert.Equal(nameof(OrderStatus.Pending), confirmation.Status);
        Assert.Equal(21.99m, confirmation.TotalIncludingGst);
    }

    [Theory]
    [InlineData(TestJwtIdentity.DefaultTenantId, "another-owner")]
    [InlineData("another-tenant", TestJwtIdentity.DefaultSubject)]
    [InlineData("", TestJwtIdentity.DefaultSubject)]
    public async Task GetOrderConfirmation_OutsideTheOrdersTenantOrOwner_IsRefused_SoItsMessageDeadLetters(string tenantId, string ownerSubject)
    {
        var orderId = await PlaceOrderAsync("refused@example.com");

        var refusal = await Assert.ThrowsAsync<WorkerOperationRefusedException>(() =>
            Operations.GetOrderConfirmationAsync(tenantId, ownerSubject, orderId, CancellationToken.None));

        Assert.True(StarterApp.Functions.MessageSettlement.IsNonRetryable(refusal));
    }

    private async Task<Guid> PlaceOrderAsync(string email)
    {
        var customerResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerCommand { Name = "Worker Customer", Email = email });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDto>();

        var productResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/products", new CreateProductCommand { Name = "Worker Product", Description = "Read by the worker", Price = 19.99m, Currency = "USD", Stock = 10 });
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        var orderResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrderCommand
        {
            CustomerId = customer!.Id,
            Items = [new CreateOrderItemCommand { ProductId = product!.Id, Quantity = 1 }]
        });
        orderResponse.EnsureSuccessStatusCode();
        return (await orderResponse.Content.ReadFromJsonAsync<OrderDto>())!.Id;
    }
}
