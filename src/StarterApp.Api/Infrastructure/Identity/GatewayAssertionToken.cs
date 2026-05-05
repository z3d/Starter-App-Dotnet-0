using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Identity;

internal static class GatewayAssertionToken
{
    private const string Version = "v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(GatewayAssertionPayload payload, string signingKey)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadSegment = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{Version}.{payloadSegment}";
        var signature = Sign(signingInput, signingKey);
        return $"{signingInput}.{signature}";
    }

    public static bool TryRead(string token, out string signingInput, out string payloadSegment, out string signatureSegment)
    {
        signingInput = string.Empty;
        payloadSegment = string.Empty;
        signatureSegment = string.Empty;

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Version)
            return false;

        payloadSegment = parts[1];
        signatureSegment = parts[2];
        signingInput = $"{parts[0]}.{payloadSegment}";
        return true;
    }

    public static GatewayAssertionPayload? ReadPayload(string payloadSegment)
    {
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadSegment));
            return JsonSerializer.Deserialize<GatewayAssertionPayload>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool VerifySignature(string signingInput, string signatureSegment, string signingKey)
    {
        var expected = Sign(signingInput, signingKey);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(signatureSegment);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding > 0)
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');

        return Convert.FromBase64String(base64);
    }

    private static string Sign(string signingInput, string signingKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)));
    }
}
