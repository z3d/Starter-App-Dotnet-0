using System.Text.Json;
using Anthropic.Models.Messages;

namespace StarterApp.Api.Modules.Assistant.Application;

// The prompt and the tools are reviewed like code (AGENTS.md, "A specified mechanism is not swapped for a cheaper one").
public static class AssistantPrompt
{
    public const string Read = "read";
    public const string Propose = "propose";
    public const string ShowTable = "show_table";
    public const string ShowGraphic = "show_graphic";
    public const string AskPerson = "ask_person";

    // Added to every area's prompt: the person is led, and is not expected to know the system's words or what it needs.
    public const string Guiding = """
        Guide the person. Do not expect them to know this system's words, what it can do or what an action needs. People say what happened, not the name of an action. Work out what fits from what they said and from the records.

        The product asks for a change's details itself. As soon as you know which change the person means and which records it is about, propose it with whatever values you have: the card it shows is a form with every field of that change, in the same order every time, filled in with what you gave and waiting for the rest. So never ask the person for a field of a change, in a sentence or with ask_person; propose and let the card ask. A path's ids are the exception: find the record first, and if you cannot tell which record is meant, ask.

        Use the ask_person tool only for what the product cannot work out: which of several records is meant, or which of several ways to record something when what they describe could be recorded in more than one way. Offer those as one choice, say in a few words what each means, and put the one you would pick first.

        After the person confirms a change the product itself offers what usually comes next; you are not asked to.
        """;

    public static IReadOnlyList<ToolUnion> Tools { get; } =
    [
        new Tool
        {
            Name = Read,
            Description = "Call one of the GET routes listed in the system prompt, as the person, and get back what it returned (lists are cut to twenty records). Use it to look up records, their ids and what already exists before proposing anything.",
            InputSchema = Schema(
                new()
                {
                    ["path"] = Property("string", "The route's path exactly as listed, with its braces, for example /api/v1/cases/{id}."),
                    ["pathParams"] = Property("object", "A value for each name in braces in the path."),
                    ["query"] = Property("object", "Query parameters from the route's list, when they narrow the read."),
                },
                "path"),
        },
        new Tool
        {
            Name = Propose,
            Description = "Propose one request to a POST, PUT or DELETE route listed in the system prompt. It shows the person a card with the route, what it does and every value you give; nothing is sent unless they confirm, and then it is sent as them. Call it once you have every starred field, on its own.",
            InputSchema = Schema(
                new()
                {
                    ["method"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = new[] { "POST", "PUT", "DELETE" }, description = "The route's method." }),
                    ["path"] = Property("string", "The route's path exactly as listed, with its braces."),
                    ["pathParams"] = Property("object", "A value for each name in braces in the path."),
                    ["body"] = Property("object", "The body fields to send, by the names in the route's list."),
                    ["title"] = Property("string", "A few plain words for the card saying what will happen, for example \"Register Priya Patel as a customer\"."),
                },
                "method", "path", "title"),
        },
        new Tool
        {
            Name = ShowTable,
            Description = "Show the person a table on their screen: use it whenever they ask to see, list or compare several records, after reading them. One record a row, and only the few columns that answer what they asked. It changes nothing. Call it on its own, with any words of yours kept to a sentence.",
            InputSchema = Schema(
                new()
                {
                    ["title"] = Property("string", "A few words saying what the table shows."),
                    ["columns"] = JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "string" }, description = "The column headings, at most eight." }),
                    ["rows"] = JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "array", items = new { type = "string" } }, description = "The rows, each with one value for each column, as text: money with a dollar sign, days as 1 Dec 2026. At most fifty." }),
                    ["note"] = Property("string", "One sentence under the table when something needs saying, such as that the list was cut short. Leave out otherwise."),
                },
                "title", "columns", "rows"),
        },
        new Tool
        {
            Name = ShowGraphic,
            Description = """
                Draw the person a graph or diagram on their screen as SVG, after reading the records it is drawn from: use it when they ask for a graph, a chart, a trend, a comparison at a glance or a picture of how something splits. It changes nothing. Call it on its own.

                Draw with this palette and nothing else, so it reads in light and dark: marks in var(--chart-1) (blue), var(--chart-2) (orange) and var(--chart-3) (green), in that order, one colour for one series; a single series or the one thing that matters in var(--accent); text in var(--ink), lesser text in var(--ink-secondary), gridlines and axes in var(--line) at 1px; no other colours, no gradients, no backgrounds. Text never takes a mark's colour.

                Keep it plain: a viewBox about 640 wide, text 12 to 14px, bars at most 24px thick with 2px between them, lines 2px, a value at the end of each bar or line, a name beside each series, at most a few gridlines. Work the scale out before you draw: pick a round maximum, and place every mark at its value over that maximum, from one baseline at zero. Only shapes and text: no scripts, links, images, styles or foreign content; anything else is removed.

                Always give the figures the graph is drawn from in data, so the person can check them as a table.
                """,
            InputSchema = Schema(
                new()
                {
                    ["title"] = Property("string", "A few words saying what the graph shows."),
                    ["svg"] = Property("string", "The whole drawing, from <svg viewBox=...> to </svg>."),
                    ["data"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        description = "The figures the graph is drawn from.",
                        properties = new
                        {
                            columns = new { type = "array", items = new { type = "string" } },
                            rows = new { type = "array", items = new { type = "array", items = new { type = "string" } } },
                        },
                        required = new[] { "columns", "rows" },
                    }),
                    ["note"] = Property("string", "One sentence under the graph when something needs saying. Leave out otherwise."),
                },
                "title", "svg", "data"),
        },
        new Tool
        {
            Name = AskPerson,
            Description = "Ask the person for what you need as a short form on their screen, instead of a question in a sentence. Each field is one thing to pick or fill in. Use kind choice with options whenever the answer is one of a known set (two to eight options, real records by name with the id as the value); date for a day; amount for money; yesno for a yes or no; text only when nothing else fits. Their answers come back as a JSON object keyed by each field's name. Call it on its own, never with another tool.",
            InputSchema = Schema(
                new()
                {
                    ["question"] = Property("string", "One short sentence above the form saying what you need and why, in the person's words, not the system's."),
                    ["fields"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        description = "One to five fields.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string", description = "The key the answer comes back under." },
                                label = new { type = "string", description = "What the person sees beside the field, a few plain words." },
                                kind = new { type = "string", @enum = new[] { "choice", "date", "amount", "yesno", "text" } },
                                options = new
                                {
                                    type = "array",
                                    description = "For kind choice: the options, the likeliest first.",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            value = new { type = "string", description = "What comes back when it is picked: a record's id, or a short word." },
                                            label = new { type = "string", description = "What the person sees." },
                                            hint = new { type = "string", description = "A few words on what picking it means, when that is not obvious. Leave out otherwise." },
                                        },
                                        required = new[] { "value", "label" },
                                    },
                                },
                                optional = new { type = "boolean", description = "True when the person may leave it empty." },
                            },
                            required = new[] { "name", "label", "kind" },
                        },
                    }),
                },
                "question", "fields"),
        },
    ];

    public static string Today(DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"Today is {today:dddd d MMMM yyyy} ({today:yyyy-MM-dd}) where the company is.");

    private static JsonElement Property(string type, string description) => JsonSerializer.SerializeToElement(new { type, description });

    private static InputSchema Schema(Dictionary<string, JsonElement> properties, params string[] required) =>
        new() { Properties = properties, Required = required };
}
