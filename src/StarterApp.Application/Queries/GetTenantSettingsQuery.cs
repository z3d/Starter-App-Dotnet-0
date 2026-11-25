namespace StarterApp.Application.Queries;

public class GetTenantSettingsQuery : IQuery<TenantSettingsReadModel>, IOwnerScopedRequest
{
}

public class GetTenantSettingsQueryHandler : IRequestHandler<GetTenantSettingsQuery, TenantSettingsReadModel>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<GetTenantSettingsQueryHandler> _logger;

    public GetTenantSettingsQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<GetTenantSettingsQueryHandler> logger)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<TenantSettingsReadModel> HandleAsync(GetTenantSettingsQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling GetTenantSettingsQuery");
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        var sqlQuery = @"
            SELECT
                palette AS ""Palette"",
                updated_by AS ""UpdatedBy"",
                last_updated AS ""LastUpdated""
            FROM tenant_settings
            WHERE tenant_id = @TenantId";

        var settings = await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<TenantSettingsReadModel>(
                new CommandDefinition(sqlQuery, new { ownerScope.TenantId }, cancellationToken: ct)),
            cancellationToken);

        return settings ?? new TenantSettingsReadModel();
    }
}
