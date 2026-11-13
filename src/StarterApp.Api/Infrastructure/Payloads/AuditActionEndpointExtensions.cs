namespace StarterApp.Api.Infrastructure.Payloads;

public static class AuditActionEndpointExtensions
{
    public static RouteHandlerBuilder WithAuditAction(this RouteHandlerBuilder builder, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        builder.WithMetadata(new AuditActionMetadata(action));
        return builder;
    }
}
