using System.Reflection;
using StarterApp.Functions;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.AppHost.Tests;

public class PayloadCaptureConventionTests
{
    // Every inbound Service Bus message must be archived for the PII audit trail. Injecting the sink is
    // not enough — a Function that triggers on a subscription but never calls IPayloadCaptureSink.CaptureAsync
    // would consume events with no support artifact. This scans Function IL (including async state machines).
    [Fact]
    public void ServiceBusTriggeredFunctions_MustCaptureInboundPayload()
    {
        var functions = GetServiceBusTriggeredFunctionTypes().ToList();
        Assert.NotEmpty(functions);

        var violations = functions
            .Where(type => !TypeInvokesInterface(type, nameof(IPayloadCaptureSink)))
            .Select(type => $"{type.FullName} has a [ServiceBusTrigger] but never calls IPayloadCaptureSink.CaptureAsync; inbound payloads must be archived for the PII audit trail.")
            .ToList();

        Assert.True(violations.Count == 0,
            "Every Service Bus-triggered Function must capture its inbound payload:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<Type> GetServiceBusTriggeredFunctionTypes()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        return typeof(OrderConfirmationEmailFunction).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetMethods(flags)
                .SelectMany(method => method.GetParameters())
                .SelectMany(parameter => parameter.GetCustomAttributes())
                .Any(attribute => attribute.GetType().Name == "ServiceBusTriggerAttribute"));
    }

    private static bool TypeInvokesInterface(Type type, string interfaceTypeName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // The Function method is async, so the CaptureAsync call lives in a compiler-generated nested
        // state machine type rather than the Run method's own body.
        var methods = type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic).SelectMany(nested => nested.GetMethods(flags)));

        return methods.Any(method => IlReferencesType(method, interfaceTypeName));
    }

    private static bool IlReferencesType(MethodInfo method, string typeName)
    {
        var body = method.GetMethodBody();
        if (body == null)
            return false;

        var il = body.GetILAsByteArray();
        if (il == null)
            return false;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                if (module.ResolveMember(token)?.DeclaringType?.Name == typeName)
                    return true;
            }
            catch
            {
                // Unresolvable generic instantiation — not the member we are looking for.
            }

            i += 4;
        }

        return false;
    }
}
