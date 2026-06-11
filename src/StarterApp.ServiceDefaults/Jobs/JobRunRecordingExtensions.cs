using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Jobs;

namespace Microsoft.Extensions.Hosting;

public static class JobRunRecordingExtensions
{
    // Conditional like Service Bus registration: standalone dev and unit tests without a
    // database connection string get a no-op recorder instead of a startup failure.
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
        builder.Services.AddSingleton<IJobRunRecorder>(provider => new NpgsqlJobRunRecorder(
            connectionString,
            retentionDays,
            provider.GetRequiredService<ILogger<NpgsqlJobRunRecorder>>()));
        return builder;
    }
}
