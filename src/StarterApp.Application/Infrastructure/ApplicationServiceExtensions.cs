using Microsoft.Extensions.DependencyInjection.Extensions;
using StarterApp.ServiceDefaults;

namespace StarterApp.Application.Infrastructure;

public static class ApplicationServiceExtensions
{
    // What the API and the worker both run; anything that needs the web server stays out, or the worker cannot build.
    public static IHostApplicationBuilder AddApplication(this IHostApplicationBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddApplicationDistributedCache();
        builder.Services.AddPersistence(connectionString);
        builder.Services.AddMediator(typeof(IApplicationMarker).Assembly);
        builder.Services.AddCallerIdentity();
        builder.Services.AddAssistant(builder.Configuration);
        return builder;
    }

    // Both hosts share it: a write in either invalidates the by-id rows the other serves.
    public static IHostApplicationBuilder AddApplicationDistributedCache(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
            builder.AddRedisDistributedCache("redis", configureOptions: options => AzureClientAuthentication.ConfigureRedis(options));
        else if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "Testing")
            builder.Services.AddDistributedMemoryCache();
        else
            throw new InvalidOperationException(
                "ConnectionStrings:redis is required outside Development/Testing environments. " +
                "A per-process cache is invalidated only on the replica that took the write, so the others would serve stale rows.");

        return builder;
    }

    public static IServiceCollection AddCallerIdentity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<CurrentUserAccessor>();
        services.TryAddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.TryAddScoped<OwnerPolicyEvaluationTracker>();
        services.TryAddScoped<IOwnerOnlyPolicy, OwnerOnlyPolicy>();
        return services;
    }
}
