namespace StarterApp.Api.Modules.Assistant.Application;

// One field of a write's body, as the product asks a person for it: the same field, kind and choices every time.
// Label is what a person is asked where the field's own name would not tell them.
// WhenField and WhenValues say the field applies only once another field holds one of those values; it is then required.
public sealed record AssistantField(string Name, bool Required, string Kind, IReadOnlyList<string> Options, string? Label = null, string? WhenField = null, IReadOnlyList<string>? WhenValues = null);
