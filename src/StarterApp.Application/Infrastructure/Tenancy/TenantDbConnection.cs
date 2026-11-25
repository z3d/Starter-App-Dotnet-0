using System.Data.Common;
using Npgsql;

namespace StarterApp.Application.Infrastructure.Tenancy;

internal static class TenantDbConnection
{
    // Dapper opens a closed connection itself, on each call and each retry, so the tenant is written whenever this connection opens.
    public static NpgsqlConnection Create(NpgsqlDataSource dataSource, ICurrentUser? currentUser)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var tenantId = currentUser?.TenantId;
        var connection = dataSource.CreateConnection();
        connection.StateChange += (sender, change) =>
        {
            if (change.CurrentState == ConnectionState.Open && sender is DbConnection opened)
                TenantSession.Apply(opened, tenantId);
        };
        return connection;
    }
}
