namespace StarterApp.Api.Application.Commands;

public class UpdateTenantSettingsCommand : ICommand, IRequest<TenantSettingsDto>
{
    public string? Palette { get; set; }
}

public class UpdateTenantSettingsCommandHandler : IRequestHandler<UpdateTenantSettingsCommand, TenantSettingsDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<UpdateTenantSettingsCommandHandler> _logger;

    public UpdateTenantSettingsCommandHandler(ApplicationDbContext dbContext, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<UpdateTenantSettingsCommandHandler> logger)
    {
        _dbContext = dbContext;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<TenantSettingsDto> HandleAsync(UpdateTenantSettingsCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling UpdateTenantSettingsCommand");

        var scope = _ownerOnlyPolicy.GetRequiredScope();
        var settings = await SaveAsync(scope, command.Palette!, cancellationToken);

        _logger.LogInformation("Tenant palette set to {Palette} by {Subject}", settings.Palette, settings.UpdatedBy);

        return new TenantSettingsDto
        {
            Palette = settings.Palette,
            UpdatedBy = settings.UpdatedBy,
            LastUpdated = settings.LastUpdated
        };
    }

    private async Task<TenantSettings> SaveAsync(OwnerScope scope, string palette, CancellationToken cancellationToken)
    {
        try
        {
            return await UpsertAsync(scope, palette, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation("pk_tenant_settings"))
        {
            // Two first saves raced; the row now exists, so the second becomes an update.
            _dbContext.ChangeTracker.Clear();
            return await UpsertAsync(scope, palette, cancellationToken);
        }
    }

    private async Task<TenantSettings> UpsertAsync(OwnerScope scope, string palette, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TenantSettings.FindAsync([scope.TenantId], cancellationToken);
        if (settings is null)
        {
            settings = new TenantSettings(scope.TenantId, palette, scope.OwnerSubject);
            _dbContext.TenantSettings.Add(settings);
        }
        else
        {
            settings.ChoosePalette(palette, scope.OwnerSubject);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
