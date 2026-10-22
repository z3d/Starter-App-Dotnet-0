using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StarterApp.Api.Infrastructure.Validation;

public sealed class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTimeOffset();
        return value.Offset == TimeSpan.Zero ? value : throw new NonUtcInstantException();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    public static Task DescribeAsDateTime(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTimeOffset) || (type == typeof(DateTimeOffset?) && context.ParameterDescription is not null))
            schema.Type = JsonSchemaType.String;
        else if (type == typeof(DateTimeOffset?))
            schema.Type = JsonSchemaType.Null | JsonSchemaType.String;
        return Task.CompletedTask;
    }
}

public sealed class NonUtcInstantException() : JsonException("An instant must be sent in UTC.")
{
    public static ValidationError ErrorFor(string field) =>
        new(field, $"{field} must be sent in UTC, with a Z or +00:00 offset.");
}
