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
            .Take(128)
            .ToArray();

        return chars.Length == 0 ? Create() : new string(chars);
    }

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
