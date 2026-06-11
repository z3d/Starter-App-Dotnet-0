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
    public void CacheableQueries_RefreshWindowMustBePositiveAndSmallerThanDuration()
    {
        foreach (var type in GetCacheableTypes())
        {
            var instance = CreateDefaultInstance(type);
            Assert.True(instance.CacheRefreshWindow > TimeSpan.Zero,
                $"{type.Name}.CacheRefreshWindow must be positive — without a refresh window a hot key expires " +
                "under load and every concurrent request recomputes at once (cache stampede)");
            Assert.True(instance.CacheRefreshWindow < instance.CacheDuration,
                $"{type.Name}.CacheRefreshWindow must be smaller than CacheDuration — a window covering the whole " +
                "TTL would recompute on every request and the cache would never serve a settled value");
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

    [Fact]
    public void MutationHandlers_OnCacheableEntities_MustInjectCacheInvalidator()
    {
        // Mechanical rule: any non-create command handler that mutates an entity with a
        // cacheable by-id query must inject ICacheInvalidator so stale entity reads are purged.
        var cacheableResources = GetCacheableResourceNames();

        var mutationHandlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(t => !t.Name.StartsWith("Create", StringComparison.Ordinal))
            .Where(t => cacheableResources.Any(resource => HandlerTargetsResource(t, resource)));

        var violations = mutationHandlers
            .Where(handler => !handler.GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(ICacheInvalidator))))
            .Select(handler => handler.FullName ?? handler.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Mutation handlers for cacheable entities must inject ICacheInvalidator:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void MutationHandlers_OnCacheableEntities_MustInvokeCacheInvalidator()
    {
        // Injecting ICacheInvalidator is necessary but not sufficient — a handler that injects it and
        // never calls it leaves stale entity reads in the distributed cache after a write. Scan handler
        // IL (including async state machines) for an actual call to a member declared on ICacheInvalidator,
        // closing the gap where MustInjectCacheInvalidator passes for an inject-and-forget handler.
        var handlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(InjectsCacheInvalidator)
            .ToList();

        Assert.NotEmpty(handlers);

        var violations = handlers
            .Where(handler => !GetAllMethodsIncludingStateMachines(handler)
                .Any(method => IlReferencesType(method, nameof(ICacheInvalidator))))
            .Select(handler => handler.FullName ?? handler.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Command handlers that inject ICacheInvalidator must actually call it after SaveChangesAsync — " +
            "injecting the invalidator without invoking it leaves stale entity reads in the distributed cache:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void CommandHandlers_MutatingCacheableEntities_MustInvokeCacheInvalidator()
    {
        // Cross-entity cache rule. A command handler that changes a cacheable entity's persisted state
        // must invalidate that entity's cache, even when the mutated entity differs from the entity the
        // handler is named after — the order handlers decrement/restore Product stock, and Product has a
        // cacheable by-id read model that carries Stock. The name-based MustInjectCacheInvalidator rule
        // misses this (it matches by handler name and excludes Create*), so detect the mutation
        // structurally: scan the handler plus any application helper it delegates to (e.g.
        // OrderCancellationService) for a Product stock write (Product.UpdateStock or EF ExecuteUpdateAsync).
        var handlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(MutatesProductStock)
            .ToList();

        Assert.NotEmpty(handlers);

        var violations = handlers
            .Where(handler => !GetAllMethodsIncludingStateMachines(handler)
                .Any(method => IlReferencesType(method, nameof(ICacheInvalidator))))
            .Select(handler => handler.FullName ?? handler.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Command handlers that mutate Product stock (directly or via a delegated service) must invoke " +
            "ICacheInvalidator to purge the cached product read model — otherwise GetProductByIdQuery serves " +
            "stale stock for the cache duration:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void ListQueries_MustNotBeCacheable()
    {
        var violations = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => (QueryType: t, ResponseType: ResolveQueryResponseType(t)))
            .Where(x => x.ResponseType is not null)
            .Where(x => IsListShape(x.ResponseType!) && typeof(ICacheable).IsAssignableFrom(x.QueryType))
            .Select(x => x.QueryType.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Only by-id queries may implement ICacheable. List and paged queries must not be cached " +
            "because IDistributedCache has no pattern-based invalidation. Offenders:\n" +
            string.Join("\n", violations));
    }

    private static bool InjectsCacheInvalidator(Type handler) =>
        handler.GetConstructors()
            .Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ICacheInvalidator)));

    // Distinctive method names that mean "this code wrote product stock": the domain mutation method
    // (Product.UpdateStock) and the EF bulk-update used by the atomic reservation path (ExecuteUpdateAsync).
    private static readonly string[] ProductStockMutationMethods = ["UpdateStock", "ExecuteUpdateAsync"];

    private static bool MutatesProductStock(Type handler)
    {
        var typesToScan = new HashSet<Type> { handler };
        foreach (var helper in GetReferencedCommandHelperTypes(handler))
            typesToScan.Add(helper);

        return typesToScan
            .SelectMany(GetAllMethodsIncludingStateMachines)
            .Any(method => ProductStockMutationMethods.Any(name => IlReferencesMethodNamed(method, name)));
    }

    // Application-layer helper types the handler delegates to (e.g. OrderCancellationService), so a
    // stock write performed inside a shared service is still attributed to the calling handler.
    private static IEnumerable<Type> GetReferencedCommandHelperTypes(Type handler)
    {
        var helpers = new HashSet<Type>();
        foreach (var method in GetAllMethodsIncludingStateMachines(handler))
        {
            foreach (var member in ResolveCalledMembers(method))
            {
                var declaringType = member.DeclaringType;
                if (declaringType is { Namespace: "StarterApp.Api.Application.Commands" }
                    && !declaringType.Name.EndsWith("CommandHandler", StringComparison.Ordinal)
                    && !declaringType.Name.EndsWith("Command", StringComparison.Ordinal))
                {
                    helpers.Add(declaringType);
                }
            }
        }

        return helpers;
    }

    private static bool IlReferencesMethodNamed(MethodInfo method, string methodName) =>
        ResolveCalledMembers(method).Any(member => member.Name == methodName);

    private static IEnumerable<MemberInfo> ResolveCalledMembers(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null)
            yield break;

        var il = body.GetILAsByteArray();
        if (il == null)
            yield break;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            // call / callvirt, each followed by a 4-byte metadata token.
            if (il[i] is not (0x28 or 0x6F))
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            MemberInfo? member = null;
            try
            {
                member = module.ResolveMember(token);
            }
            catch
            {
                // Unresolvable generic instantiation — skip.
            }

            if (member != null)
                yield return member;

            i += 4;
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

    private static IReadOnlySet<string> GetCacheableResourceNames() =>
        GetCacheableTypes()
            .Select(ExtractCacheableResourceName)
            .Where(resource => !string.IsNullOrWhiteSpace(resource))
            .Select(resource => resource!)
            .ToHashSet(StringComparer.Ordinal);

    private static string? ExtractCacheableResourceName(Type queryType)
    {
        var cacheKeyPrefix = ExtractCacheKeyPrefix(queryType);
        if (!string.IsNullOrWhiteSpace(cacheKeyPrefix))
            return cacheKeyPrefix;

        var name = queryType.Name;
        if (name.EndsWith("Query", StringComparison.Ordinal))
            name = name[..^"Query".Length];

        if (name.StartsWith("Get", StringComparison.Ordinal))
            name = name["Get".Length..];

        if (name.EndsWith("ById", StringComparison.Ordinal))
            name = name[..^"ById".Length];

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? ExtractCacheKeyPrefix(Type queryType)
    {
        var cacheKey = CreateDefaultInstance(queryType).CacheKey;
        var separatorIndex = cacheKey.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex > 0 ? cacheKey[..separatorIndex] : null;
    }

    private static bool HandlerTargetsResource(Type handlerType, string resourceName) =>
        handlerType.Name.Contains(resourceName, StringComparison.Ordinal);

    private static Type? ResolveQueryResponseType(Type queryType)
    {
        var queryInterface = queryType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

        return queryInterface?.GetGenericArguments()[0];
    }

    private static bool IsListShape(Type responseType)
    {
        if (responseType.IsArray)
            return true;

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(PagedResponse<>))
            return true;

        if (!responseType.IsGenericType)
            return false;

        var genericType = responseType.GetGenericTypeDefinition();
        return genericType == typeof(IReadOnlyList<>)
            || genericType == typeof(IReadOnlyCollection<>)
            || genericType == typeof(IEnumerable<>)
            || genericType == typeof(ICollection<>)
            || genericType == typeof(List<>);
    }
}
