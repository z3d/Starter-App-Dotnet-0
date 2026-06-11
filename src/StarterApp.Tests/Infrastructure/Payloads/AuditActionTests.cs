namespace StarterApp.Tests.Infrastructure.Payloads;

public class AuditActionTests
{
    [Theory]
    [InlineData("GET", "Read")]
    [InlineData("HEAD", "Read")]
    [InlineData("POST", "Create")]
    [InlineData("PUT", "Update")]
    [InlineData("PATCH", "Update")]
    [InlineData("DELETE", "Delete")]
    [InlineData("OPTIONS", "Other")]
    public void FromMethod_MapsVerbsToBusinessActions(string method, string expected)
    {
        Assert.Equal(expected, AuditAction.FromMethod(method));
    }

    [Fact]
    public void Resolve_WithoutEndpointOverride_FallsBackToVerb()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;

        Assert.Equal(AuditAction.Update, AuditAction.Resolve(context));
    }

    [Fact]
    public void Resolve_WithEndpointOverride_UsesTheOverride()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(new AuditActionMetadata(AuditAction.StatusChange)),
            "test-endpoint"));

        Assert.Equal(AuditAction.StatusChange, AuditAction.Resolve(context));
    }
}
