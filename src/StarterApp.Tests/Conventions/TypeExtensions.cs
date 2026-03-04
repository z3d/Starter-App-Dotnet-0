using System.Reflection;

namespace StarterApp.Tests.Conventions;

public static class TypeExtensions
{
    public static IEnumerable<Type> GetAllTypesImplementingOpenGenericType(
        this Assembly assembly, Type openGenericType)
    {
        return from type in assembly.GetExportedTypes()
               from iface in type.GetInterfaces()
               let baseType = type.BaseType
               where
                   (baseType != null && baseType.IsGenericType &&
                    openGenericType.IsAssignableFrom(baseType.GetGenericTypeDefinition())) ||
                   (iface.IsGenericType &&
                    openGenericType.IsAssignableFrom(iface.GetGenericTypeDefinition()))
               select type;
    }
}
