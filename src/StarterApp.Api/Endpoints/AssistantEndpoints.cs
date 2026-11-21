
namespace StarterApp.Api.Endpoints;

public class AssistantEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assistant").WithTags("Assistant").RequireAuthorization();

        group.MapGet("/", GetStatus)
            .RequireScope(AssistantAreas.Scope)
            .WithName("GetAssistantStatus").WithSummary("Whether the assistant is turned on in this environment")
            .Produces<AssistantStatusDto>(200, "application/json");

        group.MapGet("/routes", GetRoutes)
            .RequireScope(AssistantAreas.Scope)
            .WithName("GetAssistantRoutes").WithSummary("The changes a person in an area may make, each with the fields its card asks for")
            .WithDescription("The cards the web app builds, for a request the assistant proposed or a step the person picked, come from this list, so a card asks for the same fields in the same order every time. It needs no model.")
            .Produces<AssistantRoutesDto>(200, "application/json").ProducesProblem(400);

        group.MapPost("/turns", NextTurn)
            .RequireScope(AssistantAreas.Scope)
            .SecuredBy2Fa()
            .WithName("NextAssistantTurn").WithSummary("Ask the assistant for its next turn: a question, a read for the browser to run, or an action for the person to confirm")
            .WithDescription("The browser holds the conversation and sends all of it each time, with the area the person is working in, which they must hold a role for. The reads run in the browser through this API as the person; a proposal changes nothing until the person confirms it through the route it names. 503 when no model is configured here, and 502 when the model does not answer; neither changes anything.")
            .Accepts<NextAssistantTurnQuery>("application/json")
            .Produces<AssistantTurnDto>(200, "application/json").ProducesProblem(400).ProducesProblem(502).ProducesProblem(503);
    }

    private static async Task<IResult> GetStatus(IMediator mediator, CancellationToken cancellationToken) =>
        Results.Ok(await mediator.SendAsync(new GetAssistantStatusQuery(), cancellationToken));

    private static async Task<IResult> GetRoutes(IMediator mediator, CancellationToken cancellationToken, string? area = null) =>
        Results.Ok(await mediator.SendAsync(new GetAssistantRoutesQuery { Area = area }, cancellationToken));

    private static async Task<IResult> NextTurn(NextAssistantTurnQuery query, IMediator mediator, CancellationToken cancellationToken) =>
        Results.Ok(await mediator.SendAsync(query, cancellationToken));
}
