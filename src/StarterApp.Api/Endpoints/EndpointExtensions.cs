namespace StarterApp.Api.Endpoints;

/// <summary>
/// Extension methods for configuring API endpoints using the endpoint definition pattern.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Maps all API endpoints by discovering and executing endpoint definition classes.
    /// </summary>
    /// <param name="app">The WebApplication to configure</param>
    /// <returns>The WebApplication for method chaining</returns>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(Program).Assembly
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
