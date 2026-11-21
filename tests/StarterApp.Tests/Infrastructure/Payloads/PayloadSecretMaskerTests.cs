using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadSecretMaskerTests
{
    [Theory]
    [InlineData("""{"a":{"b":[{"clientSecret":"S3CRET-VALUE"}]}}""")]
    [InlineData("""{"refresh_token":"S3CRET-VALUE"}""")]
    [InlineData("""{"oneTimeCode":"S3CRET-VALUE"}""")]
    [InlineData("""{"pin":"S3CRET-VALUE"}""")]
    [InlineData("""{"cvv":"S3CRET-VALUE"}""")]
    [InlineData("""{"redacted":true,"alreadyMasked":"yes","password":"S3CRET-VALUE"}""")]
    [InlineData("""{"body":"{\"password\":\"S3CRET-VALUE\"}"}""")]
    [InlineData("""["{\"apiKey\":\"S3CRET-VALUE\"}"]""")]
    [InlineData("""{"password":"S3CRET-VALUE","note":"cut sh""")]
    [InlineData("grant_type=password&password=S3CRET-VALUE&username=ada")]
    public void MaskPayload_MasksSecretsAtAnyDepthAndInEveryShape(string payload)
    {
        var masked = PayloadSecretMasker.MaskPayload(payload);

        Assert.DoesNotContain("S3CRET-VALUE", masked, StringComparison.Ordinal);
        Assert.Contains(PayloadSecretMasker.Mask, masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("paid with 4111 1111 1111 1111 today")]
    [InlineData("paid with 4111-1111-1111-1111 today")]
    [InlineData("""{"note":"card 5555555555554444 on file"}""")]
    [InlineData("amex 3782 822463 10005")]
    public void MaskPayload_MasksLuhnValidCardNumbersInText(string payload)
    {
        var masked = PayloadSecretMasker.MaskPayload(payload);

        Assert.Contains(PayloadSecretMasker.Mask, masked, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}[ -]?\d{4}[ -]?\d{4}", masked);
    }

    [Theory]
    [InlineData("""{"orderId":"00000000-0000-0000-0000-000000000001","shipping":"express","total":42}""")]
    [InlineData("""{ "note" : "reference 4111111111111112 is not a card",   "count": 3 }""")]
    [InlineData("order 1234567890 shipped")]
    public void MaskPayload_LeavesABodyWithNoSecretByteForByte(string payload)
    {
        Assert.Same(payload, PayloadSecretMasker.MaskPayload(payload));
    }

    [Fact]
    public void LogRedactor_MasksAFieldNobodyListed_EvenWhenItIsNotSensitive()
    {
        var redactor = new JsonPayloadRedactor(Options.Create(new PayloadCaptureOptions()));

        var redacted = redactor.Redact("""{"favouriteColour":"teal","orderId":"o-1"}""", "application/json");

        Assert.DoesNotContain("teal", redacted, StringComparison.Ordinal);
        Assert.Contains("\"orderId\":\"o-1\"", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureAsync_ArchivesTheBodyWithItsSecretsMasked_AndTheRestInFull()
    {
        var store = new InMemoryPayloadArchiveStore();
        var options = Options.Create(new PayloadCaptureOptions());
        var sink = new PayloadCaptureSink(store, new JsonPayloadRedactor(options), TimeProvider.System, options,
            NullLogger<PayloadCaptureSink>.Instance);

        var record = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada","email":"ada@example.com","password":"S3CRET-VALUE"}"""
        }, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("""{"name":"Ada","email":"ada@example.com","password":"***SECRET***"}""", record.Payload);
        Assert.DoesNotContain(store.Lines.Values.SelectMany(lines => lines), line => line.Contains("S3CRET-VALUE", StringComparison.Ordinal));
    }
}
