using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StarterApp.Api.Infrastructure.Tenancy;

internal sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentUser? _currentUser;

    public TenantConnectionInterceptor(ICurrentUser? currentUser)
    {
        _currentUser = currentUser;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        TenantSession.Apply(connection, _currentUser?.TenantId);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        TenantSession.ApplyAsync(connection, _currentUser?.TenantId, cancellationToken);
}
