using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediatorImpl = StarterApp.Api.Infrastructure.Mediator.Mediator;

namespace StarterApp.Tests.Infrastructure;

public sealed class RequestInstantTests : IAsyncLifetime
{
    private static readonly DateTimeOffset NineUtc = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddApiProblemDetails();
        builder.Services.AddScoped<IMediator, MediatorImpl>();
        builder.Services.AddScoped<IRequestHandler<ProbeCommand, ProbeCommand>, ProbeHandler>();
        _app = builder.Build();
        _app.UseExceptionHandling();
        _app.UseRouting();
        _app.MapPost("/probe", (ProbeCommand command, IMediator mediator, CancellationToken cancellationToken) => mediator.SendAsync(command, cancellationToken));
        _app.MapGet("/probe", (DateTimeOffset at, DateTimeOffset? until, IMediator mediator, CancellationToken cancellationToken) =>
            mediator.SendAsync(new ProbeCommand(at, until, null), cancellationToken));
        _app.MapPost("/refused", (ProbeCommand command, IMediator mediator, CancellationToken cancellationToken) => mediator.SendAsync(command, cancellationToken))
            .AddEndpointFilter((_, _) => ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden)));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("""{"at":"2026-07-01T09:00:00+10:00"}""", "At")]
    [InlineData("""{"at":"2026-07-01T09:00:00Z","until":"2026-07-01T09:00:00-05:30"}""", "Until")]
    [InlineData("""{"at":"2026-07-01T09:00:00Z","items":[{"at":"2026-07-01T09:00:00Z"},{"at":"2026-07-01T19:00:00+10:00"}]}""", "Items[1].At")]
    [InlineData("""{"at":"2026-07-01T09:00:00Z","byName":{"first":{"at":"2026-07-01T19:00:00+10:00"}}}""", "ByName[0].Value.At")]
    [InlineData("""{"at":"2026-07-01T09:00:00Z","nested":{"nested":{"nested":{"nested":{"nested":{"at":"2026-07-01T19:00:00+10:00"}}}}}}""", "Nested.Nested.Nested.Nested.Nested.At")]
    public async Task Body_WithNonZeroOffset_IsABadRequestNamingTheField(string json, string field)
    {
        var response = await PostAsync("/probe", json);

        await AssertNamesFieldAsync(response, field);
    }

    [Theory]
    [InlineData("/probe?at=2026-07-01T09:00:00%2B10:00", "At")]
    [InlineData("/probe?at=2026-07-01T09:00:00Z&until=2026-07-01T09:00:00%2B10:00", "Until")]
    public async Task Query_WithNonZeroOffset_IsABadRequestNamingTheField(string path, string field)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        await AssertNamesFieldAsync(response, field);
    }

    [Theory]
    [InlineData("2026-07-01T09:00:00Z")]
    [InlineData("2026-07-01T09:00:00+00:00")]
    public async Task Body_InUtc_IsAcceptedAndWrittenAsBefore(string instant)
    {
        var response = await PostAsync("/probe", $$"""{"at":"{{instant}}","until":"{{instant}}","items":[{"at":"{{instant}}"}]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            JsonSerializer.Serialize(new ProbeCommand(NineUtc, NineUtc, [new ProbeItem(NineUtc)]), JsonSerializerOptions.Web),
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_InUtc_IsAccepted()
    {
        var response = await _client.GetAsync(new Uri("/probe?at=2026-07-01T09:00:00Z&until=2026-07-01T10:00:00%2B00:00", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(NineUtc, (await response.Content.ReadFromJsonAsync<ProbeCommand>())!.At);
    }

    [Fact]
    public async Task Refusal_ByAnEndpointFilter_StillComesBeforeTheInstantCheck()
    {
        var response = await PostAsync("/refused", """{"at":"2026-07-01T09:00:00+10:00"}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await _client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    private static async Task AssertNamesFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal([field], errors.EnumerateObject().Select(error => error.Name));
        Assert.StartsWith(field, errors.GetProperty(field)[0].GetString(), StringComparison.Ordinal);
    }

    public sealed record ProbeItem(DateTimeOffset At);

    public sealed record ProbeNest(ProbeNest? Nested, DateTimeOffset? At);

    public sealed record ProbeCommand(DateTimeOffset At, DateTimeOffset? Until, IReadOnlyList<ProbeItem>? Items, Dictionary<string, ProbeItem>? ByName = null, ProbeNest? Nested = null) : IRequest<ProbeCommand>;

    private sealed class ProbeHandler : IRequestHandler<ProbeCommand, ProbeCommand>
    {
        public Task<ProbeCommand> HandleAsync(ProbeCommand request, CancellationToken cancellationToken) => Task.FromResult(request);
    }
}
