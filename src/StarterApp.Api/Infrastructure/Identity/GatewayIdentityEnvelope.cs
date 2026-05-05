namespace StarterApp.Api.Infrastructure.Identity;

internal sealed record GatewayIdentityEnvelope(
    CurrentUser User,
    string HeaderHash);
