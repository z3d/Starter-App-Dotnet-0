namespace StarterApp.Tests.Application.Commands;

public abstract class PostgresCommandHandlerTestBase : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    protected PostgresCommandHandlerTestBase(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    protected ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(new DomainEventsInterceptor())
            .Options;

        return new ApplicationDbContext(options);
    }
}
