using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StarterApp.ServiceDefaults;

namespace Microsoft.Extensions.Hosting;

public static class DatabaseServiceCollectionExtensions
{
    // One NpgsqlDataSource per process, whoever asks first. EF Core, Dapper and the job-run
    // recorder all resolve the same instance, so the managed-identity password provider is
    // configured once and every connection in the process carries the token.
    public static IServiceCollection AddDatabaseDataSource(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(_ => DatabaseAuthentication.CreateDataSource(connectionString));
        return services;
    }
}
