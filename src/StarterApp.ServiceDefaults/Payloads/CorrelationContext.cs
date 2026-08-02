using System.Security.Cryptography;
using System.Text;

namespace StarterApp.ServiceDefaults.Payloads;

public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ApplicationPropertyName = "CorrelationId";

    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string? Current => CurrentValue.Value;

    public static IDisposable Push(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var previous = CurrentValue.Value;
        CurrentValue.Value = Sanitize(correlationId);
        return new Scope(previous);
    }

    public static string GetOrCreate()
    {
        if (!string.IsNullOrWhiteSpace(CurrentValue.Value))
            return CurrentValue.Value;

        CurrentValue.Value = Create();
        return CurrentValue.Value;
    }

    public static string Create()
    {
        return Guid.CreateVersion7().ToString("N");
    }

    public static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return Create();

        // The output contract is ASCII [A-Za-z0-9._-]{1,128} — it feeds archive blob names and
        // the echoed X-Correlation-ID response header, so only this exact set may pass.
        var chars = trimmed
            .Where(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.')
            .Take(MaxSanitizedLength)
            .ToArray();

        var sanitized = new string(chars);
        if (sanitized.Length == trimmed.Length)
            return sanitized;

        // Lossy sanitization (stripped characters or truncation) can collapse distinct raw ids
        // onto one sanitized id, co-mingling unrelated requests in the same archive stream. Bind
        // the sanitized form to the raw input with a short hash so distinct raws stay distinct —
        // including when nothing survives the filter: a stable raw-bound "invalid.<hash>" keeps
        // one caller-supplied id in one archive stream, where a random fallback would split it
        // across requests.
        var rawHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))[..HashSuffixLength].ToLowerInvariant();
        if (sanitized.Length == 0)
            return $"invalid.{rawHash}";

        var maxBaseLength = MaxSanitizedLength - HashSuffixLength - 1;
        var truncatedBase = sanitized.Length > maxBaseLength ? sanitized[..maxBaseLength] : sanitized;
        return $"{truncatedBase}.{rawHash}";
    }

    private const int MaxSanitizedLength = 128;
    private const int HashSuffixLength = 8;

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentValue.Value = _previous;
            _disposed = true;
        }
    }
}
