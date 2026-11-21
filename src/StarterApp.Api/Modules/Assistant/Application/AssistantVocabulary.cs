namespace StarterApp.Api.Modules.Assistant.Application;

// The fields a person picks a value for, and the values: a card offers them as a choice, the same ones every time.
// The lists are held to the enums the API parses by AssistantVocabularyTests; a field name that means different things
// on different routes names its path.
public static class AssistantVocabulary
{
    public static readonly IReadOnlyList<(string Field, string? PathHas, string[] Values)> Fields = [];

    // Fields a person is asked for under a plainer name, and fields that apply only for some values of another field
    // of the same body.
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
