using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace StarterApp.Gateway.Proxy;

internal static class GatewayProxyConfig
{
    public static IReadOnlyList<RouteConfig> Routes { get; } =
    [
        new RouteConfig
        {
            RouteId = "api",
            ClusterId = "api",
            Match = new RouteMatch { Path = "{**catch-all}" },
        }
        // Preserve the caller's Host: MapOpenApi derives the OpenAPI document's server URL from
        // the request host, so Scalar opened via the gateway must see the gateway origin or its
        // try-it requests would bypass the proxy and hit the Required-mode API unsigned.
        .WithTransformUseOriginalHostHeader(useOriginal: true),
    ];

    public static IReadOnlyList<ClusterConfig> Clusters { get; } =
    [
        new ClusterConfig
        {
            ClusterId = "api",
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                // Aspire service-discovery URI resolved from the services__api__* env vars
                // injected by WithReference(api) in AppHost.
                ["api"] = new DestinationConfig { Address = "https+http://api" },
            },
        },
    ];
}
