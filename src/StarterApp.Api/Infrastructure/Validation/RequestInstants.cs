using System.Collections;
using System.Collections.Concurrent;

namespace StarterApp.Api.Infrastructure.Validation;

public static class RequestInstants
{
    private const int MaxDepth = 4;
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertiesThatMayHoldAnInstant = new();

    public static IReadOnlyList<ValidationError> NotInUtc(object request)
    {
        List<ValidationError>? errors = null;
        Walk(request, "", 0, ref errors);
        return (IReadOnlyList<ValidationError>?)errors ?? [];
    }

    private static void Walk(object? value, string path, int depth, ref List<ValidationError>? errors)
    {
        switch (value)
        {
            case null:
                return;
            case DateTimeOffset instant:
                if (instant.Offset != TimeSpan.Zero)
                    (errors ??= []).Add(new ValidationError(path, $"{path} must be sent in UTC, with a Z or +00:00 offset."));
                return;
            case IEnumerable items when depth > 0 && depth < MaxDepth:
                var index = 0;
                foreach (var item in items)
                    Walk(item, $"{path}[{index++}]", depth + 1, ref errors);
                return;
        }

        if (depth >= MaxDepth)
            return;

        foreach (var property in PropertiesThatMayHoldAnInstant.GetOrAdd(value.GetType(), Find))
            Walk(property.GetValue(value), path.Length == 0 ? property.Name : $"{path}.{property.Name}", depth + 1, ref errors);
    }

    private static PropertyInfo[] Find(Type type)
    {
        var root = RootNamespace(type);
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => MayHoldAnInstant(property.PropertyType, root) || MayHoldAnInstant(ElementType(property.PropertyType), root))
            .ToArray();
    }

    private static bool MayHoldAnInstant(Type? type, string? root)
    {
        if (type is null)
            return false;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(DateTimeOffset)
            || (!underlying.IsEnum && !underlying.IsPrimitive && root is not null && RootNamespace(underlying) == root);
    }

    private static Type? ElementType(Type type) =>
        type == typeof(string) ? null : type.GetInterfaces().Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    private static string? RootNamespace(Type type) => type.Namespace?.Split('.')[0];
}
