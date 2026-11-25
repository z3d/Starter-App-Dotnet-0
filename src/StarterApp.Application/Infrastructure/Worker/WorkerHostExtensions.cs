namespace StarterApp.Application.Infrastructure.Worker;

public static class WorkerHostExtensions
{
    public static IHostApplicationBuilder AddWorkerOperations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddSingleton<IWorkerOperations, UnconfiguredWorkerOperations>();
            return builder;
        }

        builder.AddApplication(connectionString);
        builder.Services.AddSingleton<IWorkerOperations, WorkerOperations>();
        return builder;
    }
}
