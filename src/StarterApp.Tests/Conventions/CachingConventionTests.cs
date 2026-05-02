namespace StarterApp.Tests.Conventions;

public class CachingConventionTests : ConventionTestBase
{
    private static IEnumerable<Type> GetCacheableTypes() =>
        ApiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICacheable).IsAssignableFrom(t));

    [Fact]
    public void CacheableQueries_CacheKeyMustNotBeNullOrEmpty()
    {
        foreach (var type in GetCacheableTypes())
        {
            var instance = CreateDefaultInstance(type);
            Assert.False(string.IsNullOrWhiteSpace(instance.CacheKey),
                $"{type.Name} implements ICacheable but CacheKey is null or empty");
        }
    }

    [Fact]
    public void CacheableQueries_CacheDurationMustBePositive()
    {
        foreach (var type in GetCacheableTypes())
        {
            var instance = CreateDefaultInstance(type);
            Assert.True(instance.CacheDuration > TimeSpan.Zero,
                $"{type.Name}.CacheDuration must be positive");
        }
    }

    [Fact]
    public void CacheableQueries_MustHaveDeterministicCacheKeys()
    {
        foreach (var type in GetCacheableTypes())
        {
            var instance1 = CreateInstance(type, identity: 1);
            var instance2 = CreateInstance(type, identity: 1);
            Assert.Equal(instance1.CacheKey, instance2.CacheKey);
        }
    }

    [Fact]
    public void CacheableQueries_MustVaryCacheKeysByIdentity()
    {
        foreach (var type in GetCacheableTypes())
        {
            var instance1 = CreateInstance(type, identity: 1);
            var instance2 = CreateInstance(type, identity: 2);
            Assert.NotEqual(instance1.CacheKey, instance2.CacheKey);
        }
    }

    private static ICacheable CreateDefaultInstance(Type type) => CreateInstance(type, identity: 1);

    private static ICacheable CreateInstance(Type type, int identity)
    {
        var constructorWithId = type.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Any(p =>
                p.Name != null &&
                p.Name.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                p.ParameterType == typeof(int)));

        if (constructorWithId != null)
        {
            var args = constructorWithId.GetParameters()
                .Select(p => p.ParameterType == typeof(int) &&
                             p.Name != null &&
                             p.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
                    ? identity
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                .ToArray();
            return (ICacheable)constructorWithId.Invoke(args)!;
        }

        var parameterlessCtor = type.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor != null)
        {
            var instance = parameterlessCtor.Invoke(null);
            var idProperty = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (idProperty is { CanWrite: true } && idProperty.PropertyType == typeof(int))
                idProperty.SetValue(instance, identity);

            return (ICacheable)instance;
        }

        throw new InvalidOperationException(
            $"{type.Name} implements ICacheable but cannot be constructed with an integer Id. " +
            "Only by-id queries should opt into distributed caching.");
    }
}
