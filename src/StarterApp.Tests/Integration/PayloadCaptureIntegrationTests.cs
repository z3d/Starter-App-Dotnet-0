using System.Text;
using System.Text.Json;

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
        var auditEntry = _fixture.PayloadArchiveStore.Lines.Single(pair =>
            pair.Key.StartsWith("audit/", StringComparison.Ordinal) &&
            pair.Key.EndsWith("/payload-audit.jsonl", StringComparison.Ordinal));
        var entityIndexEntry = _fixture.PayloadArchiveStore.Lines.Single(pair =>
            pair.Key.StartsWith($"entity-index/customer/{customerId}/", StringComparison.Ordinal) &&
            pair.Key.EndsWith($"/{correlationId}.jsonl", StringComparison.Ordinal));

        Assert.Equal(2, archiveEntry.Value.Count);
        Assert.Equal(2, auditEntry.Value.Count);
        Assert.Single(entityIndexEntry.Value);
        Assert.Contains($"{correlationId}@example.com", archiveEntry.Value[0]);
        Assert.Contains("\"direction\":\"inbound\"", archiveEntry.Value[0]);
        Assert.Contains("\"direction\":\"outbound\"", archiveEntry.Value[1]);
        Assert.Contains($"\"archiveBlobName\":\"{archiveEntry.Key}\"", entityIndexEntry.Value.Single());
        Assert.DoesNotContain($"{correlationId}@example.com", entityIndexEntry.Value.Single());

        using var auditJson = JsonDocument.Parse(auditEntry.Value[0]);
        Assert.Equal(correlationId, auditJson.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(archiveEntry.Key, auditJson.RootElement.GetProperty("archiveBlobName").GetString());

        _output.WriteLine($"Archive blob: {archiveEntry.Key}");
        _output.WriteLine($"Audit blob: {auditEntry.Key}");
        _output.WriteLine($"Entity index blob: {entityIndexEntry.Key}");
    }
}
