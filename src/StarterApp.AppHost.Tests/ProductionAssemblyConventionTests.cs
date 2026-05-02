using StarterApp.Functions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StarterApp.AppHost.Tests;

public class ProductionAssemblyConventionTests
{
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(ServiceBusTopology).Assembly,
        typeof(OrderConfirmationEmailFunction).Assembly,
        typeof(Microsoft.Extensions.Hosting.Extensions).Assembly
    ];

    [Fact]
    public void ProductionAsyncMethods_MustHaveAsyncSuffix()
    {
        var failures = GetProductionTypes()
            .SelectMany(type => GetDeclaredMethods(type)
                .Where(IsAsyncMethod)
                .Where(method => !method.Name.EndsWith("Async", StringComparison.Ordinal))
                .Select(method => $"{type.FullName}.{method.Name} returns/uses async but does not end with Async."))
            .ToList();

        Assert.True(failures.Count == 0,
            "Async production methods must use the Async suffix:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ProductionMethods_MustNotBeAsyncVoid()
    {
        var failures = GetProductionTypes()
            .SelectMany(type => GetDeclaredMethods(type)
                .Where(method => method.ReturnType == typeof(void))
                .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
                .Select(method => $"{type.FullName}.{method.Name} is async void. Return Task instead."))
            .ToList();

        Assert.True(failures.Count == 0,
            "Production methods must not be async void:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ProductionTypes_MustNotResolveCurrentTimeViaDateTime()
    {
        var failures = GetProductionTypes()
            .SelectMany(type => GetDeclaredMethods(type)
                .Where(CallsDateTimeCurrentTime)
                .Select(method => $"{type.FullName}.{method.Name} calls DateTime.Now, DateTime.UtcNow, or DateTime.Today. Use DateTimeOffset or an injected clock."))
            .ToList();

        Assert.True(failures.Count == 0,
            "Production code must not resolve current time via DateTime:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<Type> GetProductionTypes()
    {
        return ProductionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && !IsCompilerGenerated(type));
    }

    private static IEnumerable<MethodInfo> GetDeclaredMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));
    }

    private static bool IsAsyncMethod(MethodInfo method)
    {
        return method.GetCustomAttribute<AsyncStateMachineAttribute>() != null ||
               typeof(Task).IsAssignableFrom(method.ReturnType) ||
               (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)) ||
               method.ReturnType == typeof(ValueTask);
    }

    private static bool CallsDateTimeCurrentTime(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null)
            return false;

        var il = body.GetILAsByteArray();
        if (il == null)
            return false;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var member = method.Module.ResolveMember(token);
                if (member?.DeclaringType == typeof(DateTime) &&
                    member.Name is "get_Now" or "get_UtcNow" or "get_Today")
                    return true;
            }
            catch
            {
                // Some generic instantiations cannot be resolved from raw IL tokens. They are not DateTime calls.
            }

            i += 4;
        }

        return false;
    }

    private static bool IsCompilerGenerated(Type type)
    {
        return type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Any() ||
               type.Name.Contains('<') ||
               type.Name.Contains('>') ||
               type.Name.StartsWith("<>", StringComparison.Ordinal) ||
               type.Name.Contains("d__", StringComparison.Ordinal) ||
               type.Name.Contains("c__DisplayClass", StringComparison.Ordinal) ||
               type.Name.Contains("__StaticArrayInitTypeSize", StringComparison.Ordinal) ||
               type.IsNested;
    }
}
