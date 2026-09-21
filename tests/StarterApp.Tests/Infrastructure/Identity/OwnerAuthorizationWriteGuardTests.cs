using Microsoft.EntityFrameworkCore.Infrastructure;

namespace StarterApp.Tests.Infrastructure.Identity;

public class OwnerAuthorizationWriteGuardTests
{
    private sealed class Row
    {
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class RowContext : DbContext
    {
        public RowContext(DbContextOptions<RowContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();
    }

    private static RowContext CreateContext(OwnerPolicyEvaluationTracker tracker)
    {
        var options = new DbContextOptionsBuilder<RowContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(new OwnerAuthorizationWriteGuard(tracker))
            .Options;
        return new RowContext(options);
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
