using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StarterApp.ServiceDefaults.Jobs;

namespace Microsoft.Extensions.Hosting;

public static class JobRunRecordingExtensions
{
    // No connection string means a no-op recorder, not a startup failure.
    public static IHostApplicationBuilder AddJobRunRecording(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("database");
        if (string.IsNullOrEmpty(connectionString))
        {
            builder.Services.AddSingleton<IJobRunRecorder, NullJobRunRecorder>();
            return builder;
        }

        var retentionDays = builder.Configuration.GetValue("JobRuns:RetentionDays", 30);
        builder.Services.AddDatabaseDataSource(connectionString);
        builder.Services.AddSingleton<IJobRunRecorder>(provider => new NpgsqlJobRunRecorder(
            provider.GetRequiredService<NpgsqlDataSource>(),
            retentionDays,
            provider.GetRequiredService<ILogger<NpgsqlJobRunRecorder>>(),
            provider.GetRequiredService<TimeProvider>()));
        return builder;
    }
}
