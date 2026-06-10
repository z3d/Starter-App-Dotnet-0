namespace StarterApp.Tests.Infrastructure.Identity;

public class OwnerOnlyPolicyTests
{
    [Fact]
    public void Authorize_WithMatchingOwner_ShouldPass()
    {
        var policy = TestOwnerOnlyPolicy.For("subject-1", "tenant-1");

        policy.Authorize("subject-1", "tenant-1");
    }

    [Fact]
    public void Authorize_WithDifferentSubject_ShouldThrowForbidden()
    {
        var policy = TestOwnerOnlyPolicy.For("subject-1", "tenant-1");

        Assert.Throws<ForbiddenAccessException>(() => policy.Authorize("subject-2", "tenant-1"));
    }

    [Fact]
    public void GetRequiredScope_WhenAnonymous_ShouldThrowUnauthorized()
    {
        var policy = new OwnerOnlyPolicy(CurrentUser.Anonymous, new OwnerPolicyEvaluationTracker());

        Assert.Throws<UnauthorizedAccessException>(policy.GetRequiredScope);
    }
}
