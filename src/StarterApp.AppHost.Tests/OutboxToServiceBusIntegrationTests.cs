using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StarterApp.AppHost.Tests;

[Trait("Category", "Aspire")]
public class OutboxToServiceBusIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task AppHost_ShouldEventuallyExposeHealthyApi()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act — poll with retries instead of a single GET, since resources may still be starting
        var response = await PollForHealthyAsync(httpClient, "/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ShouldSucceedEndToEnd()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Wait for API to be ready
        await PollForHealthyAsync(httpClient, "/health/ready");

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

        // Assert — verify the order persisted correctly
        var getResponse = await httpClient.GetAsync($"/api/v1/orders/{orderId}");
        getResponse.EnsureSuccessStatusCode();
        var fetchedOrder = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Pending", fetchedOrder.GetProperty("status").GetString());

        // Verify stock was decremented
        var getProductResponse = await httpClient.GetAsync($"/api/v1/products/{productId}");
        getProductResponse.EnsureSuccessStatusCode();
        var fetchedProduct = await getProductResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(48, fetchedProduct.GetProperty("stock").GetInt32());
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

        // Wait for full readiness before checking all endpoints
        await PollForHealthyAsync(httpClient, "/health/ready");

        // Act & Assert — all health endpoints should respond
        var liveResponse = await httpClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var aliveResponse = await httpClient.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, aliveResponse.StatusCode);

        var readyResponse = await httpClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ShouldWriteAndProcessOutboxEvent()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");
        await PollForHealthyAsync(httpClient, "/health/ready");

        // Create a customer
        var customerResponse = await httpClient.PostAsJsonAsync("/api/v1/customers", new
        {
            Name = "Outbox Test Customer",
            Email = $"outbox-{Guid.NewGuid():N}@test.com"
        });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetInt32();

        // Create a product
        var productResponse = await httpClient.PostAsJsonAsync("/api/v1/products", new
        {
            Name = "Outbox Test Product",
            Description = "Product for outbox integration test",
            Price = 10.00m,
            Currency = "USD",
            Stock = 100
        });
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var productId = product.GetProperty("id").GetInt32();

        // Act — create an order (triggers domain event → outbox → Service Bus)
        var orderResponse = await httpClient.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerId = customerId,
            Items = new[] { new { ProductId = productId, Quantity = 1 } }
        });
        orderResponse.EnsureSuccessStatusCode();

        // Assert — query OutboxMessages directly via SQL
        var connectionString = await app.GetConnectionStringAsync("database");
        Assert.NotNull(connectionString);

        // Poll until the outbox message is created AND processed
        var (outboxRowFound, processedOnUtc) = await PollForOutboxMessageAsync(
            connectionString, "order.created.v1", maxAttempts: 30, delayMs: 2000);

        Assert.True(outboxRowFound, "OutboxMessages should contain a row with Type = 'order.created.v1'");
        Assert.NotNull(processedOnUtc); // Proves the OutboxProcessor published to Service Bus
    }

    private static async Task<(bool Found, string? ProcessedOnUtc)> PollForOutboxMessageAsync(
        string connectionString,
        string expectedType,
        int maxAttempts,
        int delayMs)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TOP 1 Type, ProcessedOnUtc
                    FROM OutboxMessages
                    WHERE Type = @Type
                    ORDER BY OccurredOnUtc DESC
                    """;
                command.Parameters.AddWithValue("@Type", expectedType);

                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var processedOnUtc = reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1).ToString("O");
                    if (processedOnUtc != null)
                        return (true, processedOnUtc);

                    // Row exists but not yet processed — keep polling
                }
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // Connection not ready yet — retry
            }

            await Task.Delay(delayMs);
        }

        // One final check to distinguish "no row" from "row but not processed"
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP 1 Type, ProcessedOnUtc FROM OutboxMessages WHERE Type = @Type";
            command.Parameters.AddWithValue("@Type", expectedType);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var processedOnUtc = reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1).ToString("O");
                return (true, processedOnUtc);
            }
        }
        catch
        {
            // Ignore final check errors
        }

        return (false, null);
    }

    private static async Task<HttpResponseMessage> PollForHealthyAsync(
        HttpClient client,
        string endpoint,
        int maxAttempts = 30,
        int delayMs = 2000)
    {
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                lastResponse = await client.GetAsync(endpoint, cts.Token);
                if (lastResponse.IsSuccessStatusCode)
                    return lastResponse;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // Connection refused, timeout, etc. — retry
            }

            await Task.Delay(delayMs);
        }

        return lastResponse ?? throw new TimeoutException(
            $"Health endpoint {endpoint} did not become healthy after {maxAttempts} attempts");
    }
}
