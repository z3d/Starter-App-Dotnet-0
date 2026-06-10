using Npgsql;

namespace StarterApp.DbMigrator;

public static class OutboxReplayer
{
    // The sanctioned operator recovery path for errored outbox rows (see
    // docs/runbooks/event-replay.md). Mirrors OutboxMessage.ResetForReplay
    // semantics: only unprocessed, errored rows are eligible; the reset clears
    // error state and any stale claim, restores the retry budget, and stamps
    // replay metadata so the processor can mark the republished message.
    // OutboxReplayTests asserts both representations stay in sync.
    private const string ReplayByIdSql = """
        UPDATE outbox_messages
        SET error = NULL,
            processing_id = NULL,
            locked_until_utc = NULL,
            retry_count = 0,
            replay_count = replay_count + 1,
            replayed_on_utc = now()
        WHERE processed_on_utc IS NULL
          AND error IS NOT NULL
          AND id = @id
        """;

    private const string ReplayAllErroredSql = """
        UPDATE outbox_messages
        SET error = NULL,
            processing_id = NULL,
            locked_until_utc = NULL,
            retry_count = 0,
            replay_count = replay_count + 1,
            replayed_on_utc = now()
        WHERE processed_on_utc IS NULL
          AND error IS NOT NULL
        """;

    public static int Run(string connectionString, string[] args)
    {
        Guid? messageId = null;
        var allErrored = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--id" when i + 1 < args.Length && Guid.TryParse(args[i + 1], out var parsed):
                    messageId = parsed;
                    i++;
                    break;
                case "--all-errored":
                    allErrored = true;
                    break;
                default:
                    Log.Error("Unrecognized replay argument '{Argument}'", args[i]);
                    return Usage();
            }
        }

        var hasId = messageId is not null;
        if (hasId == allErrored)
            return Usage();

        var affected = Execute(connectionString, messageId);

        if (messageId is not null && affected == 0)
        {
            Log.Warning("Outbox message {MessageId} was not replayed: it does not exist, is already processed, or is not errored", messageId);
            return 1;
        }

        Log.Information("Reset {Count} errored outbox message(s) for replay; the OutboxProcessor will republish on its next poll", affected);
        return 0;
    }

    private static int Execute(string connectionString, Guid? messageId)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var command = messageId is null
            ? new NpgsqlCommand(ReplayAllErroredSql, connection)
            : new NpgsqlCommand(ReplayByIdSql, connection);

        if (messageId is not null)
            command.Parameters.AddWithValue("id", messageId.Value);

        return command.ExecuteNonQuery();
    }

    private static int Usage()
    {
        Log.Error("Usage: replay-outbox --id <guid> | replay-outbox --all-errored");
        return 2;
    }
}
