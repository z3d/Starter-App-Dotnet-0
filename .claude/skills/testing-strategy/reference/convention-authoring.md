# Writing Convention Tests

Built on [Best.Conventional](https://github.com/andrewabest/Conventional), in `src/StarterApp.Tests/Conventions/`.

## Use built-ins where one fits

```csharp
endpointTypes.MustConformTo(Convention.NameMustEndWith("Endpoints"));
entityTypes.MustConformTo(Convention.PropertiesMustHavePrivateSetters);
dtoTypes.MustConformTo(Convention.PropertiesMustHavePublicGetters);
commandHandlers.MustConformTo(Convention.MustNotTakeADependencyOn(typeof(IDbConnection), "..."));
commandsWithResponse.MustConformTo(Convention.RequiresACorrespondingImplementationOf(typeof(IRequestHandler<,>), allTypes));
entityTypes.MustConformTo(Convention.MustHaveANonPublicDefaultConstructor);
// also: VoidMethodsMustNotBeAsync, MustNotResolveCurrentTimeViaDateTime
```

## Custom specifications

For structural or wiring checks the built-ins don't cover, extend `ConventionSpecification`:

```csharp
private class MyCustomConvention : ConventionSpecification
{
    protected override string FailureMessage => "description of what's expected";

    public override ConventionResult IsSatisfiedBy(Type type)
    {
        return /* check passes */
            ? ConventionResult.Satisfied(type.FullName!)
            : ConventionResult.NotSatisfied(type.FullName!, "specific failure reason");
    }
}
```

Give `NotSatisfied` a reason specific enough that the fix is obvious from the test output alone.

## Behaviour checks: the IL layer

Presence checks (a constructor parameter exists, an interface is implemented) don't prove behaviour (the dependency is called, the SQL is filtered). The behavioural half uses IL inspection, and all the plumbing is shared:

- **`ConventionTestBase.GetAllMethodsIncludingStateMachines`** — walks async state machines and same-assembly base classes, opening closed generics. String literals and calls in an `async` method live in the compiled state-machine type, not the declaring method, so scanning without this misses almost everything.
- **`ConventionTestBase.ExtractStringLiterals`** — pulls `ldstr` operands; this is how the `SELECT *` ban and the owner-predicate SQL checks read query text out of compiled handlers. The `SELECT *` check allows `COUNT(*)` and other non-column-expanding uses.
- **`ConventionTestBase.IlReferencesType`** / **`ContainsCallToMethod(il, module, name)`** — the "was it actually invoked" primitives behind the invoke-checks for `IOwnerOnlyPolicy` and `ICacheInvalidator`.
- **`IlInstructionWalker`** (in `Consistency/`) — the operand-aware instruction iterator underneath all of the above.

**Never hand-roll a raw IL byte loop.** IL operands can contain any byte value; a loop that doesn't track instruction boundaries will eventually read an operand byte (an `ldc.i4` constant, a branch offset) as an opcode, and the failure mode is silent — the scan finds nothing, the test passes, the rule enforces nothing.

## Verification discipline

A convention test that has never failed is unproven. Before trusting a new one:

1. Inject the exact regression it guards — change `Guid.CreateVersion7()` to `Guid.NewGuid()`, point a dependency check at a type nothing injects, remove the owner predicate from one SQL literal.
2. Run the test. It must fail **with the intended message** — a failure for the wrong reason is a different bug.
3. Revert and confirm green.

Two traps in step 3:

- **Empty-set vacuity.** If the test filters a discovered set (handlers, aggregates, endpoints), `Assert.NotEmpty` on the set first. Otherwise a renamed suffix or a moved assembly empties the cohort and the test passes forever.
- **Stale builds.** Restoring a mutated source file via `mv`/`cp` of a backup can give it an *older* mtime than the regression-compiled DLL, so MSBuild skips the rebuild and the test looks like it's still failing. `touch` the source (or build `--no-incremental`) before re-running.

## Scan scope

A convention that scans one assembly enforces nothing in the others. When adding a rule, check which assemblies the cohort discovery covers, and name the test to match its actual scope (`ApiTypes_MustNot...`, not `Types_MustNot...`). AppHost, Functions, and ServiceDefaults are not referenced by `StarterApp.Tests` — rules for those belong in `StarterApp.AppHost.Tests`.
