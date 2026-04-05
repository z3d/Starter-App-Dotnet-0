namespace StarterApp.Tests.Conventions;

public class DomainConventionTests : ConventionTestBase
{
    // === Encapsulation ===

    [Fact]
    public void DomainEntities_ShouldHaveProperEncapsulation()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract);
        entityTypes
            .MustConformTo(Convention.PropertiesMustHavePrivateSetters)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ValueObjects_ShouldBeImmutable()
    {
        var valueObjectTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("ValueObjects") &&
                   t.IsClass && !t.IsAbstract);
        valueObjectTypes
            .MustConformTo(Convention.PropertiesMustHavePrivateSetters)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void DTOs_ShouldHavePublicGetters()
    {
        var dtoTypes = ApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Dto") || t.Name.EndsWith("ReadModel"));
        dtoTypes
            .MustConformTo(Convention.PropertiesMustHavePublicGetters)
            .WithFailureAssertion(Assert.Fail);
    }

    // === Constructors ===

    [Fact]
    public void DomainEntities_MustHaveNonPublicDefaultConstructor()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));
        entityTypes
            .MustConformTo(Convention.MustHaveANonPublicDefaultConstructor)
            .WithFailureAssertion(Assert.Fail);
    }

    // === Equality ===

    [Fact]
    public void ValueObjects_MustOverrideEqualsAndGetHashCode()
    {
        var valueObjectTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("ValueObjects") &&
                   t.IsClass && !t.IsAbstract);
        valueObjectTypes
            .MustConformTo(new MustOverrideEqualsAndGetHashCodeConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    // === Async Methods ===

    [Fact]
    public void AsyncMethods_ShouldHaveAsyncSuffix()
    {
        var assemblies = new[] { ApiAssembly, DomainAssembly };

        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                       !t.Name.EndsWith("Handler") &&
                       !IsCompilerGenerated(t));
            types
                .MustConformTo(Convention.AsyncMethodsMustHaveAsyncSuffix)
                .WithFailureAssertion(Assert.Fail);
        }
    }

    // === Async Void ===

    [Fact]
    public void Methods_MustNotBeAsyncVoid()
    {
        var assemblies = new[] { ApiAssembly, DomainAssembly };
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));
            types
                .MustConformTo(Convention.VoidMethodsMustNotBeAsync)
                .WithFailureAssertion(Assert.Fail);
        }
    }

    // === DateTime Safety ===
    // Domain entities use DateTimeOffset.UtcNow for timestamps. API-layer code must not resolve
    // time directly, ensuring testability of application logic.

    [Fact]
    public void ApiTypes_MustNotResolveCurrentTimeViaDateTime()
    {
        var types = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));
        types
            .MustConformTo(Convention.MustNotResolveCurrentTimeViaDateTime)
            .WithFailureAssertion(Assert.Fail);
    }

    // === DateTimeOffset Enforcement ===

    [Fact]
    public void DomainTypes_MustUseDateTimeOffsetNotDateTime()
    {
        var domainTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));

        domainTypes
            .MustConformTo(new MustNotUseDateTimePropertiesConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    // === Domain Events ===

    [Fact]
    public void DomainEvents_MustNotHoldEntityReferences()
    {
        var domainEventTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(IDomainEvent)));

        Assert.NotEmpty(domainEventTypes);

        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract)
            .ToHashSet();

        var failures = new List<string>();
        foreach (var eventType in domainEventTypes)
        {
            var entityProperties = eventType.GetProperties()
                .Where(p => entityTypes.Contains(p.PropertyType))
                .ToList();

            foreach (var prop in entityProperties)
                failures.Add($"{eventType.Name}.{prop.Name} holds a reference to entity {prop.PropertyType.Name}. " +
                             "Domain events must carry flat data for safe outbox serialization.");
        }

        Assert.True(failures.Count == 0,
            $"Domain events must not hold entity references:\n{string.Join("\n", failures)}");
    }

    // === Custom Convention Specifications ===

    private class MustNotUseDateTimePropertiesConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must use DateTimeOffset instead of DateTime for all timestamp properties";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var dateTimeProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?))
                .ToList();

            return dateTimeProperties.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} uses DateTime on: {string.Join(", ", dateTimeProperties.Select(p => p.Name))}. Use DateTimeOffset instead.");
        }
    }

    // === Aggregate Construction Safety ===

    [Fact]
    public void AggregateConstructors_MustNotCallRaiseDomainEvent()
    {
        // Domain events raised in constructors capture pre-persist identity values (e.g. Id=0
        // for IDENTITY columns). Creation events must be raised via the RecordCreation() override
        // which the DbContext calls AFTER SaveChanges assigns database keys.
        var aggregateTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(AggregateRoot)));

        var failures = new List<string>();

        foreach (var type in aggregateTypes)
        {
            var constructors = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var ctor in constructors)
            {
                var body = ctor.GetMethodBody();
                if (body == null)
                    continue;

                var il = body.GetILAsByteArray();
                if (il == null)
                    continue;

                // Scan IL for a call/callvirt to RaiseDomainEvent
                if (ContainsCallToMethod(il, ctor.Module, "RaiseDomainEvent"))
                    failures.Add($"{type.Name} constructor calls RaiseDomainEvent. " +
                                 "Override RecordCreation() instead — the DbContext calls it after SaveChanges when IDENTITY values are assigned.");
            }
        }

        Assert.True(failures.Count == 0,
            "Aggregate constructors must not raise domain events (pre-persist keys would be captured):\n" +
            string.Join("\n", failures));
    }

    private static bool ContainsCallToMethod(byte[] il, Module module, string methodName)
    {
        // IL opcodes: call = 0x28, callvirt = 0x6F — both followed by a 4-byte metadata token
        for (var i = 0; i < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F) || i + 4 >= il.Length)
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var member = module.ResolveMember(token);
                if (member is MethodInfo method && method.Name == methodName)
                    return true;
            }
            catch (Exception)
            {
                // Token may not resolve (e.g. generic instantiation) — skip safely
            }

            i += 4; // skip the 4-byte token
        }

        return false;
    }

    // === Event Routing Contract ===

    [Fact]
    public void ServiceBusSubscriptionFilters_MustReferenceExistingDomainEvents()
    {
        // Every EventType string in the Service Bus subscription correlation filters must
        // correspond to an actual IDomainEvent class name. This catches silent routing
        // breakage when a domain event class is renamed.
        var domainEventNames = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(IDomainEvent)))
            .Select(t => t.Name)
            .ToHashSet();

        var configPath = ResolveServiceBusConfigPath();
        if (configPath == null)
            return;

        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var orphanedFilters = new List<string>();

        foreach (var ns in json.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray())
        {
            foreach (var topic in ns.GetProperty("Topics").EnumerateArray())
            {
                foreach (var subscription in topic.GetProperty("Subscriptions").EnumerateArray())
                {
                    var subscriptionName = subscription.GetProperty("Name").GetString()!;
                    foreach (var rule in subscription.GetProperty("Rules").EnumerateArray())
                    {
                        if (!rule.GetProperty("Properties").TryGetProperty("CorrelationFilter", out var filter))
                            continue;
                        if (!filter.TryGetProperty("ApplicationProperties", out var props))
                            continue;
                        if (!props.TryGetProperty("EventType", out var eventType))
                            continue;

                        var typeName = eventType.GetString()!;
                        if (!domainEventNames.Contains(typeName))
                            orphanedFilters.Add($"  Subscription '{subscriptionName}' filters on EventType '{typeName}' but no IDomainEvent class with that name exists");
                    }
                }
            }
        }

        Assert.True(orphanedFilters.Count == 0,
            "Service Bus subscription filters reference non-existent domain event types. " +
            "If you renamed a domain event class, update config/servicebus-emulator.json to match:\n" +
            string.Join("\n", orphanedFilters));
    }

    private static string? ResolveServiceBusConfigPath()
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(Product).Assembly.Location)!,
            "..", "..", "..", "..", "..", "config", "servicebus-emulator.json");

        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "..", "..", "..", "..", "..", "..", "config", "servicebus-emulator.json"));
        }

        return File.Exists(path) ? path : null;
    }

    // === Custom Convention Specifications ===

    private class MustOverrideEqualsAndGetHashCodeConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must override Equals(object) and GetHashCode()";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var equalsMethod = type.GetMethod("Equals", [typeof(object)]);
            var getHashCodeMethod = type.GetMethod("GetHashCode", Type.EmptyTypes);

            var failures = new List<string>();

            if (equalsMethod == null || equalsMethod.DeclaringType != type)
                failures.Add($"{type.Name} does not override Equals(object)");

            if (getHashCodeMethod == null || getHashCodeMethod.DeclaringType != type)
                failures.Add($"{type.Name} does not override GetHashCode()");

            return failures.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!, string.Join("; ", failures));
        }
    }
}
