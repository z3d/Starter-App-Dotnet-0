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

        var chars = trimmed
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            .Take(MaxSanitizedLength)
            .ToArray();

        if (chars.Length == 0)
            return Create();

        var sanitized = new string(chars);
        if (sanitized.Length == trimmed.Length)
            return sanitized;

        // Lossy sanitization (stripped characters or truncation) can collapse distinct raw ids
        // onto one sanitized id, co-mingling unrelated requests in the same archive stream. Bind
        // the sanitized form to the raw input with a short hash so distinct raws stay distinct.
        var rawHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))[..HashSuffixLength].ToLowerInvariant();
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
