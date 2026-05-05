using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scalar.AspNetCore;
using StarterApp.Api.Infrastructure.HealthChecks;
using StarterApp.Api.Infrastructure.Outbox;
using System.Text;

namespace StarterApp.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var exception = context.HttpContext.Features
                    .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()
                    ?.Error;

                if (exception is not ValidationException validationException)
                    return;

                context.ProblemDetails.Extensions["errors"] = validationException.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray(),
                        StringComparer.Ordinal);
            };
        });

        return services;
    }

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "Starter App API";
                document.Info.Version = "v1";
                document.Info.Description = "A sample API for the Starter App built with .NET 10 Minimal APIs";
                document.Info.Contact = new() { Name = "Starter App Team" };
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        // EnableRetryOnFailure is safe here because:
        //   1. ApplicationDbContext uses a single-SaveChanges outbox (no user transaction),
        //      so the retrying execution strategy does not have to refuse the flow.
        //   2. CreateOrderCommandHandler's user transaction (stock reservation + order save)
        //      is wrapped in Database.CreateExecutionStrategy().ExecuteAsync, which is the
        //      retry-safe pattern documented by Microsoft.
        // If a future handler opens BeginTransaction without wrapping in an execution strategy,
        // the first transient fault will throw at runtime with a clear message.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null))
                   .EnableSensitiveDataLogging(false));

        // Dapper reads use this connection. Retries are NOT attached at the SqlConnection level
        // because SqlConnection.RetryLogicProvider only covers Open() — Dapper creates its own
        // SqlCommands whose RetryLogicProvider defaults to null, so query-time transient faults
        // would slip through. Query handlers wrap their Dapper calls in SqlRetryPolicy.ExecuteAsync
        // instead, which is enforced by DapperConventionTests.QueryHandlers_MustUseSqlRetryPolicy.
        services.AddScoped<System.Data.IDbConnection>(provider =>
            new Microsoft.Data.SqlClient.SqlConnection(connectionString));

        return services;
    }

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (environment.IsDevelopment())
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                else
                    policy.WithOrigins(configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                          .WithMethods("GET", "POST", "PUT", "DELETE")
                          .WithHeaders("Authorization", "Content-Type");
            });
        });

        return services;
    }

    public static IServiceCollection AddGatewayIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<GatewayIdentityOptions>()
            .Bind(configuration.GetSection(GatewayIdentityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsDevelopmentLike(environment) || options.Mode == GatewayIdentityMode.Required,
                "GatewayIdentity:Mode=UnsignedDevelopment is only allowed in Development, Testing, or Docker environments.")
            .Validate(options => options.Mode != GatewayIdentityMode.Required || !string.IsNullOrWhiteSpace(options.SigningKey),
                "GatewayIdentity:SigningKey is required when GatewayIdentity:Mode=Required.")
            .Validate(options => options.Mode != GatewayIdentityMode.Required ||
                                 (!string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32),
                "GatewayIdentity:SigningKey must be at least 32 bytes when GatewayIdentity:Mode=Required.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddSingleton<IGatewayAssertionValidator, GatewayAssertionValidator>();

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
                var key = currentUser is { IsAuthenticated: true }
                    ? $"identity:{currentUser.TenantId}:{currentUser.Subject}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 5
                });
            });

            options.AddFixedWindowLimiter("fixed", options =>
            {
                options.PermitLimit = 100;
                options.Window = TimeSpan.FromMinutes(1);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 5;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    private static bool IsDevelopmentLike(IHostEnvironment environment)
    {
        return environment.IsDevelopment() ||
            environment.EnvironmentName == "Testing" ||
            environment.EnvironmentName == "Docker";
    }

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddServiceBusPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("servicebus");
        if (string.IsNullOrEmpty(connectionString))
            return services;

        services.AddOptions<OutboxProcessorOptions>()
            .Bind(configuration.GetSection("OutboxProcessor"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new OutboxProcessorOptions();
        configuration.GetSection("OutboxProcessor").Bind(options);

        services.AddSingleton(_ => new ServiceBusClient(connectionString));
        services.AddSingleton(provider =>
            provider.GetRequiredService<ServiceBusClient>().CreateSender(options.TopicName));
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
