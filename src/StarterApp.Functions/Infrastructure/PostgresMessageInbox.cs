using Microsoft.Extensions.Logging;
using Npgsql;

namespace StarterApp.Functions;

// A redelivery after the commit finds the row and skips the work. One that races an uncommitted claim waits on the row lock, then skips or takes over.
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

    // Service Bus stops redelivering at the subscription's TTL, so a row older than that can never match a message again.
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
