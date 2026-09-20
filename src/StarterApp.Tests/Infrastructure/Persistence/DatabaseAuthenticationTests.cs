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
    [InlineData("Host=db;Database=app", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("this is ;;; not=a=connection", false)]
    public void UsesManagedIdentity_OnlyForAPasswordlessStringThatNamesAUser(string? connectionString, bool expected)
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

    private sealed class CountingCredential : Azure.Core.TokenCredential
    {
        public int Requests { get; private set; }

        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Requests++;
            return new Azure.Core.AccessToken("token", DateTimeOffset.MaxValue);
        }

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
