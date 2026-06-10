using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class CorrelationContextTests
{
    [Fact]
    public void Sanitize_WithContractValidId_ShouldReturnItUnchanged()
    {
        Assert.Equal("support-case-123", CorrelationContext.Sanitize("support-case-123"));
    }

    [Fact]
    public void Sanitize_WithStrippedCharacters_ShouldKeepDistinctRawIdsDistinct()
    {
        // Lossy sanitization used to collapse "a:b" and "a|b" onto "ab", co-mingling unrelated
        // requests in one archive stream; the raw-bound hash suffix keeps them apart.
        var first = CorrelationContext.Sanitize("a:b");
        var second = CorrelationContext.Sanitize("a|b");

        Assert.StartsWith("ab.", first, StringComparison.Ordinal);
        Assert.StartsWith("ab.", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sanitize_IsDeterministicForTheSameRawId()
    {
        Assert.Equal(CorrelationContext.Sanitize("trace:abc"), CorrelationContext.Sanitize("trace:abc"));
    }

    [Fact]
    public void Sanitize_WithOverlongIds_ShouldStayWithinContractLengthAndStayDistinct()
    {
        var longBase = new string('a', 200);
        var first = CorrelationContext.Sanitize(longBase + "1");
        var second = CorrelationContext.Sanitize(longBase + "2");

        Assert.True(first.Length <= 128);
        Assert.True(second.Length <= 128);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sanitize_Output_ShouldAlwaysMatchTheCorrelationIdContractCharset()
    {
        foreach (var raw in new[] { "trace:abc", "a|b", new string('x', 300), "  spaced value  " })
            Assert.Matches("^[A-Za-z0-9._-]{1,128}$", CorrelationContext.Sanitize(raw));
    }
}
