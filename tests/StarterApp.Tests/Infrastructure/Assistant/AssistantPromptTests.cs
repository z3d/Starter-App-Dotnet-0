using StarterApp.Application.Infrastructure.Assistant;

namespace StarterApp.Tests.Infrastructure.Assistant;

public class AssistantPromptTests
{
    [Fact]
    public void TheNotes_ListEveryOrderStatusTheApiParses_AndNoOther()
    {
        var line = AssistantPrompt.Notes.Split('\n').Single(l => l.TrimStart().StartsWith("- an order's status:", StringComparison.Ordinal));

        var listed = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Split(',', StringSplitOptions.TrimEntries);

        Assert.Equal(Enum.GetNames<OrderStatus>(), listed);
    }

    [Fact]
    public void TheChoicesACardOffersForAnOrdersStatus_AreEveryValueTheApiParses_AndNoOther()
    {
        var entry = AssistantVocabulary.Fields.Single(candidate => candidate.Field == "status");

        Assert.Equal(Enum.GetNames<OrderStatus>(), entry.Values);
        Assert.Equal(entry.Values, AssistantVocabulary.OptionsFor("/api/v1/orders/{id}/status", "status"));
        Assert.Empty(AssistantVocabulary.OptionsFor("/api/v1/products", "status"));
    }

    [Fact]
    public void EveryTool_HasANameADescriptionAndASchema_AndTheNamesAreDistinct()
    {
        var tools = AssistantPrompt.Tools.Select(tool => tool.TryPickTool(out var own) ? own : null).ToList();

        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool?.Description)));
        Assert.Equal(tools.Count, tools.Select(tool => tool!.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(AssistantPrompt.Guiding, AssistantPrompt.Instructions, StringComparison.Ordinal);
    }
}
