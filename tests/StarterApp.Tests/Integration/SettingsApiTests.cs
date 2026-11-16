using System.Net.Http.Headers;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class SettingsApiTests : IAsyncLifetime
{
    private const string Path = "/api/v1/settings/appearance";
    private const string StaffScopes = "customers:read customers:write orders:read orders:write products:read products:write settings:read";

    private readonly ApiTestFixture _fixture;

    public SettingsApiTests(ApiTestFixture fixture)
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
    public async Task ATenantThatHasChosenNothing_ReadsNoPalette()
    {
        var settings = await _fixture.Client.GetFromJsonAsync<TenantSettingsReadModel>(Path);

        Assert.NotNull(settings);
        Assert.Null(settings.Palette);
    }

    [Fact]
    public async Task AnAdministratorsChoice_IsWhatEveryoneInTheTenantReads()
    {
        var saved = await _fixture.Client.PutAsJsonAsync(Path, new UpdateTenantSettingsCommand { Palette = "sky" });
        saved.EnsureSuccessStatusCode();
        var changed = await saved.Content.ReadFromJsonAsync<TenantSettingsDto>();
        Assert.Equal("sky", changed!.Palette);
        Assert.Equal(TestJwtIdentity.DefaultSubject, changed.UpdatedBy);

        var again = await _fixture.Client.PutAsJsonAsync(Path, new UpdateTenantSettingsCommand { Palette = "coral" });
        again.EnsureSuccessStatusCode();

        var colleague = await SendAsync(HttpMethod.Get, subject: "a-colleague", scopes: StaffScopes);
        colleague.EnsureSuccessStatusCode();
        var read = await colleague.Content.ReadFromJsonAsync<TenantSettingsReadModel>();
        Assert.Equal("coral", read!.Palette);
        Assert.Equal(TestJwtIdentity.DefaultSubject, read.UpdatedBy);
        Assert.NotNull(read.LastUpdated);
    }

    [Fact]
    public async Task APersonWithoutTheAdministratorsScope_CannotChangeIt()
    {
        var response = await SendAsync(HttpMethod.Put, subject: "a-colleague", scopes: StaffScopes, palette: "sky");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("scope=\"settings:write\"", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        var settings = await _fixture.Client.GetFromJsonAsync<TenantSettingsReadModel>(Path);
        Assert.Null(settings!.Palette);
    }

    [Fact]
    public async Task AnAdministratorWithoutASecondFactor_CannotChangeIt()
    {
        var response = await SendAsync(HttpMethod.Put, authenticationMethods: "pwd", palette: "sky");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task APersonWithoutTheReadScope_CannotReadIt()
    {
        var response = await SendAsync(HttpMethod.Get, scopes: "products:read");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sky")]
    [InlineData("sky blue")]
    [InlineData("a-name-that-runs-past-thirty-two-c")]
    public async Task APaletteThatIsNotAName_IsRefused(string palette)
    {
        var response = await _fixture.Client.PutAsJsonAsync(Path, new UpdateTenantSettingsCommand { Palette = palette });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnotherTenantsChoice_IsNotSeen()
    {
        (await _fixture.Client.PutAsJsonAsync(Path, new UpdateTenantSettingsCommand { Palette = "fresh" })).EnsureSuccessStatusCode();

        var response = await SendAsync(HttpMethod.Get, subject: "someone-else", tenantId: "another-tenant");

        response.EnsureSuccessStatusCode();
        var settings = await response.Content.ReadFromJsonAsync<TenantSettingsReadModel>();
        Assert.Null(settings!.Palette);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string subject = TestJwtIdentity.DefaultSubject,
        string tenantId = TestJwtIdentity.DefaultTenantId,
        string scopes = TestJwtIdentity.DefaultScopes,
        string authenticationMethods = TestJwtIdentity.DefaultAuthenticationMethods,
        string? palette = null)
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(method, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtIdentity.CreateToken(subject, tenantId, scopes, authenticationMethods));
        if (palette is not null)
            request.Content = JsonContent.Create(new UpdateTenantSettingsCommand { Palette = palette });
        return await client.SendAsync(request);
    }
}
