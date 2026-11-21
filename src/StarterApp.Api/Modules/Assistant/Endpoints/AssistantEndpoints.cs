using Anthropic.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StarterApp.Api.Endpoints;
using StarterApp.Api.Modules.Assistant.Application;
using StarterApp.Api.Modules.Assistant.Application.DTOs;

namespace StarterApp.Api.Modules.Assistant.Endpoints;

// A turn proposes and never writes, so it needs the scope only (docs/DECISIONS.md, "The assistant proposes; a person confirms").
public class AssistantEndpoints : IEndpointDefinition
{
    public const string Scope = "identity-admin:read";

    public void DefineEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assistant").WithTags("Assistant").RequireAuthorization();

        group.MapGet("/", GetStatus)
            .RequireScope(Scope)
            .WithName("GetAssistantStatus").WithSummary("Whether the assistant is turned on in this environment")
            .Produces<AssistantStatusDto>();

        group.MapGet("/routes", GetRoutes)
            .RequireScope(Scope)
            .WithName("GetAssistantRoutes").WithSummary("The changes a person in an area may make, each with the fields its card asks for")
            .WithDescription("The cards the web app builds, for a request the assistant proposed or a step the person picked, come from this list, so a card asks for the same fields in the same order every time. It needs no model.")
            .Produces<AssistantRoutesDto>().ProducesProblem(400).ProducesProblem(403);

        group.MapPost("/turns", NextTurn)
            .RequireScope(Scope)
            .WithName("NextAssistantTurn").WithSummary("Ask the assistant for its next turn: a question, a read for the browser to run, or an action for the person to confirm")
            .WithDescription("The browser holds the conversation and sends all of it each time, with the area the person is working in, which they must hold a role for. The reads run in the browser through this API as the person; a proposal changes nothing until the person confirms it through the route it names. 503 when no model is configured.")
            .Accepts<AssistantTurnRequest>("application/json")
            .Produces<AssistantTurnDto>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(502).ProducesProblem(503);
    }

    private static IResult GetStatus(IOptions<AssistantOptions> options, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Results.NoContent() : Results.Ok(new AssistantStatusDto { Enabled = options.Value.IsConfigured, Primary = options.Value.IsConfigured && options.Value.PrimaryInterface });

    private static async Task<IResult> GetRoutes(string? area, [FromServices] AssistantCatalogue catalogue, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        area ??= AssistantAreas.Staff;
        if (AssistantAreas.RolesOf(area) is not { } roles)
            throw new ValidationException([new ValidationError(nameof(area), "There is no such area.")]);
        if (!roles.Any(currentUser.HasRole))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not your area", detail: $"Your sign-in has no role in the {area} area.");

        var routes = await catalogue.RoutesForAsync(area, cancellationToken);
        return Results.Ok(new AssistantRoutesDto { Routes = routes.Where(route => !route.IsRead).Select(AssistantTurnMapper.ToDto).ToList() });
    }

    private static async Task<IResult> NextTurn(AssistantTurnRequest request, [FromServices] AssistantTurns turns, [FromServices] ICurrentUser currentUser, IOptions<AssistantOptions> options, ILogger<AssistantEndpoints> logger, CancellationToken cancellationToken)
    {
        var area = request.Area ?? AssistantAreas.Staff;
        if (AssistantAreas.RolesOf(area) is not { } roles)
            throw new ValidationException([new ValidationError(nameof(request.Area), "There is no such area.")]);
        if (!roles.Any(currentUser.HasRole))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not your area", detail: $"Your sign-in has no role in the {area} area.");

        if (!options.Value.IsConfigured)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The assistant is turned off", detail: "No model is configured for the assistant in this environment.");

        try
        {
            return Results.Ok(await turns.NextAsync(area, request.Messages ?? [], cancellationToken));
        }
        catch (AnthropicApiException ex)
        {
            logger.LogWarning(ex, "The assistant's model did not answer");
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The assistant did not answer", detail: "The assistant's model could not be reached or refused the request. Nothing was changed; try again, or do it by hand.");
        }
    }
}
