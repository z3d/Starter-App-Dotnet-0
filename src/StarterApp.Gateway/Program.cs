using System.Text;

// Dev-only emulator of a trusted APIM gateway. Fronts the API in local orchestration: strips inbound
// caller identity, projects normalized X-Authenticated-* headers (caller-stated or the default dev
// identity), and signs the X-Gateway-Assertion the API verifies in Required mode. Never deployed —
// in real environments APIM (or an equivalent gateway) is the front door.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<GatewaySignerOptions>()
    .Bind(builder.Configuration.GetSection(GatewaySignerOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "GatewaySigner:SigningKey must be configured and at least 32 bytes; AppHost supplies a per-run key when orchestrated.")
    .Validate(
        options => options.TokenLifetimeSeconds is > 0 and <= 120,
        "GatewaySigner:TokenLifetimeSeconds must be between 1 and 120 seconds (the API's maximum token lifetime).")
    .ValidateOnStart();

builder.Services.AddSingleton<GatewayIdentityProjectionResolver>();
builder.Services.AddSingleton<GatewayAssertionSigner>();

builder.Services.AddReverseProxy()
    .LoadFromMemory(GatewayProxyConfig.Routes, GatewayProxyConfig.Clusters)
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms<GatewayAssertionTransformProvider>();

var app = builder.Build();

// /health and /alive answer locally (literal routes outrank the catch-all proxy route);
// /health/ready is deliberately unmapped here so it proxies through to the API's readiness.
app.MapDefaultEndpoints();

app.MapReverseProxy();

app.Run();
