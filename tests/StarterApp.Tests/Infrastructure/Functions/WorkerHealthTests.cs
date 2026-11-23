using Microsoft.Azure.Functions.Worker;
using StarterApp.Functions;

namespace StarterApp.Tests.Infrastructure.Functions;

public class WorkerHealthTests
{
    private static readonly Assembly Functions = typeof(WorkerHealth).Assembly;

    [Fact]
    public void EverySubscriptionTheWorkerReads_IsChecked()
    {
        var triggers = Functions.GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters())
            .Count(parameter => parameter.GetCustomAttribute<ServiceBusTriggerAttribute>() is not null);

        var subscriptions = WorkerHealth.Subscriptions(Functions);

        Assert.NotEmpty(subscriptions);
        Assert.Equal(triggers, subscriptions.Count);
        Assert.Contains(("domain-events", "email-notifications"), subscriptions);
    }

    [Fact]
    public async Task WithNoServiceBusAndNoDatabase_EveryCheckIsNamedAndUnhealthy()
    {
        var checks = await WorkerHealth.For(serviceBus: null, database: null).CheckAsync(CancellationToken.None);

        Assert.Equal(WorkerHealth.Subscriptions(Functions).Count + 1, checks.Count);
        Assert.Contains("database", checks.Keys);
        Assert.Contains("servicebus-domain-events-email-notifications", checks.Keys);
        Assert.All(checks.Values, status => Assert.Equal("unhealthy (InvalidOperationException)", status));
        Assert.False(WorkerHealth.IsHealthy(checks));
    }

    [Fact]
    public async Task AFailedCheck_GivesItsTypeAndNeverItsMessage()
    {
        var health = new WorkerHealth(
        [
            ("reads", _ => Task.CompletedTask),
            ("refused", _ => throw new UnauthorizedAccessException("principal 1234 on host db.internal")),
        ], TimeSpan.FromSeconds(5));

        var checks = await health.CheckAsync(CancellationToken.None);

        Assert.Equal(WorkerHealth.Healthy, checks["reads"]);
        Assert.Equal("unhealthy (UnauthorizedAccessException)", checks["refused"]);
        Assert.False(WorkerHealth.IsHealthy(checks));
    }

    [Fact]
    public async Task ACheckThatHangs_TimesOutAndTheOthersStillAnswer()
    {
        var health = new WorkerHealth(
        [
            ("hangs", token => Task.Delay(Timeout.InfiniteTimeSpan, token)),
            ("ignores-the-token", _ => new TaskCompletionSource().Task),
            ("reads", _ => Task.CompletedTask),
        ], TimeSpan.FromMilliseconds(50));

        var checks = await health.CheckAsync(CancellationToken.None);

        Assert.Equal("unhealthy (timed out)", checks["hangs"]);
        Assert.Equal("unhealthy (timed out)", checks["ignores-the-token"]);
        Assert.Equal(WorkerHealth.Healthy, checks["reads"]);
    }

    [Fact]
    public async Task WhenEveryCheckPasses_TheWorkerIsHealthy()
    {
        var health = new WorkerHealth([("reads", _ => Task.CompletedTask)], TimeSpan.FromSeconds(5));

        Assert.True(WorkerHealth.IsHealthy(await health.CheckAsync(CancellationToken.None)));
    }

    // The worker does no work for an HTTP caller: a change is made through the API's routes or the worker's own triggers.
    [Fact]
    public void TheWorkersOnlyHttpAddress_IsItsHealthCheck()
    {
        var http = Functions.GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => (Method: method, Trigger: parameter.GetCustomAttribute<HttpTriggerAttribute>())))
            .Where(pair => pair.Trigger is not null)
            .ToList();

        var only = Assert.Single(http);
        Assert.Equal(typeof(HealthFunction), only.Method.DeclaringType);
        Assert.Equal(HealthFunction.Route, only.Trigger!.Route);
        Assert.Equal(["get"], only.Trigger.Methods ?? []);
        Assert.Equal(AuthorizationLevel.Anonymous, only.Trigger.AuthLevel);
    }

    [Fact]
    public void TheLocalStackAndItsTests_WaitOnTheHealthCheck()
    {
        var appHost = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "dev", "StarterApp.AppHost", "Program.cs"));
        var fixture = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "StarterApp.AppHost.Tests", "AspireE2EFixture.cs"));

        Assert.Contains($".WithHttpHealthCheck(\"/api/{HealthFunction.Route}\")", appHost);
        Assert.Contains($"\"/api/{HealthFunction.Route}\", \"functions\"", fixture);
    }
}
