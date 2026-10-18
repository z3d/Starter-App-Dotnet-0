using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StarterApp.ServiceDefaults.Jobs;

namespace Microsoft.Extensions.Hosting;

public static class JobRunRecordingExtensions
{
    // No connection string means a recorder that does nothing, not a start-up failure.
    public static IHostApplicationBuilder AddJobRunRecording(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("database");
        if (string.IsNullOrEmpty(connectionString))
        {
            builder.Services.AddSingleton<IJobRunRecorder, NullJobRunRecorder>();
            builder.Services.AddSingleton<IJobRunHistory, NoJobRunHistory>();
            return builder;
        }

        var retentionDays = builder.Configuration.GetValue("JobRuns:RetentionDays", 30);
        builder.Services.AddDatabaseDataSource(connectionString);
        builder.Services.AddSingleton<IJobRunRecorder>(provider => new NpgsqlJobRunRecorder(
            provider.GetRequiredService<NpgsqlDataSource>(),
            retentionDays,
            provider.GetRequiredService<ILogger<NpgsqlJobRunRecorder>>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<IJobRunHistory>(provider => new NpgsqlJobRunHistory(provider.GetRequiredService<NpgsqlDataSource>()));
        return builder;
    }
}
