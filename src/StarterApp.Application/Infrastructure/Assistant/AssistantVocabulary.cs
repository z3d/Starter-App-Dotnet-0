namespace StarterApp.Application.Infrastructure.Assistant;

// AssistantPromptTests holds these lists to the enums the API parses.
public static class AssistantVocabulary
{
    public static readonly IReadOnlyList<(string Field, string? PathHas, string[] Values)> Fields =
    [
        ("status", "/orders", ["Pending", "Confirmed", "Processing", "Shipped", "Delivered", "Cancelled"]),
    ];

    public static readonly IReadOnlyList<(string Field, string PathHas, string Label, string WhenField, string[] WhenValues)> Asks = [];

    public static (string Label, string WhenField, string[] WhenValues)? AskFor(string path, string field)
    {
        foreach (var (name, pathHas, label, whenField, whenValues) in Asks)
        {
            if (name == field && path.Contains(pathHas, StringComparison.Ordinal))
                return (label, whenField, whenValues);
        }

        return null;
    }

    public static IReadOnlyList<string> OptionsFor(string path, string field)
    {
        foreach (var (name, pathHas, values) in Fields)
        {
            if (name == field && (pathHas is null || path.Contains(pathHas, StringComparison.Ordinal)))
                return values;
        }

        return [];
    }
}
