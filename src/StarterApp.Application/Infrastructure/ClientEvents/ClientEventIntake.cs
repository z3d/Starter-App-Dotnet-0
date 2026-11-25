using System.Text.RegularExpressions;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Application.Infrastructure.ClientEvents;

// Browser errors and events, posted before or after sign-in, written to the API's own logs. Not a business route, so it does not go through the mediator.
public static partial class ClientEventIntake
{
    public const string Path = "/api/v1/client-events";
    public const string RateLimitPolicy = "client-events";
    public const int MaxEventsPerBatch = 50;

    public static IEndpointRouteBuilder MapClientEventIntake(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, Record)
            .WithTags("Client events")
            .WithName("RecordClientEvents")
            .WithSummary("Record errors and events from the browser")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .Accepts<ClientEventBatch>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(429);
        return app;
    }

    private static IResult Record(ClientEventBatch batch, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (batch.Events is not { Count: > 0 and <= MaxEventsPerBatch } events || events.Any(IsMalformed))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: $"A batch holds 1 to {MaxEventsPerBatch} events, each with a kind of 'error' or 'event' and a name.");

        var logger = loggerFactory.CreateLogger("StarterApp.Api.ClientEvents");
        foreach (var clientEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Clean(clientEvent.Name, 100);
            var route = Clean(clientEvent.Route?.Split('?', '#')[0], 200);
            var build = Clean(clientEvent.Build, 64);
            var elapsed = Math.Max(0, clientEvent.ElapsedMs ?? 0);
            if (clientEvent.Kind == "error")
                logger.LogWarning("Browser error {ClientErrorName} on {ClientRoute} in build {ClientBuild} after {ClientElapsedMs} ms: {ClientErrorMessage}",
                    name, route, build, elapsed, Clean(clientEvent.Message, 1000));
            else
                logger.LogInformation("Browser event {ClientEventName} on {ClientRoute} in build {ClientBuild} after {ClientElapsedMs} ms",
                    name, route, build, elapsed);
        }

        return Results.Accepted();
    }

    private static bool IsMalformed(ClientEvent? clientEvent) =>
        clientEvent is null || clientEvent.Kind is not ("error" or "event") || string.IsNullOrWhiteSpace(clientEvent.Name);

    // A browser message can quote anything a person typed, so whatever looks like an email, a card number or a secret is blanked.
    internal static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cut = text.Length > maxLength ? text[..maxLength] : text;
        var masked = EmailRegex().Replace(PayloadSecretMasker.MaskPayload(cut), "[email]");
        return new string(masked.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
