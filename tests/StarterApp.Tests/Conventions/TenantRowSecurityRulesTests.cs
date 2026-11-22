namespace StarterApp.Tests.Conventions;

// The rules run against the live catalog in Integration/TenantRowSecurityTests; here each is shown failing on a made-up violation.
public class TenantRowSecurityRulesTests
{
    private const string Session = "((tenant_id)::text = NULLIF(current_setting('app.tenant_id'::text, true), ''::text))";

    private static readonly TenantRowSecurityRules.Table Invoices = new("billing.invoices", HasTenantId: true, RowSecurity: true, ForceRowSecurity: true);
    private static readonly TenantRowSecurityRules.Policy InvoicesPolicy = new("billing.invoices", "tenant_isolation_invoices", true, ["public"], "ALL", Session, Session);
    private static readonly Dictionary<string, string> NoPlatformTables = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> NoCrossTenantPolicies = new(StringComparer.Ordinal);

    [Fact]
    public void AForcedTenantTableWithItsPolicy_Passes()
    {
        Assert.Empty(TenantRowSecurityRules.Violations([Invoices], [InvoicesPolicy], NoPlatformTables, NoCrossTenantPolicies));
    }

    [Theory]
    [InlineData(false, true, "not enabled")]
    [InlineData(true, false, "not forced")]
    public void ATenantTable_WithoutRowSecurityOnAndForced_Fails(bool enabled, bool forced, string expected)
    {
        var table = Invoices with { RowSecurity = enabled, ForceRowSecurity = forced };

        Assert.Contains(TenantRowSecurityRules.Violations([table], [InvoicesPolicy], NoPlatformTables, NoCrossTenantPolicies), failure => failure.Contains(expected, StringComparison.Ordinal));
    }

    public static TheoryData<TenantRowSecurityRules.Policy> WeakPolicies => new()
    {
        InvoicesPolicy with { WithCheck = null },
        InvoicesPolicy with { Using = "true", WithCheck = "true" },
        InvoicesPolicy with { Using = $"({Session} OR true)" },
        InvoicesPolicy with { Command = "SELECT" },
        InvoicesPolicy with { Roles = ["starterapp_app"] },
        InvoicesPolicy with { Permissive = false },
    };

    [Theory]
    [MemberData(nameof(WeakPolicies))]
    public void ATenantTable_WhosePolicyDoesNotHoldEveryCommandToTheSessionTenant_Fails(TenantRowSecurityRules.Policy policy)
    {
        var failures = TenantRowSecurityRules.Violations([Invoices], [policy], NoPlatformTables, NoCrossTenantPolicies);

        Assert.Contains(failures, failure => failure.Contains("no permissive policy", StringComparison.Ordinal));
    }

    [Fact]
    public void ATableWithNoTenantId_AndNoStatedReason_Fails()
    {
        var table = new TenantRowSecurityRules.Table("billing.rates", HasTenantId: false, RowSecurity: false, ForceRowSecurity: false);

        Assert.Single(TenantRowSecurityRules.Violations([table], [], NoPlatformTables, NoCrossTenantPolicies));
        Assert.Empty(TenantRowSecurityRules.Violations([table], [], new Dictionary<string, string> { ["billing.rates"] = "reference data" }, NoCrossTenantPolicies));
    }

    [Fact]
    public void ATenantTable_ListedAsAPlatformTable_Fails()
    {
        var failures = TenantRowSecurityRules.Violations([Invoices], [InvoicesPolicy], new Dictionary<string, string> { ["billing.invoices"] = "says it is shared" }, NoCrossTenantPolicies);

        Assert.Contains(failures, failure => failure.Contains("listed as a platform table", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExtraPolicy_NotListedAsCrossTenant_Fails()
    {
        var extra = new TenantRowSecurityRules.Policy("billing.invoices", "everyone_reads", true, ["public"], "SELECT", "true", null);

        Assert.Single(TenantRowSecurityRules.Violations([Invoices], [InvoicesPolicy, extra], NoPlatformTables, NoCrossTenantPolicies));
        Assert.Empty(TenantRowSecurityRules.Violations([Invoices], [InvoicesPolicy, extra], NoPlatformTables, new Dictionary<string, string> { ["billing.invoices/everyone_reads"] = "a reason" }));
    }

    [Fact]
    public void AListedTableOrPolicy_ThatIsGoneOrNowHoldsATenant_IsStale()
    {
        var rates = new TenantRowSecurityRules.Table("billing.rates", HasTenantId: false, RowSecurity: false, ForceRowSecurity: false);
        var platform = new Dictionary<string, string> { ["billing.rates"] = "reference data", ["billing.gone"] = "dropped", ["billing.invoices"] = "now a tenant's" };
        var cross = new Dictionary<string, string> { ["billing.invoices/dropped_policy"] = "dropped" };

        var stale = TenantRowSecurityRules.StaleEntries([rates, Invoices], [InvoicesPolicy], platform, cross);

        Assert.Equal(3, stale.Count);
        Assert.Contains(stale, entry => entry.Contains("billing.gone", StringComparison.Ordinal));
        Assert.Contains(stale, entry => entry.Contains("billing.invoices,", StringComparison.Ordinal));
        Assert.Contains(stale, entry => entry.Contains("dropped_policy", StringComparison.Ordinal));
    }
}
