using Npgsql;

namespace StarterApp.Functions;

// Handler writes made on this transaction commit together with the inbox claim, so they happen once per message.
public sealed record InboxTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction)
{
    // The work starts with no tenant and sees no tenant rows; a handler that writes a tenant's table binds the tenant its event names, for this transaction only.
    public async Task BindTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        await using var bind = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenantId, true)", Connection, Transaction);
        bind.Parameters.AddWithValue("tenantId", tenantId);
        await bind.ExecuteNonQueryAsync(cancellationToken);
    }
}
