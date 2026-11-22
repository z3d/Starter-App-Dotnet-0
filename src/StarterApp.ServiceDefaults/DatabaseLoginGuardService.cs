using Microsoft.Extensions.Hosting;
using Npgsql;

namespace StarterApp.ServiceDefaults;

internal sealed class DatabaseLoginGuardService(NpgsqlDataSource dataSource) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var violations = await DatabaseLoginGuard.FindViolationsAsync(connection, null, cancellationToken);
        if (violations.Count > 0)
            throw DatabaseLoginGuard.Refusal(connection.UserName ?? "in use", violations);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
