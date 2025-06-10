using Microsoft.Extensions.Configuration;

namespace DockerLearning.ServiceBus;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(configuration.GetSection(ServiceBusOptions.SectionName));
        services.AddSingleton<IServiceBusService, ServiceBusService>();
        
        return services;
    }
}