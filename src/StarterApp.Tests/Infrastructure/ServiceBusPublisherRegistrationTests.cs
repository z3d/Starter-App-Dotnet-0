using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace StarterApp.Tests.Infrastructure;

public class ServiceBusPublisherRegistrationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AddServiceBusPublisher_WithoutConnectionString_OutsideDevelopmentLike_Throws(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddServiceBusPublisher(configuration, new FakeHostEnvironment(environmentName)));

        Assert.Contains("ConnectionStrings:servicebus", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AddServiceBusPublisher_WithoutConnectionString_InDevelopmentLike_IsNoOp(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddServiceBusPublisher(configuration, new FakeHostEnvironment(environmentName));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "StarterApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
