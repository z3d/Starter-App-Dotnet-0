namespace StarterApp.Api.Endpoints;

public class SettingsEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var settings = app.MapGroup("/api/v1/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        settings.MapGet("/appearance", GetAppearance)
            .WithName("GetAppearance")
            .WithSummary("Get the tenant's colour theme")
            .RequireScope("settings:read")
            .Produces<TenantSettingsReadModel>(200, "application/json")
            .ProducesProblem(500);

        settings.MapPut("/appearance", UpdateAppearance)
            .WithName("UpdateAppearance")
            .WithSummary("Set the tenant's colour theme (administrators only)")
            .RequireScope("settings:write")
            .SecuredBy2Fa()
            .Accepts<UpdateTenantSettingsCommand>("application/json")
            .Produces<TenantSettingsDto>(200, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);
    }

    private static async Task<IResult> GetAppearance(IMediator mediator, CancellationToken cancellationToken)
    {
        return Results.Ok(await mediator.SendAsync(new GetTenantSettingsQuery(), cancellationToken));
    }

    private static async Task<IResult> UpdateAppearance(UpdateTenantSettingsCommand command, IMediator mediator, CancellationToken cancellationToken)
    {
        return Results.Ok(await mediator.SendAsync(command, cancellationToken));
    }
}
