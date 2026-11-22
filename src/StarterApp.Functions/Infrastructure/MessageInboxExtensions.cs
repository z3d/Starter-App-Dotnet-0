using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace StarterApp.Functions;

public static class MessageInboxExtensions
{
    public static IHostApplicationBuilder AddMessageInbox(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("database");
        if (string.IsNullOrEmpty(connectionString))
        {
            // Without the inbox a redelivered message runs its work twice, so only a developer's own run may go without it.
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("ConnectionStrings:database is required outside Development: without it the message inbox cannot deduplicate redeliveries.");

            builder.Services.AddSingleton<IMessageInbox, NullMessageInbox>();
            return builder;
        }

        var retentionDays = builder.Configuration.GetValue("Inbox:RetentionDays", 7);
        builder.Services.AddDatabaseDataSource(connectionString);
        builder.Services.AddSingleton<IMessageInbox>(provider => new PostgresMessageInbox(
            provider.GetRequiredService<NpgsqlDataSource>(),
            retentionDays,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PostgresMessageInbox>>()));
        return builder;
    }
}
