namespace StarterApp.Api.Infrastructure.Assistant;

public sealed record AssistantRoute(string Method, string Path, string Summary, string Fields, IReadOnlyList<string> Admits, IReadOnlyList<AssistantField>? Body = null)
{
    public IReadOnlyList<AssistantField> BodyFields => Body ?? [];

    public bool IsRead => Method == "GET";

    // What a card names: the back-office roles, or the one kind of person, never the staff umbrella.
    public IReadOnlyList<string> Roles => Admits.Where(role => role != StarterApp.Api.Infrastructure.Identity.Roles.Staff).ToList();
}
