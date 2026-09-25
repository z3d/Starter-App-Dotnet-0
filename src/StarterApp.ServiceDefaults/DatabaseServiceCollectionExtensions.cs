using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StarterApp.ServiceDefaults;

namespace Microsoft.Extensions.Hosting;

public static class DatabaseServiceCollectionExtensions
{
    // One data source per process, whoever asks first, so every connection carries the token.
    public static IServiceCollection AddDatabaseDataSource(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(_ => DatabaseAuthentication.CreateDataSource(connectionString));
        return services;
    }
}
