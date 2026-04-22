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
    public void AggregatesOverridingRecordCreation_MustHaveGuidId()
    {
        // Aggregates whose RecordCreation() captures their Id into a creation event must use
        // client-generated Guid IDs. This is what keeps SaveChangesWithOutboxAsync a single
        // SaveChanges call — events are built BEFORE SaveChanges, so retry (EnableRetryOnFailure)
        // is safe. Int IDENTITY PKs would require a round-trip BEFORE RecordCreation, which
        // breaks the single-SaveChanges model and prohibits retry.
        var aggregateTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(AggregateRoot)))
            .ToList();

        var failures = new List<string>();

        foreach (var type in aggregateTypes)
        {
            var recordCreation = type.GetMethod("RecordCreation",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (recordCreation == null || recordCreation.DeclaringType == typeof(AggregateRoot))
                continue;

            var idProperty = type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
            if (idProperty == null || idProperty.PropertyType != typeof(Guid))
                failures.Add($"{type.Name} overrides RecordCreation but its Id is {idProperty?.PropertyType.Name ?? "missing"}. " +
                             "Aggregates that raise creation events must use client-generated Guid IDs (e.g. Guid.CreateVersion7() in the constructor).");
        }

        Assert.True(failures.Count == 0,
            "Aggregates overriding RecordCreation must have Guid Id:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void AggregateConstructors_MustNotCallRaiseDomainEvent()
    {
        // Creation events must be raised via the RecordCreation() override, which the DbContext
        // calls BEFORE SaveChanges. Raising from the constructor would fire the event before the
        // aggregate is attached to the ChangeTracker, so CaptureDomainEventsIntoOutbox would never
        // see it — the outbox row would be silently dropped. RecordCreation sidesteps this by
        // running on Added aggregates inside the SaveChanges pipeline.
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
                                 "Override RecordCreation() instead — the DbContext calls it before SaveChanges on Added aggregates, " +
                                 "so the event reaches the outbox in the same unit of work.");
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

    // === Event Contract ===

    [Fact]
    public void DomainEvents_MustExposeStableEventTypeContract()
    {
        // Every IDomainEvent must expose a non-empty EventType property that is independent
        // of the CLR type name. This ensures outbox messages and Service Bus routing use
        // stable, versioned contract strings.
        var domainEventTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(IDomainEvent)))
            .ToList();

        Assert.NotEmpty(domainEventTypes);

        var failures = new List<string>();
        foreach (var type in domainEventTypes)
        {
            var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type) as IDomainEvent;
            var eventType = instance?.EventType;

            if (string.IsNullOrWhiteSpace(eventType))
                failures.Add($"{type.Name} has a null/empty EventType. Add a const Contract and implement EventType => Contract.");
            else if (eventType == type.Name)
                failures.Add($"{type.Name}.EventType returns the CLR type name '{eventType}'. Use a stable versioned string (e.g. 'order.created.v1').");
        }

        Assert.True(failures.Count == 0,
            "All domain events must expose a stable, versioned EventType contract:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void DomainEvents_MustHaveUniqueEventTypeContracts()
    {
        var domainEventTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(IDomainEvent)))
            .ToList();

        var contractsByName = new Dictionary<string, List<string>>();
        foreach (var type in domainEventTypes)
        {
            var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type) as IDomainEvent;
            var eventType = instance?.EventType;
            if (string.IsNullOrWhiteSpace(eventType))
                continue;

            if (!contractsByName.TryGetValue(eventType, out var types))
            {
                types = [];
                contractsByName[eventType] = types;
            }
            types.Add(type.Name);
        }

        var duplicates = contractsByName
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => $"  Contract '{kvp.Key}' is used by: {string.Join(", ", kvp.Value)}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Domain event contracts must be unique:\n" + string.Join("\n", duplicates));
    }

    [Fact]
    public void ServiceBusSubscriptionFilters_MustReferenceValidEventContracts()
    {
        // Every EventType string in Service Bus subscription filters must match an actual
        // domain event's stable contract. Catches config drift after contract changes.
        var domainEventContracts = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(IDomainEvent)))
            .Select(t => (System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t) as IDomainEvent)?.EventType)
            .Where(e => !string.IsNullOrWhiteSpace(e))
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

                        var contract = eventType.GetString()!;
                        if (!domainEventContracts.Contains(contract))
                            orphanedFilters.Add($"  Subscription '{subscriptionName}' filters on '{contract}' but no IDomainEvent exposes that contract");
                    }
                }
            }
        }

        Assert.True(orphanedFilters.Count == 0,
            "Service Bus subscription filters reference non-existent event contracts. " +
            "Update config/servicebus-emulator.json to match domain event Contract constants:\n" +
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
