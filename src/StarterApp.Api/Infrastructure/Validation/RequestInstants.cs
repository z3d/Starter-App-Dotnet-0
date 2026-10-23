using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace StarterApp.Api.Infrastructure.Validation;

public static class RequestInstants
{
    private const int MaxDepth = 8;
    private static readonly ConcurrentDictionary<(Type Type, string? Root), MemberInfo[]> MembersThatMayHoldAnInstant = new();

    public static IReadOnlyList<ValidationError> NotInUtc(object request)
    {
        List<ValidationError>? errors = null;
        Walk(request, "", 0, RootNamespace(request.GetType()), ref errors);
        return (IReadOnlyList<ValidationError>?)errors ?? [];
    }

    private static void Walk(object? value, string path, int depth, string? root, ref List<ValidationError>? errors)
    {
        switch (value)
        {
            case null or string:
                return;
            case DateTimeOffset instant:
                if (instant.Offset != TimeSpan.Zero)
                    (errors ??= []).Add(new ValidationError(path, $"{path} must be sent in UTC, with a Z or +00:00 offset."));
                return;
            case IEnumerable items when depth > 0 && depth < MaxDepth:
                var index = 0;
                foreach (var item in items)
                    Walk(item, $"{path}[{index++}]", depth + 1, root, ref errors);
                return;
        }

        if (depth >= MaxDepth)
            return;

        foreach (var member in MembersThatMayHoldAnInstant.GetOrAdd((value.GetType(), root), key => Find(key.Type, key.Root)))
            Walk(Read(member, value), path.Length == 0 ? member.Name : $"{path}.{member.Name}", depth + 1, root, ref errors);
    }

    private static object? Read(MemberInfo member, object value) =>
        member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value);

    private static MemberInfo[] Find(Type type, string? root)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => MayHoldAnInstant(property.PropertyType, root))
            .Cast<MemberInfo>();
        var tupleFields = type.IsDefined(typeof(TupleElementNamesAttribute), false) || (type.IsGenericType && type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
            ? type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => MayHoldAnInstant(field.FieldType, root)).Cast<MemberInfo>()
            : [];
        return properties.Concat(tupleFields).ToArray();
    }

    private static bool MayHoldAnInstant(Type type, string? root)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(DateTimeOffset))
            return true;
        if (underlying.IsEnum || underlying.IsPrimitive || underlying == typeof(string) || underlying == typeof(decimal))
            return false;
        if (underlying.IsGenericType && underlying.GetGenericArguments().Any(argument => MayHoldAnInstant(argument, root)))
            return true;
        if (underlying.IsArray)
            return MayHoldAnInstant(underlying.GetElementType()!, root);
        return root is not null && RootNamespace(underlying) == root;
    }

    private static string? RootNamespace(Type type) => type.Namespace?.Split('.')[0];
}
