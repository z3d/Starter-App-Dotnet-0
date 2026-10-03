using System.Text;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class PayloadCaptureIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PayloadCaptureIntegrationTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
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
    public async Task CreateCustomer_ShouldArchiveInboundAndOutboundPayloadsWithSameCorrelationId()
    {
        var correlationId = $"integration-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers");
        request.Headers.Add("X-Correlation-ID", correlationId);
        request.Content = new StringContent(
            $$"""{"name":"Support Test","email":"{{correlationId}}@example.com"}""",
            Encoding.UTF8,
            "application/json");

        var response = await _fixture.Client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customer.GetProperty("id").GetInt32();
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());

        var archiveEntry = _fixture.PayloadArchiveStore.Lines.Single(pair =>
            pair.Key.StartsWith("archive/", StringComparison.Ordinal) &&
            pair.Key.EndsWith($"/{correlationId}.jsonl", StringComparison.Ordinal));
        var lines = await WaitForAsync(lines =>
            AuditLines(lines, correlationId).Count == 2 && lines.Keys.Any(key => IsEntityIndexFor(key, customerId, correlationId)));
        var auditLines = AuditLines(lines, correlationId);
        var entityIndexEntry = lines.Single(pair => IsEntityIndexFor(pair.Key, customerId, correlationId));

        Assert.Equal(2, archiveEntry.Value.Count);
        Assert.Equal(2, auditLines.Count);
        Assert.Single(entityIndexEntry.Value);
        Assert.Contains($"{correlationId}@example.com", archiveEntry.Value[0]);
        Assert.Contains("\"direction\":\"inbound\"", archiveEntry.Value[0]);
        Assert.Contains("\"direction\":\"outbound\"", archiveEntry.Value[1]);
        Assert.Contains($"\"archiveBlobName\":\"{archiveEntry.Key}\"", entityIndexEntry.Value.Single());
        Assert.DoesNotContain($"{correlationId}@example.com", entityIndexEntry.Value.Single());

        using var auditJson = JsonDocument.Parse(auditLines[0]);
        Assert.Equal(correlationId, auditJson.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(archiveEntry.Key, auditJson.RootElement.GetProperty("archiveBlobName").GetString());

        _output.WriteLine($"Archive blob: {archiveEntry.Key}");
        _output.WriteLine($"Entity index blob: {entityIndexEntry.Key}");
    }

    [Theory]
    [InlineData("POST", "/api/v1/orders", "application/json")]
    [InlineData("GET", "/api/v1/customers/424242", null)]
    [InlineData("PUT", "/api/v1/customers/424242", "application/octet-stream")]
    public async Task AnonymousRequest_IsArchivedButNeverWritesTheEntityIndex(string method, string path, string? contentType)
    {
        var correlationId = $"anonymous-{Guid.NewGuid():N}";
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Correlation-ID", correlationId);
        if (contentType is not null)
            request.Content = new StringContent("""{"customerId":424242,"items":[{"productId":1,"quantity":1}]}""", Encoding.UTF8, contentType);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(_fixture.PayloadArchiveStore.Lines, pair =>
            pair.Key.StartsWith("archive/", StringComparison.Ordinal) &&
            pair.Key.EndsWith($"/{correlationId}.jsonl", StringComparison.Ordinal));

        // The background writer is one FIFO reader, so once a later request's index line lands, this one's would have.
        var laterCorrelationId = $"later-{Guid.NewGuid():N}";
        await PostOrderReferencingCustomerAsync(_fixture.Client, laterCorrelationId, customerId: 454545);
        var lines = await WaitForAsync(lines => lines.Keys.Any(key => IsEntityIndexFor(key, 454545, laterCorrelationId)));
        Assert.Contains(lines.Keys, key => IsEntityIndexFor(key, 454545, laterCorrelationId));
        Assert.DoesNotContain(lines.Keys, key => key.StartsWith("entity-index/", StringComparison.Ordinal) && key.EndsWith($"/{correlationId}.jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedRequest_WritesTheEntityIndexForItsRequestBody()
    {
        var correlationId = $"authenticated-{Guid.NewGuid():N}";

        var response = await PostOrderReferencingCustomerAsync(_fixture.Client, correlationId, customerId: 434343);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var lines = await WaitForAsync(lines => lines.Keys.Any(key => IsEntityIndexFor(key, 434343, correlationId)));
        Assert.Contains(lines.Keys, key => IsEntityIndexFor(key, 434343, correlationId));
    }

    private static bool IsEntityIndexFor(string key, int customerId, string correlationId) =>
        key.StartsWith($"entity-index/customer/{customerId}/", StringComparison.Ordinal) &&
        key.EndsWith($"/{correlationId}.jsonl", StringComparison.Ordinal);

    private static List<string> AuditLines(IReadOnlyDictionary<string, IReadOnlyList<string>> lines, string correlationId) =>
        lines
            .Where(pair => pair.Key.StartsWith("audit/", StringComparison.Ordinal) && pair.Key.EndsWith("/payload-audit.jsonl", StringComparison.Ordinal))
            .SelectMany(pair => pair.Value)
            .Where(line => line.Contains($"\"correlationId\":\"{correlationId}\"", StringComparison.Ordinal))
            .ToList();

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> WaitForAsync(
        Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, bool> condition)
    {
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (true)
        {
            var lines = _fixture.PayloadArchiveStore.Lines;
            if (condition(lines) || TimeProvider.System.GetUtcNow() > deadline)
                return lines;

            await Task.Delay(50);
        }
    }

    private static async Task<HttpResponseMessage> PostOrderReferencingCustomerAsync(HttpClient client, string correlationId, int customerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders");
        request.Headers.Add("X-Correlation-ID", correlationId);
        request.Content = new StringContent(
            $$"""{"customerId":{{customerId}},"items":[{"productId":1,"quantity":1}]}""",
            Encoding.UTF8,
            "application/json");
        return await client.SendAsync(request);
    }
}
