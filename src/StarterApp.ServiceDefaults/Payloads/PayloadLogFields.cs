namespace StarterApp.ServiceDefaults.Payloads;

// The checked-in decision, field by field, on whether a payload value may reach a log line. PayloadLogFieldConventionTests fails on a field the code sends that it does not list.
public static class PayloadLogFields
{
    public const string ResourceName = "StarterApp.ServiceDefaults.Payloads.payload-log-fields.txt";

    private static readonly Lazy<(string[] Kept, string[] Masked)> Embedded = new(LoadEmbedded);

    public static string[] Kept => Embedded.Value.Kept;

    public static string[] Masked => Embedded.Value.Masked;

    public static (string[] Kept, string[] Masked) Parse(string text)
    {
        var kept = new List<string>();
        var masked = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[1].Contains(' ', StringComparison.Ordinal))
                throw new FormatException($"'{line}' in {ResourceName} is not 'keep <field>' or 'mask <field>'.");

            switch (parts[0])
            {
                case "keep":
                    kept.Add(parts[1]);
                    break;
                case "mask":
                    masked.Add(parts[1]);
                    break;
                default:
                    throw new FormatException($"'{line}' in {ResourceName} is not 'keep <field>' or 'mask <field>'.");
            }
        }

        return ([.. kept], [.. masked]);
    }

    private static (string[] Kept, string[] Masked) LoadEmbedded()
    {
        using var stream = typeof(PayloadLogFields).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in {typeof(PayloadLogFields).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
