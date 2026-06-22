namespace StarterApp.Api.Infrastructure.Mediator;

public static class MediatorServiceExtensions
{
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IMediator, Mediator>();
        services.AddSingleton<FeatureToggles.IFeatureToggles, FeatureToggles.ConfigurationFeatureToggles>();
        // Feature toggles are gated in the mediator BEFORE validators and before any pipeline
        // behavior runs (Mediator.RunFeatureToggleGate), so a disabled feature can never be served
        // from cache. Registration order below still matters for the remaining behaviors: first
        // registered runs outermost.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(Identity.OwnerAuthorizationBehavior<,>));
        services.AddScoped<ICacheInvalidator, CacheInvalidator>();

        foreach (var assembly in assemblies)
        {
            RegisterHandlers(services, assembly);
            RegisterValidators(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i => IsHandlerInterface(i)))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(IsHandlerInterface);

            foreach (var @interface in interfaces)
            {
                services.AddScoped(@interface, handlerType);
            }
        }
    }

    private static void RegisterValidators(IServiceCollection services, Assembly assembly)
    {
        var validatorTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(IsValidatorInterface))
            .ToList();

        foreach (var validatorType in validatorTypes)
        {
            var interfaces = validatorType.GetInterfaces()
                .Where(IsValidatorInterface);

            foreach (var @interface in interfaces)
            {
                services.AddScoped(@interface, validatorType);
            }
        }
    }

    private static bool IsHandlerInterface(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var genericTypeDefinition = type.GetGenericTypeDefinition();
        return genericTypeDefinition == typeof(IRequestHandler<,>);
    }

    private static bool IsValidatorInterface(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidator<>);
    }
}
