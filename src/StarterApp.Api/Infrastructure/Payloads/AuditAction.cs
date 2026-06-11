namespace StarterApp.Api.Infrastructure.Payloads;

public sealed record AuditActionMetadata(string Action);

// Business-action taxonomy stamped onto payload-capture audit rows so support and
// compliance queries can ask "all deletes by subject X" without parsing routes.
// Derived from the HTTP method; routes whose verb misrepresents the business action
// override per endpoint via WithAuditAction (allowlist convention-tested).
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

    // Request rows are captured before routing selects an endpoint, so they carry the
    // verb-derived action; response rows resolve the endpoint override here.
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
