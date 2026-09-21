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
        // The non-ASCII cases are the regression guard: char.IsLetterOrDigit is Unicode-wide, so
        // an all-non-ASCII-letters id once passed through verbatim (length-equality early return,
        // no hash suffix) into blob names and the echoed X-Correlation-ID header.
        foreach (var raw in new[]
                 {
                     "trace:abc", "a|b", new string('x', 300), "  spaced value  ",
                     "città", "трейс-123", "标识符", "🔥trace🔥"
                 })
            Assert.Matches("^[A-Za-z0-9._-]{1,128}$", CorrelationContext.Sanitize(raw));
    }

    [Fact]
    public void Sanitize_WithNonAsciiLetters_ShouldStripThemAndKeepDistinctRawIdsDistinct()
    {
        var first = CorrelationContext.Sanitize("cliché-a");
        var second = CorrelationContext.Sanitize("cliche-a");

        Assert.Matches("^[A-Za-z0-9._-]{1,128}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sanitize_WhenEveryCharacterIsStripped_ShouldStayStableAndRawBound()
    {
        // A caller that sends the same all-stripped id on every request must land in one archive
        // stream, not a fresh random one per request; distinct all-stripped ids must stay apart.
        var first = CorrelationContext.Sanitize("标识符");
        var second = CorrelationContext.Sanitize("标识符");
        var other = CorrelationContext.Sanitize("Ωμέγα");

        Assert.Equal(first, second);
        Assert.StartsWith("invalid.", first, StringComparison.Ordinal);
        Assert.Matches("^[A-Za-z0-9._-]{1,128}$", first);
        Assert.NotEqual(first, other);
    }
}
