namespace StarterApp.Api.Endpoints;

public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(IApiMarker).Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IEndpointDefinition)) &&
                       !t.IsAbstract &&
                       !t.IsInterface)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var definition in endpointDefinitions)
        {
            definition.DefineEndpoints(app);
        }

        return app;
    }
}
