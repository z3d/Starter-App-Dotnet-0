namespace StarterApp.Tests.Conventions.SyntheticDerivation
{
    public interface ICacheable;

    public interface IOwnerScopedRequest;

    public sealed class Customer : IOwnerScopedRequest;

    public sealed class OrderCreatedDomainEvent;

    public sealed class Ledger;
}

namespace StarterApp.Tests.Conventions.SyntheticDerivation.Modules.Billing
{
    public sealed class Invoice : ICacheable;

    public sealed class Order;
}
