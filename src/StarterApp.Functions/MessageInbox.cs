using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace StarterApp.Functions;

// Handler writes that join the claim's transaction commit together with it, so they happen once per message.
public sealed record InboxTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction);

public interface IMessageInbox
{
    // False when this consumer already committed the message: Service Bus delivers at least once and out of order.
    Task<bool> ProcessOnceAsync(string consumer, string messageId, Func<InboxTransaction?, CancellationToken, Task> work, CancellationToken cancellationToken);
}

// A redelivery after commit finds the row and skips; one racing an uncommitted claim waits on the key, then skips or takes over.
public sealed class PostgresMessageInbox : IMessageInbox
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _retentionDays;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PostgresMessageInbox> _logger;
    private DateTimeOffset _lastPurgeUtc;

    public PostgresMessageInbox(NpgsqlDataSource dataSource, int retentionDays, TimeProvider timeProvider, ILogger<PostgresMessageInbox> logger)
    {
        _dataSource = dataSource;
        _retentionDays = retentionDays;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> ProcessOnceAsync(string consumer, string messageId, Func<InboxTransaction?, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(work);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PurgeIfDueAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var claim = new NpgsqlCommand(
            "INSERT INTO inbox_messages (consumer, message_id) VALUES (@consumer, @messageId) ON CONFLICT DO NOTHING", connection, transaction))
        {
            claim.Parameters.AddWithValue("consumer", consumer);
            claim.Parameters.AddWithValue("messageId", messageId);
            if (await claim.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                _logger.LogInformation("Skipping message {MessageId} for {Consumer}: already processed", messageId, consumer);
                return false;
            }
        }

        await work(new InboxTransaction(connection, transaction), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Redelivery stops at the subscription TTL, so rows older than the retention can never be matched again.
    private async Task PurgeIfDueAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        if (nowUtc - _lastPurgeUtc < TimeSpan.FromHours(24))
            return;

        await using var purge = new NpgsqlCommand("DELETE FROM inbox_messages WHERE processed_on_utc < @cutoffUtc", connection);
        purge.Parameters.AddWithValue("cutoffUtc", nowUtc.AddDays(-_retentionDays));
        var purged = await purge.ExecuteNonQueryAsync(cancellationToken);
        _lastPurgeUtc = nowUtc;
        if (purged > 0)
            _logger.LogInformation("Inbox retention purge deleted {Count} rows older than {RetentionDays} days", purged, _retentionDays);
    }
}

// Without a database the work simply runs; standalone runs and tests have no duplicates worth guarding.
public sealed class NullMessageInbox : IMessageInbox
{
    public async Task<bool> ProcessOnceAsync(string consumer, string messageId, Func<InboxTransaction?, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await work(null, cancellationToken);
        return true;
    }
}

public static class MessageInboxExtensions
{
    public static IHostApplicationBuilder AddMessageInbox(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("database");
        if (string.IsNullOrEmpty(connectionString))
        {
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
