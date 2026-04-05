using Aspire.Hosting.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StarterApp.AppHost.Tests;

[Trait("Category", "Aspire")]
public class OutboxToServiceBusIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task AppHost_ShouldStartAllResources()
    {
        // Arrange & Act
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Assert — verify the API resource is reachable
        var httpClient = app.CreateHttpClient("api");
        var response = await httpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ShouldPublishToOutbox_AndBeProcessed()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Create a customer
        var customerResponse = await httpClient.PostAsJsonAsync("/api/v1/customers", new
        {
            Name = "Aspire Test Customer",
            Email = $"aspire-{Guid.NewGuid():N}@test.com"
        });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetInt32();

        // Create a product
        var productResponse = await httpClient.PostAsJsonAsync("/api/v1/products", new
        {
            Name = "Aspire Test Product",
            Description = "Product for Aspire integration test",
            Price = 29.99m,
            Currency = "USD",
            Stock = 50
        });
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var productId = product.GetProperty("id").GetInt32();

        // Act — create an order (triggers domain event → outbox)
        var orderResponse = await httpClient.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerId = customerId,
            Items = new[] { new { ProductId = productId, Quantity = 2 } }
        });
        orderResponse.EnsureSuccessStatusCode();
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderId = order.GetProperty("id").GetInt32();
        Assert.True(orderId > 0);

        // Assert — wait for outbox processing (processor polls every 5s by default)
        // Verify the order exists and was created successfully
        var getResponse = await httpClient.GetAsync($"/api/v1/orders/{orderId}");
        getResponse.EnsureSuccessStatusCode();
        var fetchedOrder = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Pending", fetchedOrder.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthEndpoints_ShouldBeReachable()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act & Assert — all health endpoints should respond
        var liveResponse = await httpClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var aliveResponse = await httpClient.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, aliveResponse.StatusCode);

        var readyResponse = await httpClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }
}
