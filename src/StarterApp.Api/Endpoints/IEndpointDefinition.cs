namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines the contract for endpoint configuration classes.
/// Implementing classes should define their API endpoints in the DefineEndpoints method.
/// </summary>
public interface IEndpointDefinition
{
    /// <summary>
    /// Configure the API endpoints for this group.
    /// </summary>
    /// <param name="app">The WebApplication instance to configure endpoints on</param>
    void DefineEndpoints(WebApplication app);
}
