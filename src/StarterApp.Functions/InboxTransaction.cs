using Npgsql;

namespace StarterApp.Functions;

// Handler writes made on this transaction commit together with the inbox claim, so they happen once per message.
public sealed record InboxTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction);
