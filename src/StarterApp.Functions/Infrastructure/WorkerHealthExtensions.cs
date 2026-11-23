using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StarterApp.ServiceDefaults;

namespace StarterApp.Functions;

public static class WorkerHealthExtensions
{
    public static IHostApplicationBuilder AddWorkerHealth(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The triggers read the same setting: a keyed connection string locally, the namespace alone when the hosting identity signs in.
        var serviceBus = builder.Configuration["servicebus"] is { Length: > 0 } keyed
            ? keyed
            : builder.Configuration["servicebus:fullyQualifiedNamespace"];
        if (!string.IsNullOrWhiteSpace(serviceBus))
            builder.Services.AddSingleton(_ => AzureClientAuthentication.CreateServiceBusClient(serviceBus));

        builder.Services.AddSingleton(provider => WorkerHealth.For(provider.GetService<ServiceBusClient>(), provider.GetService<NpgsqlDataSource>()));
        return builder;
    }
}
