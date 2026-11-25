namespace StarterApp.Application.Infrastructure.Assistant;

public static class AssistantServiceExtensions
{
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.SectionName));
        services.AddHttpClient(ClaudeAssistantModel.HttpClientName);
        services.AddSingleton<IAssistantModel, ClaudeAssistantModel>();
        services.AddSingleton<AssistantCatalogue>();
        return services;
    }
}
