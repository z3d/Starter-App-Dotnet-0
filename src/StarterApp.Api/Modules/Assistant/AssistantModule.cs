using StarterApp.Api.Modules.Assistant.Application;

namespace StarterApp.Api.Modules.Assistant;

public static class AssistantModule
{
    public static IServiceCollection AddAssistantModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.SectionName));
        services.AddHttpClient(ClaudeAssistantModel.HttpClientName);
        services.AddSingleton<IAssistantModel, ClaudeAssistantModel>();
        services.AddSingleton<AssistantCatalogue>();
        services.AddScoped<AssistantTurns>();
        return services;
    }
}
