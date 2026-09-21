namespace StarterApp.Tests.Integration;

// The xmin row-version token on Product/Order is convention-checked for configuration, but the
// runtime behaviour (a stale write actually failing) was previously untested. This exercises it
// against real PostgreSQL: two contexts load the same row, the first write wins, the second write
// carries a stale xmin and must surface DbUpdateConcurrencyException — which the API maps to 409.
[Collection("Integration Tests")]
public class OptimisticConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public OptimisticConcurrencyIntegrationTests(ApiTestFixture fixture)
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

    [Fact]
    public async Task ConcurrentProductWrites_StaleUpdate_ThrowsConcurrencyException()
    {
        var created = await _fixture.Client.PostAsJsonAsync("/api/v1/products", new CreateProductCommand
        {
            Name = "Concurrency Product",
            Description = "Row version conflict test",
            Price = 9.99m,
            Currency = "USD",
            Stock = 100
        });
        created.EnsureSuccessStatusCode();
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(new DomainEventsInterceptor())
            .Options;

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);

        var firstView = await firstContext.Products.SingleAsync(p => p.Id == product.Id);
        var secondView = await secondContext.Products.SingleAsync(p => p.Id == product.Id);

        // First writer wins and advances the row's xmin.
        firstView.UpdateStock(-5);
        await firstContext.SaveChangesAsync();

        // Second writer still holds the original xmin — its UPDATE matches zero rows.
        secondView.UpdateStock(-10);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }
}
