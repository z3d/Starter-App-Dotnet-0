using Npgsql;

namespace StarterApp.Functions;

// With no database the worker still indexes its functions; there is then nothing to purge.
public sealed class IdempotencyRecordPurge(NpgsqlDataSource? dataSource)
{
    public const int BatchSize = 1000;

    public async Task<IdempotencyRecordPurgeResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, TimeSpan budget, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (dataSource is null)
            return new IdempotencyRecordPurgeResult(0, false);

        const string sql = """
            DELETE FROM idempotency_records
            WHERE ctid IN (SELECT ctid FROM idempotency_records WHERE created_on_utc < @cutoffUtc LIMIT @batchSize)
            """;

        var deadline = timeProvider.GetUtcNow() + budget;
        var total = 0;
        while (true)
        {
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("cutoffUtc", cutoffUtc);
            command.Parameters.AddWithValue("batchSize", BatchSize);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
                return new IdempotencyRecordPurgeResult(total, false);
            if (timeProvider.GetUtcNow() >= deadline)
                return new IdempotencyRecordPurgeResult(total, true);
        }
    }
}
