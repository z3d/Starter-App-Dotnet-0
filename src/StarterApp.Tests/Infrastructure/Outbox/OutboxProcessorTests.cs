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
        Assert.Equal("test.event.v1", capturedMessage.ApplicationProperties["EventType"]);
        Assert.Equal("test.event.v1", capturedMessage.Subject);
        Assert.Equal("application/json", capturedMessage.ContentType);
    }

    [Fact]
    public async Task ProcessBatch_WhenPublishFails_ShouldRetryBeforePermanentError()
    {
        // Arrange — MessageSizeExceeded is non-transient (message-level bug), so consumes retry budget
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Payload too large", ServiceBusFailureReason.MessageSizeExceeded));

        var processor = CreateProcessor(dbName, senderMock.Object, maxRetries: 3);

        // Act — first failure should increment RetryCount but not set Error
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await RunSingleBatchAsync(processor, cts.Token);

        await using (var verifyContext = CreateDbContext(dbName))
        {
            var message = await verifyContext.OutboxMessages.FirstAsync();
            Assert.Equal(1, message.RetryCount);
            Assert.Null(message.Error);
            Assert.Null(message.ProcessedOnUtc);
        }

        // Act — exhaust remaining retries to reach permanent error
        await RunSingleBatchAsync(processor, cts.Token);
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — after 3 failures, message should be permanently errored
        await using var finalContext = CreateDbContext(dbName);
        var errored = await finalContext.OutboxMessages.FirstAsync();
        Assert.Equal(3, errored.RetryCount);
        Assert.NotNull(errored.Error);
        Assert.Contains("Payload too large", errored.Error);
        Assert.Null(errored.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_WhenPublishFails_WithMaxRetriesOne_ShouldErrorImmediately()
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
            .ThrowsAsync(new ServiceBusException("Payload too large", ServiceBusFailureReason.MessageSizeExceeded));

        var processor = CreateProcessor(dbName, senderMock.Object, maxRetries: 1);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — with maxRetries=1, a single failure should permanently error
        await using var verifyContext = CreateDbContext(dbName);
        var errored = await verifyContext.OutboxMessages.FirstAsync();
        Assert.NotNull(errored.Error);
        Assert.Contains("Payload too large", errored.Error);
        Assert.Null(errored.ProcessedOnUtc);
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.ServiceCommunicationProblem)]
    [InlineData(ServiceBusFailureReason.ServiceTimeout)]
    [InlineData(ServiceBusFailureReason.ServiceBusy)]
    [InlineData(ServiceBusFailureReason.QuotaExceeded)]
    public async Task ProcessBatch_WhenTransientServiceBusFailure_ShouldNotConsumeRetryBudget(ServiceBusFailureReason reason)
    {
        // Arrange — transient dependency failures (SB down, throttled) must not poison messages
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("transient", reason));

        var processor = CreateProcessor(dbName, senderMock.Object, maxRetries: 1);

        // Act — run the batch many more times than MaxRetries would allow
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 10; i++)
            await RunSingleBatchAsync(processor, cts.Token);

        // Assert — message stays unprocessed with RetryCount = 0, no Error
        await using var verifyContext = CreateDbContext(dbName);
        var message = await verifyContext.OutboxMessages.FirstAsync();
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.Error);
        Assert.Null(message.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_WhenTransientFailureMidBatch_ShouldPauseRemainingMessages()
    {
        // Arrange — 3 messages; sender throws transient on the 2nd call
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            for (var i = 0; i < 3; i++)
                setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var callCount = 0;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ServiceBusMessage, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount == 2)
                    throw new ServiceBusException("transient", ServiceBusFailureReason.ServiceCommunicationProblem);
                return Task.CompletedTask;
            });

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — processor stops after transient error, 3rd message not attempted
        Assert.Equal(2, callCount);

        await using var verifyContext = CreateDbContext(dbName);
        var messages = await verifyContext.OutboxMessages.OrderBy(m => m.OccurredOnUtc).ToListAsync();
        Assert.NotNull(messages[0].ProcessedOnUtc);          // 1st succeeded
        Assert.Null(messages[1].ProcessedOnUtc);             // 2nd transient — unprocessed, retries intact
        Assert.Equal(0, messages[1].RetryCount);
        Assert.Null(messages[2].ProcessedOnUtc);             // 3rd never attempted
        Assert.Equal(0, messages[2].RetryCount);
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

    private static OutboxProcessor CreateProcessor(string databaseName, ServiceBusSender sender, int batchSize = 20, int maxRetries = 3)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => CreateDbContext(databaseName));
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = batchSize,
            MaxRetries = maxRetries,
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

    private sealed record TestDomainEvent(string Type, string Data, DateTimeOffset OccurredOnUtc) : IDomainEvent
    {
        public string EventType => "test.event.v1";
    }
}
