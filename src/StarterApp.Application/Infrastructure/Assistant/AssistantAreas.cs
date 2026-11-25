namespace StarterApp.Application.Infrastructure.Assistant;

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
