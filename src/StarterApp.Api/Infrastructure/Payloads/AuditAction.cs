namespace StarterApp.Api.Infrastructure.Payloads;

public sealed record AuditActionMetadata(string Action);

// Derived from the HTTP method; endpoints whose verb misrepresents the action override via WithAuditAction.
public static class AuditAction
{
    public const string Create = "Create";
    public const string Read = "Read";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string StatusChange = "StatusChange";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Create, Read, Update, Delete, StatusChange };

    public static string FromMethod(string method) => method switch
    {
        "GET" or "HEAD" => Read,
        "POST" => Create,
        "PUT" or "PATCH" => Update,
        "DELETE" => Delete,
        _ => Other
    };

    // Request rows are captured before routing, so only response rows can see the endpoint override.
    public static string Resolve(HttpContext context)
    {
        var endpointOverride = context.GetEndpoint()?.Metadata.GetMetadata<AuditActionMetadata>();
        return endpointOverride?.Action ?? FromMethod(context.Request.Method);
    }
}

public static class AuditActionEndpointExtensions
{
    public static RouteHandlerBuilder WithAuditAction(this RouteHandlerBuilder builder, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        builder.WithMetadata(new AuditActionMetadata(action));
        return builder;
    }
}
