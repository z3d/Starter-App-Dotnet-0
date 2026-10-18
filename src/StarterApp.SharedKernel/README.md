# StarterApp.SharedKernel

What every domain in the solution builds on, and nothing else:

- `Abstractions/` — `AggregateRoot`, `DomainEvent`, `IDomainEvent`, `OwnershipDefaults`
- `Exceptions/` — `DomainRuleException` (409) and `EntityNotFoundException` (404), the app-wide error model
- `ValueObjects/` — `Email`, `Money`

No entity, no concrete event and nothing a database maps lives here. The business domain
lives next door in `StarterApp.Domain` (the Customer/Product/Order sample in the template) or,
in a derived project, inside each module under `src/<App>.Api/Modules/<Name>/Domain/`.
`SharedKernelConventionTests` fails the build if a domain type is added here, and
`DerivationConventionTests` fails a derived project that keeps the sample `Domain` project
once it has a module.
