using System.Data.Common;

namespace StarterApp.Api.Infrastructure.Tenancy;

// A safety net under each query's own tenant filter. Session settings outlive a request on a pooled connection, so the tenant is written on every open, as '' when there is none.
internal static class TenantSession
{
    public const string Setting = "app.tenant_id";

    private const string Sql = $"SELECT set_config('{Setting}', @tenant_id, false)";

    public static void Apply(DbConnection connection, string? tenantId)
    {
        using var command = CreateCommand(connection, tenantId);
        command.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(DbConnection connection, string? tenantId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(DbConnection connection, string? tenantId)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = Sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenantId ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }
}
