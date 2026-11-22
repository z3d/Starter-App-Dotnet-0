using Microsoft.EntityFrameworkCore.Infrastructure;

namespace StarterApp.Tests.Infrastructure.Identity;

public class OwnerAuthorizationWriteGuardTests
{
    private sealed class Row
    {
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class OwnedRow
    {
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
        public string OwnerSubject { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
    }

    private sealed class RowContext : DbContext
    {
        public RowContext(DbContextOptions<RowContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        public DbSet<OwnedRow> OwnedRows => Set<OwnedRow>();
    }

    private static readonly CurrentUser Caller = new("caller", AuthenticatedPrincipalType.User, "tenant-a", []);

    private static RowContext CreateContext(OwnerPolicyEvaluationTracker tracker, string? database = null) =>
        new(new DbContextOptionsBuilder<RowContext>()
            .UseInMemoryDatabase(database ?? Guid.NewGuid().ToString("N"))
            .AddInterceptors(new OwnerAuthorizationWriteGuard(tracker, Caller))
            .Options);

    private static OwnerPolicyEvaluationTracker Authorized()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        tracker.RequireEvaluation();
        tracker.MarkEvaluated();
        return tracker;
    }

    private static async Task<(string Database, int Mine, int Theirs)> SeedAsync()
    {
        var database = Guid.NewGuid().ToString("N");
        await using var seed = CreateContext(new OwnerPolicyEvaluationTracker(), database);
        var mine = new OwnedRow { Value = "mine", OwnerSubject = Caller.Subject, TenantId = Caller.TenantId };
        var theirs = new OwnedRow { Value = "theirs", OwnerSubject = "someone-else", TenantId = Caller.TenantId };
        seed.OwnedRows.AddRange(mine, theirs);
        await seed.SaveChangesAsync();
        return (database, mine.Id, theirs.Id);
    }

    [Fact]
    public async Task SaveChanges_AfterAuthorizingOneRow_RefusesAChangeToAnotherOwnersRow()
    {
        var (database, mine, theirs) = await SeedAsync();
        await using var context = CreateContext(Authorized(), database);

        (await context.OwnedRows.SingleAsync(row => row.Id == mine)).Value = "changed";
        (await context.OwnedRows.SingleAsync(row => row.Id == theirs)).Value = "changed too";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_AfterAuthorizing_RefusesDeletingAnotherOwnersRow()
    {
        var (database, _, theirs) = await SeedAsync();
        await using var context = CreateContext(Authorized(), database);

        context.OwnedRows.Remove(await context.OwnedRows.SingleAsync(row => row.Id == theirs));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_AfterAuthorizing_RefusesHandingTheCallersRowToAnotherOwner()
    {
        var (database, mine, _) = await SeedAsync();
        await using var context = CreateContext(Authorized(), database);

        (await context.OwnedRows.SingleAsync(row => row.Id == mine)).OwnerSubject = "someone-else";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_AfterAuthorizing_WritesTheCallersOwnRows()
    {
        var (database, mine, _) = await SeedAsync();
        await using var context = CreateContext(Authorized(), database);

        (await context.OwnedRows.SingleAsync(row => row.Id == mine)).Value = "changed";

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_ForFlaggedRequestWithoutAuthorize_ThrowsBeforeWriting()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        tracker.RequireEvaluation();
        await using var context = CreateContext(tracker);
        context.Rows.Add(new Row { Value = "unauthorized" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        await using var verify = CreateContext(new OwnerPolicyEvaluationTracker());
        Assert.Empty(await verify.Rows.ToListAsync());
    }

    [Fact]
    public async Task SaveChanges_ForFlaggedRequestAfterAuthorize_Writes()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        tracker.RequireEvaluation();
        tracker.MarkEvaluated();
        await using var context = CreateContext(tracker);
        context.Rows.Add(new Row { Value = "authorized" });

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_ForUnflaggedRequest_Writes()
    {
        await using var context = CreateContext(new OwnerPolicyEvaluationTracker());
        context.Rows.Add(new Row { Value = "create or background work" });

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public void NonQueryCommand_ForFlaggedRequestWithoutAuthorize_Throws()
    {
        // ExecuteUpdate and ExecuteDelete bypass SaveChanges and reach the database as
        // non-query commands, so the guard has to catch those too.
        var tracker = new OwnerPolicyEvaluationTracker();
        tracker.RequireEvaluation();
        var guard = new OwnerAuthorizationWriteGuard(tracker);

        Assert.Throws<InvalidOperationException>(() => guard.NonQueryExecuting(null!, null!, default));
    }

    [Fact]
    public void AddPersistence_AttachesTheGuardToTheDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Host=localhost;Database=test;Username=postgres;Password=postgres");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var interceptors = dbContext.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        Assert.Contains(interceptors, interceptor => interceptor is OwnerAuthorizationWriteGuard);
    }
}
