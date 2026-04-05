using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StarterApp.Api.Data;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Tests.Infrastructure.Outbox;

public class OutboxProcessorTests
{
    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static OutboxMessage CreateTestMessage(string type = "TestEvent", string payload = "{\"Id\":1}")
    {
        var domainEvent = new TestDomainEvent(type, payload, DateTimeOffset.UtcNow);
        return OutboxMessage.Create(domainEvent);
    }

    [Fact]
    public async Task ProcessBatch_ShouldPublishUnprocessedMessages()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);

        await using var verifyContext = CreateDbContext(dbName);
        var processed = await verifyContext.OutboxMessages.FirstAsync();
        Assert.NotNull(processed.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_ShouldSetEventTypeApplicationProperty()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        ServiceBusMessage? capturedMessage = null;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — Type is the runtime type name set by OutboxMessage.Create
        Assert.NotNull(capturedMessage);
        Assert.Equal(nameof(TestDomainEvent), capturedMessage.ApplicationProperties["EventType"]);
        Assert.Equal(nameof(TestDomainEvent), capturedMessage.Subject);
        Assert.Equal("application/json", capturedMessage.ContentType);
    }

    [Fact]
    public async Task ProcessBatch_WhenPublishFails_ShouldMarkMessageAsErrored()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Connection refused", ServiceBusFailureReason.ServiceCommunicationProblem));

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert
        await using var verifyContext = CreateDbContext(dbName);
        var errored = await verifyContext.OutboxMessages.FirstAsync();
        Assert.NotNull(errored.Error);
        Assert.Contains("Connection refused", errored.Error);
        Assert.Null(errored.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_ShouldSkipErroredMessages()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            var erroredMessage = CreateTestMessage();
            erroredMessage.MarkAsError("Previous failure");
            setupContext.OutboxMessages.Add(erroredMessage);
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — only the good message should have been published
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatch_ShouldRespectBatchSize()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            for (var i = 0; i < 5; i++)
                setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(dbName, senderMock.Object, batchSize: 3);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — only 3 of 5 should be published in one batch
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessBatch_WithNoMessages_ShouldNotPublish()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var senderMock = new Mock<ServiceBusSender>();
        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OutboxProcessor CreateProcessor(string databaseName, ServiceBusSender sender, int batchSize = 20)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => CreateDbContext(databaseName));
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = batchSize,
            TopicName = "domain-events"
        });

        return new OutboxProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            sender,
            options,
            new LoggerFactory().CreateLogger<OutboxProcessor>());
    }

    private static async Task RunSingleBatchAsync(OutboxProcessor processor, CancellationToken cancellationToken)
    {
        // Use reflection to call the private ProcessBatchAsync method
        var method = typeof(OutboxProcessor).GetMethod("ProcessBatchAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method!.Invoke(processor, [cancellationToken])!;
        await task;
    }

    private sealed record TestDomainEvent(string Type, string Data, DateTimeOffset OccurredOnUtc) : IDomainEvent;
}
