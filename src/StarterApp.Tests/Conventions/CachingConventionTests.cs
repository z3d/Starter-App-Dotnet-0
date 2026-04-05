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
            var instance1 = CreateDefaultInstance(type);
            var instance2 = CreateDefaultInstance(type);
            Assert.Equal(instance1.CacheKey, instance2.CacheKey);
        }
    }

    private static ICacheable CreateDefaultInstance(Type type)
    {
        // Try parameterless constructor first
        var parameterlessCtor = type.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor != null)
            return (ICacheable)parameterlessCtor.Invoke(null);

        // Fall back to constructors with value-type parameters (supply defaults)
        var ctor = type.GetConstructors().First();
        var args = ctor.GetParameters()
            .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();
        return (ICacheable)ctor.Invoke(args)!;
    }
}
