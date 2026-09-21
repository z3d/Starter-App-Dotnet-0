using DatabaseAuthentication = StarterApp.ServiceDefaults.DatabaseAuthentication;

namespace StarterApp.Tests.Infrastructure.Persistence;

public class DatabaseAuthenticationTests
{
    // Synthetic values only.
    [Theory]
    [InlineData("Host=db;Database=app;Username=svc", true)]
    [InlineData("Host=db;Database=app;Username=svc;Password=", true)]
    [InlineData("Host=db;Database=app;Username=svc;Password=plainzz1", false)]
    [InlineData("Host=db;Database=app;Username=svc;Pwd=plainzz1", false)]
    [InlineData("Host=db;Database=app;Username=svc;PSW=plainzz1", false)]
    [InlineData("Host=db;Database=app", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("this is ;;; not=a=connection", false)]
    public void UsesManagedIdentity_ForEveryPasswordlessStringWithAHost(string? connectionString, bool expected)
    {
        Assert.Equal(expected, DatabaseAuthentication.UsesManagedIdentity(connectionString));
    }

    [Fact]
    public void CreateDataSource_WithAPassword_DoesNotTouchTheCredential()
    {
        var credential = new CountingCredential();

        using var dataSource = DatabaseAuthentication.CreateDataSource("Host=db;Database=app;Username=svc;Password=plainzz1", credential);

        Assert.Equal(0, credential.Requests);
    }

    [Fact]
    public async Task ResolveForDirectUse_WithAPassword_ReturnsTheStringUntouched_AndNeverAsksForAToken()
    {
        var credential = new CountingCredential();
        const string connectionString = "Host=db;Database=app;Username=svc;Password=plainzz1";

        var resolved = await DatabaseAuthentication.ResolveForDirectUseAsync(connectionString, credential);

        Assert.Equal(connectionString, resolved);
        Assert.Equal(0, credential.Requests);
    }

    [Fact]
    public async Task ResolveForDirectUse_WithoutAPassword_PutsTheTokenInThePassword()
    {
        // The migrator hands DbUp a plain string, so the hosting identity's token has to travel
        // as the password. Everything else in the string is preserved.
        var credential = new CountingCredential();

        var resolved = await DatabaseAuthentication.ResolveForDirectUseAsync("Host=db;Database=app;Username=svc;SSL Mode=Require", credential);

        var builder = new NpgsqlConnectionStringBuilder(resolved);
        Assert.Equal("token", builder.Password);
        Assert.Equal("svc", builder.Username);
        Assert.Equal("db", builder.Host);
        Assert.Equal("app", builder.Database);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.Equal(1, credential.Requests);
        Assert.False(DatabaseAuthentication.UsesManagedIdentity(resolved));
    }

    [Fact]
    public async Task ResolveForDirectUse_WithoutAUser_TakesTheUserFromTheToken_AndRequiresTls()
    {
        // Aspire's Entra-mode flexible server string is "Host=…;Database=…" with no user: the
        // principal is whoever the token was issued to. A managed identity's token carries its
        // name only in xms_mirid.
        var credential = new CountingCredential(Jwt(new { xms_mirid = "/subscriptions/s/resourcegroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/api-identity-abc" }));

        var resolved = await DatabaseAuthentication.ResolveForDirectUseAsync("Host=db;Database=app", credential);

        var builder = new NpgsqlConnectionStringBuilder(resolved);
        Assert.Equal("api-identity-abc", builder.Username);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.Equal(1, credential.Requests);
    }

    [Theory]
    [InlineData("upn", "someone@example.com")]
    [InlineData("preferred_username", "someone@example.com")]
    public void UsernameFromToken_PrefersTheUserClaims(string claim, string expected)
    {
        var token = Jwt(new Dictionary<string, string> { [claim] = expected, ["xms_mirid"] = "/x/userAssignedIdentities/ignored" });

        Assert.Equal(expected, DatabaseAuthentication.UsernameFromToken(token));
    }

    [Fact]
    public void UsernameFromToken_WithNoPrincipalClaim_FailsLoudly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseAuthentication.UsernameFromToken(Jwt(new { aud = "db" })));

        Assert.Contains("Name the user in the connection string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDataSource_KeepsAnExplicitStricterSslMode()
    {
        using var dataSource = DatabaseAuthentication.CreateDataSource("Host=db;Database=app;Username=svc;SSL Mode=VerifyFull", new CountingCredential());

        Assert.Equal(SslMode.VerifyFull, new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).SslMode);
    }

    // An unsigned JWT with the given payload: only the claims matter here.
    private static string Jwt(object payload)
    {
        static string Part(object value) => Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part(new { alg = "none" })}.{Part(payload)}.";
    }

    private sealed class CountingCredential(string token = "token") : Azure.Core.TokenCredential
    {
        public int Requests { get; private set; }

        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Requests++;
            return new Azure.Core.AccessToken(token, DateTimeOffset.MaxValue);
        }

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
