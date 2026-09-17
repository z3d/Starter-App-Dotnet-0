using StarterApp.Api.Infrastructure.Persistence;

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
