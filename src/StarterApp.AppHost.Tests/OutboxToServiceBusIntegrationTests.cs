using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        var orderId = order.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, orderId);

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
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, orderId);

        // Assert — query OutboxMessages directly via SQL, correlated to this specific order
        var connectionString = await app.GetConnectionStringAsync("database");
        Assert.NotNull(connectionString);

        // Poll until the outbox message for THIS order is created AND processed
        var (outboxRowFound, processedOnUtc) = await PollForOutboxMessageAsync(
            connectionString, "order.created.v1", orderId, maxAttempts: 30, delayMs: 2000);

        Assert.True(outboxRowFound,
            $"OutboxMessages should contain a row with Type = 'order.created.v1' and OrderId = {orderId} in Payload");
        Assert.NotNull(processedOnUtc); // Proves the OutboxProcessor sent this specific event without a publish error
    }

    [Fact]
    public async Task CreateCustomer_ShouldArchivePayloadsToAspireBlobStorage()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");
        await PollForHealthyAsync(httpClient, "/health/ready");

        var correlationId = $"aspire-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers")
        {
            Content = JsonContent.Create(new
            {
                Name = "Aspire Payload Customer",
                Email = $"{correlationId}@test.com"
            })
        };
        request.Headers.Add("X-Correlation-ID", correlationId);

        // Act
        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetInt32();

        // Assert — AppHost must wire the Blob component so the API writes real archive/audit/entity-index blobs.
        var storageConnectionString = await app.GetConnectionStringAsync("payloadarchive");
        Assert.NotNull(storageConnectionString);

        var (archiveBlobName, archiveContent) = await PollForBlobContentAsync(
            storageConnectionString,
            "payload-observability",
            "archive/",
            correlationId,
            maxAttempts: 30,
            delayMs: 2000);

        var (auditBlobName, auditContent) = await PollForBlobContentAsync(
            storageConnectionString,
            "payload-observability",
            "audit/",
            correlationId,
            maxAttempts: 30,
            delayMs: 2000);

        var (entityIndexBlobName, entityIndexContent) = await PollForBlobContentAsync(
            storageConnectionString,
            "payload-observability",
            $"entity-index/customer/{customerId}/",
            correlationId,
            maxAttempts: 30,
            delayMs: 2000);

        Assert.EndsWith($"/{correlationId}.jsonl", archiveBlobName);
        Assert.Contains($"{correlationId}@test.com", archiveContent);
        Assert.Contains("\"direction\":\"inbound\"", archiveContent);
        Assert.Contains("\"direction\":\"outbound\"", archiveContent);
        Assert.EndsWith("/payload-audit.jsonl", auditBlobName);
        Assert.Contains($"\"archiveBlobName\":\"{archiveBlobName}\"", auditContent);
        Assert.EndsWith($"/{correlationId}.jsonl", entityIndexBlobName);
        Assert.Contains($"\"archiveBlobName\":\"{archiveBlobName}\"", entityIndexContent);
        Assert.DoesNotContain($"{correlationId}@test.com", entityIndexContent);
    }

    private static async Task<(bool Found, string? ProcessedOnUtc)> PollForOutboxMessageAsync(
        string connectionString,
        string expectedType,
        Guid expectedOrderId,
        int maxAttempts,
        int delayMs)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await QueryOutboxForOrderAsync(connectionString, expectedType, expectedOrderId);
                if (result.Found && result.ProcessedOnUtc != null)
                    return result;
                // Row exists but not yet processed, or not yet created — keep polling
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // Connection not ready yet — retry
            }

            await Task.Delay(delayMs);
        }

        // Final check to distinguish "no row" from "row but not processed"
        try
        {
            return await QueryOutboxForOrderAsync(connectionString, expectedType, expectedOrderId);
        }
        catch
        {
            return (false, null);
        }
    }

    private static async Task<(string BlobName, string Content)> PollForBlobContentAsync(
        string storageConnectionString,
        string containerName,
        string prefix,
        string expectedCorrelationId,
        int maxAttempts,
        int delayMs)
    {
        var containerClient = new BlobServiceClient(storageConnectionString).GetBlobContainerClient(containerName);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
                {
                    var blobClient = containerClient.GetBlobClient(blob.Name);
                    var content = await blobClient.DownloadContentAsync();
                    var text = content.Value.Content.ToString();
                    if (text.Contains(expectedCorrelationId, StringComparison.Ordinal))
                        return (blob.Name, text);
                }
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // Blob container or emulator may still be coming up.
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException($"Blob prefix {prefix} did not contain correlation {expectedCorrelationId} after {maxAttempts} attempts.");
    }

    private static async Task<(bool Found, string? ProcessedOnUtc)> QueryOutboxForOrderAsync(
        string connectionString,
        string expectedType,
        Guid expectedOrderId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Type, ProcessedOnUtc, Payload
            FROM OutboxMessages
            WHERE Type = @Type
            ORDER BY OccurredOnUtc DESC
            """;
        command.Parameters.AddWithValue("@Type", expectedType);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payload = reader.GetString(2);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
                orderIdProp.GetGuid() == expectedOrderId)
            {
                var processedOnUtc = reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1).ToString("O");
                return (true, processedOnUtc);
            }
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
