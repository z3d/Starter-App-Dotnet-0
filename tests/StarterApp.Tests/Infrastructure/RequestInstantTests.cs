using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StarterApp.Tests.Infrastructure;

public sealed class RequestInstantTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddApiProblemDetails();
        builder.Services.AddApiJson();
        builder.Services.AddApiOpenApi();
        _app = builder.Build();
        _app.UseExceptionHandling();
        _app.UseRouting();
        _app.UseMiddleware<UtcInstantParameterMiddleware>();
        _app.MapPost("/probe", (Probe probe) => probe);
        _app.MapGet("/probe", (DateTimeOffset at, [FromQuery(Name = "until")] DateTimeOffset? before) => new Probe(at, before));
        _app.MapGet("/probe/{at}", (DateTimeOffset at) => new Probe(at, null));
        _app.MapOpenApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("""{"at":"2026-07-01T09:00:00+10:00"}""", "at")]
    [InlineData("""{"at":"2026-07-01T09:00:00Z","until":"2026-07-01T09:00:00-05:30"}""", "until")]
    public async Task Body_WithNonZeroOffset_IsABadRequestNamingTheField(string json, string field)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/probe", content);

        await AssertNamesFieldAsync(response, field);
    }

    [Theory]
    [InlineData("/probe?at=2026-07-01T09:00:00%2B10:00", "at")]
    [InlineData("/probe?at=2026-07-01T09:00:00Z&until=2026-07-01T09:00:00%2B10:00", "until")]
    [InlineData("/probe/2026-07-01T09:00:00%2B10:00", "at")]
    public async Task QueryOrRoute_WithNonZeroOffset_IsABadRequestNamingTheField(string path, string field)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        await AssertNamesFieldAsync(response, field);
    }

    [Theory]
    [InlineData("2026-07-01T09:00:00Z")]
    [InlineData("2026-07-01T09:00:00+00:00")]
    public async Task Body_InUtc_IsAcceptedAndWrittenAsBefore(string instant)
    {
        using var content = new StringContent($$"""{"at":"{{instant}}","until":"{{instant}}"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/probe", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var at = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(JsonSerializer.Serialize(new Probe(at, at), JsonSerializerOptions.Web), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/probe?at=2026-07-01T09:00:00Z&until=2026-07-01T10:00:00%2B00:00")]
    [InlineData("/probe/2026-07-01T09:00:00Z")]
    public async Task QueryOrRoute_InUtc_IsAccepted(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var probe = await response.Content.ReadFromJsonAsync<Probe>();
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), probe!.At);
    }

    [Fact]
    public async Task Body_ThatIsNotJson_IsStillABadRequestWithoutFieldErrors()
    {
        using var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/probe", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.False(problem!.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task Body_OfTheWrongMediaType_KeepsTheFrameworksStatus()
    {
        using var content = new StringContent("at", System.Text.Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/probe", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_StillDescribesInstantsAsDateTimeStrings()
    {
        using var document = JsonDocument.Parse(await _client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        var properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(Probe)).GetProperty("properties");
        Assert.Equal("""{"type":"string","format":"date-time"}""", Compact(properties.GetProperty("at")));
        Assert.Equal("""{"type":["null","string"],"format":"date-time"}""", Compact(properties.GetProperty("until")));
        var parameters = document.RootElement.GetProperty("paths").GetProperty("/probe").GetProperty("get").GetProperty("parameters");
        Assert.All(parameters.EnumerateArray(), parameter =>
            Assert.Equal("""{"type":"string","format":"date-time"}""", Compact(parameter.GetProperty("schema"))));
    }

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    private static async Task AssertNamesFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal([field], errors.EnumerateObject().Select(error => error.Name));
        Assert.Contains(field, errors.GetProperty(field)[0].GetString(), StringComparison.Ordinal);
    }

    public sealed record Probe(DateTimeOffset At, DateTimeOffset? Until);
}
