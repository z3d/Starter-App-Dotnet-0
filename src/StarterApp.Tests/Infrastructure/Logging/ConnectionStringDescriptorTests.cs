using ConnectionStringDescriptor = StarterApp.ServiceDefaults.ConnectionStringDescriptor;

namespace StarterApp.Tests.Infrastructure.Logging;

public class ConnectionStringDescriptorTests
{
    // Synthetic values only; the point is that no part of the secret ever reaches the output.
    [Theory]
    [InlineData("Host=db;Port=5432;Database=app;Username=svc;Password='xq7;zk9;wv3'", "xq7;zk9;wv3")]
    [InlineData("Host=db;Database=app;Username=svc;Pwd=\"qj4;tm8;yb2\"", "qj4;tm8;yb2")]
    [InlineData("Host=db;Database=app;Username=svc;Password=plainzz1", "plainzz1")]
    public void Describe_NeverEchoesAnyPartOfThePassword(string connectionString, string secret)
    {
        var described = ConnectionStringDescriptor.Describe(connectionString);

        Assert.DoesNotContain(secret, described, StringComparison.Ordinal);
        foreach (var fragment in secret.Split(';'))
            Assert.DoesNotContain(fragment, described, StringComparison.Ordinal);
        Assert.Contains("Host=db", described, StringComparison.Ordinal);
        Assert.Contains("Database=app", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", described, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_WithMalformedInput_DoesNotEchoIt()
    {
        var described = ConnectionStringDescriptor.Describe("this is ;;; not=a=connection;Password=oops");

        Assert.DoesNotContain("oops", described, StringComparison.Ordinal);
        Assert.Equal("<unparseable connection string>", described);
    }

    [Fact]
    public void Describe_WithNothingConfigured_SaysSo()
    {
        Assert.Equal("<not configured>", ConnectionStringDescriptor.Describe(null));
        Assert.Equal("<not configured>", ConnectionStringDescriptor.Describe("  "));
    }
}
