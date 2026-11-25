namespace StarterApp.Application.Infrastructure.Assistant;

public sealed record AssistantRoute(string Method, string Path, string Summary, string Fields, string Scope, IReadOnlyList<AssistantField>? Body = null)
{
    public IReadOnlyList<AssistantField> BodyFields => Body ?? [];

    public bool IsRead => Method == "GET";
}
