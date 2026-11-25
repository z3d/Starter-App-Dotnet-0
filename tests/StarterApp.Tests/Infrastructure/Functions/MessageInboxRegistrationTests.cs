namespace StarterApp.Tests.Infrastructure.Functions;

public class MessageInboxRegistrationTests
{
    [Fact]
    public void ADeployedWorkerWithNoDatabase_RefusesToStart_RatherThanDropDeduplication()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });

        var refusal = Assert.Throws<InvalidOperationException>(() => builder.AddMessageInbox());
        Assert.Contains("ConnectionStrings:database", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopersRunWithNoDatabase_RunsTheWorkWithoutAnInbox()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });

        builder.AddMessageInbox();

        using var host = builder.Build();
        Assert.IsType<NullMessageInbox>(host.Services.GetRequiredService<IMessageInbox>());
    }
}
