using Microsoft.Extensions.DependencyInjection;

namespace StarterApp.Tests.Integration;

public abstract class IntegrationTestBase : IAsyncLifetime, IClassFixture<TestWebApplicationFactory>
{
    protected readonly TestWebApplicationFactory Factory;
    protected readonly HttpClient Client;
    protected readonly IServiceScope Scope;

    protected IntegrationTestBase(TestWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        Scope = factory.Services.CreateScope();
    }

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        await Factory.ResetDatabaseAsync();
        Scope.Dispose();
    }

    protected async Task<T?> GetAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    protected async Task<HttpResponseMessage> PostAsync<T>(string url, T content)
    {
        var response = await Client.PostAsJsonAsync(url, content);
        return response;
    }

    protected async Task<HttpResponseMessage> PutAsync<T>(string url, T content)
    {
        var response = await Client.PutAsJsonAsync(url, content);
        return response;
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string url)
    {
        var response = await Client.DeleteAsync(url);
        return response;
    }
}
