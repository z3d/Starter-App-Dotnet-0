namespace StarterApp.Api.Infrastructure.Assistant;

// The places a person works in, as the web app has them, and the scope that admits a person to each. The starter has one.
public static class AssistantAreas
{
    public const string Scope = "assistant:use";
    public const string App = "app";

    public static string? ScopeOf(string? area) => area switch
    {
        null or App => Scope,
        _ => null,
    };

    public static void EnsureAdmitted(string? area, ICurrentUser currentUser)
    {
        if (ScopeOf(area) is not { } scope || !currentUser.HasScope(scope))
            throw new ForbiddenAccessException($"Your sign-in has no scope for the {area ?? App} area.");
    }
}
