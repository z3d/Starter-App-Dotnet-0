using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StarterApp.Api.Infrastructure.Assistant;

// Every route a person may call, read once from the API's own OpenAPI document and endpoint metadata, so a new route is the assistant's without a line here.
public sealed partial class AssistantCatalogue
{
    private static readonly string[] Paging = ["page", "pageSize"];

    private readonly IServiceProvider _services;
    private readonly EndpointDataSource _endpoints;
    private readonly Lazy<Task<IReadOnlyList<AssistantRoute>>> _routes;
    private readonly ConcurrentDictionary<string, string> _text = new(StringComparer.Ordinal);

    public AssistantCatalogue(IServiceProvider services, EndpointDataSource endpoints)
    {
        _services = services;
        _endpoints = endpoints;
        _routes = new Lazy<Task<IReadOnlyList<AssistantRoute>>>(() => BuildAsync(CancellationToken.None));
    }

    public Task<IReadOnlyList<AssistantRoute>> RoutesAsync() => _routes.Value;

    public async Task<IReadOnlyList<AssistantRoute>> RoutesForAsync(IReadOnlySet<string> scopes, CancellationToken cancellationToken) =>
        (await RoutesAsync().WaitAsync(cancellationToken)).Where(route => scopes.Contains(route.Scope)).ToList();

    public async Task<AssistantRoute?> FindAsync(IReadOnlySet<string> scopes, string? method, string? path, CancellationToken cancellationToken)
    {
        var routes = await RoutesForAsync(scopes, cancellationToken);
        return routes.FirstOrDefault(route => string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase) && route.Path == path?.TrimEnd('/'));
    }

    public async Task<string> TextAsync(IReadOnlySet<string> scopes, CancellationToken cancellationToken)
    {
        var key = string.Join(' ', scopes.Order(StringComparer.Ordinal));
        if (_text.TryGetValue(key, out var held))
            return held;

        var routes = await RoutesForAsync(scopes, cancellationToken);
        var text = new StringBuilder();
        text.AppendLine("The routes of this system that this person may call. A name in braces in a path is a path parameter. After \"?\" are a read's query parameters; in braces after a write's path are its body fields, a star marking the ones that must be given. In square brackets is the scope a write needs.");
        text.AppendLine();
        text.AppendLine("Reads, for the read tool:");
        foreach (var route in routes.Where(route => route.IsRead))
            text.AppendLine(CultureInfo.InvariantCulture, $"GET {route.Path}{(route.Fields.Length > 0 ? " ? " + route.Fields : "")} : {route.Summary}");
        text.AppendLine();
        text.AppendLine("Writes, for the propose tool:");
        foreach (var route in routes.Where(route => !route.IsRead))
            text.AppendLine(CultureInfo.InvariantCulture, $"{route.Method} {route.Path}{(route.Fields.Length > 0 ? " { " + route.Fields + " }" : "")} : {route.Summary} [{route.Scope}]");
        return _text.GetOrAdd(key, text.ToString());
    }

    private async Task<IReadOnlyList<AssistantRoute>> BuildAsync(CancellationToken cancellationToken)
    {
        var scopes = _endpoints.Endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Key: $"{method} {Normalise(endpoint.RoutePattern.RawText)}", endpoint.Metadata.GetMetadata<ScopeRequiredMetadata>()?.Scope)))
            .Where(entry => entry.Scope is not null)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Scope!, StringComparer.Ordinal);

        var document = await _services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync(cancellationToken);
        using var stream = new MemoryStream();
        await document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_1, cancellationToken);
        var root = JsonNode.Parse(stream.ToArray())!;
        var schemas = root["components"]?["schemas"]?.AsObject();

        var routes = new List<AssistantRoute>();
        foreach (var (path, operations) in root["paths"]!.AsObject())
        {
            foreach (var (verb, operation) in operations!.AsObject())
            {
                var method = verb.ToUpperInvariant();
                if (method is not ("GET" or "POST" or "PUT" or "DELETE") || operation is null)
                    continue;
                var template = path.TrimEnd('/');
                if (!scopes.TryGetValue($"{method} {template}", out var scope))
                    continue;
                if (template.StartsWith("/api/v1/assistant", StringComparison.Ordinal) || template.EndsWith("/content", StringComparison.Ordinal) || template.EndsWith("/export", StringComparison.Ordinal))
                    continue;
                var content = operation["requestBody"]?["content"]?.AsObject();
                if (content is not null && !content.ContainsKey("application/json"))
                    continue;

                var body = method == "GET" ? [] : BodyFields(template, content?["application/json"]?["schema"], schemas);
                var fields = method == "GET"
                    ? string.Join(", ", (operation["parameters"]?.AsArray() ?? [])
                        .Where(parameter => parameter?["in"]?.GetValue<string>() == "query")
                        .Select(parameter => parameter!["name"]!.GetValue<string>())
                        .Where(name => !Paging.Contains(name)))
                    : string.Join(", ", body.Select(field => $"{field.Name}{(field.Required ? "*" : string.Empty)}{(field.Kind is "text" or "choice" ? string.Empty : ":" + field.Kind)}"));
                routes.Add(new AssistantRoute(method, template, operation["summary"]?.GetValue<string>() ?? string.Empty, fields, scope, body));
            }
        }

        return routes.OrderBy(route => route.Path, StringComparer.Ordinal).ThenBy(route => route.Method, StringComparer.Ordinal).ToList();
    }

    private static List<AssistantField> BodyFields(string path, JsonNode? schema, JsonObject? schemas)
    {
        schema = Resolve(schema, schemas);
        if (schema?["properties"] is not JsonObject properties)
            return [];
        var required = (schema["required"]?.AsArray() ?? []).Select(name => name!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        return properties
            .Where(property => property.Key != "actorName")
            .Select(property =>
            {
                var field = Resolve(property.Value, schemas);
                var types = field?["type"] switch
                {
                    JsonArray several => several.Select(type => type!.GetValue<string>()).ToList(),
                    JsonValue one => [one.GetValue<string>()],
                    _ => [],
                };
                var options = AssistantVocabulary.OptionsFor(path, property.Key);
                var kind = options.Count > 0 ? "choice"
                    : field?["format"]?.GetValue<string>() is "uuid" or "date" or "date-time" ? field["format"]!.GetValue<string>()
                    : types.FirstOrDefault(type => type is "number" or "integer" or "boolean" or "array" or "object") ?? "text";
                var ask = AssistantVocabulary.AskFor(path, property.Key);
                var conditional = ask is not null && properties.ContainsKey(ask.Value.WhenField);
                return new AssistantField(property.Key, required.Contains(property.Key) && !types.Contains("null"), kind, options,
                    ask?.Label, conditional ? ask!.Value.WhenField : null, conditional ? ask!.Value.WhenValues : null);
            })
            .ToList();
    }

    private static JsonNode? Resolve(JsonNode? schema, JsonObject? schemas) =>
        schema?["$ref"]?.GetValue<string>() is { } reference && schemas is not null ? schemas[reference[(reference.LastIndexOf('/') + 1)..]] : schema;

    private static string Normalise(string? pattern) => Constraint().Replace(pattern ?? string.Empty, "}").TrimEnd('/');

    [GeneratedRegex(@":[^}]+\}")]
    private static partial Regex Constraint();
}
