using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StarterApp.ServiceDefaults.Payloads;

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

    private static OutboxMessage CreateTestMessage(
        string type = "TestEvent",
        string payload = "{\"Id\":1}",
        DateTimeOffset? occurredOnUtc = null)
    {
        // Callers that add multiple messages in the same test must pass distinct occurredOnUtc
        // values — the OutboxProcessor orders claimed messages by OccurredOnUtc, so ties produce
        // a non-deterministic processing order and poison positional assertions.
        var domainEvent = new TestDomainEvent(type, payload, occurredOnUtc ?? DateTimeOffset.UtcNow);
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
        Assert.Null(processed.ProcessingId);
        Assert.Null(processed.LockedUntilUtc);
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
    public async Task ProcessBatch_ShouldPropagateCorrelationAndCaptureOutboundPayload()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            using var correlationScope = CorrelationContext.Push("support-case-123");
            setupContext.OutboxMessages.Add(CreateTestMessage(payload: "{\"Email\":\"customer@example.com\",\"Total\":42}"));
            await setupContext.SaveChangesAsync();
        }

        ServiceBusMessage? capturedMessage = null;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var payloadStore = new InMemoryPayloadArchiveStore();
        var processor = CreateProcessor(dbName, senderMock.Object, payloadStore: payloadStore);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal("support-case-123", capturedMessage.CorrelationId);
        Assert.Equal("support-case-123", capturedMessage.ApplicationProperties[CorrelationContext.ApplicationPropertyName]);

        var archiveEntry = payloadStore.Lines.Single(pair => pair.Key.StartsWith("archive/", StringComparison.Ordinal));
        Assert.EndsWith("/support-case-123.jsonl", archiveEntry.Key);
        Assert.Contains("\"channel\":\"servicebus\"", archiveEntry.Value.Single());
        Assert.Contains("customer@example.com", archiveEntry.Value.Single());
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
        Assert.NotNull(message.ProcessingId);
        Assert.NotNull(message.LockedUntilUtc);
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatch_WhenFailClosedPayloadArchiveHasTransientFailure_ShouldNotConsumeRetryBudget()
    {
        // Arrange — fail-closed archive outages are dependency failures, not message defects
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(
            dbName,
            senderMock.Object,
            maxRetries: 1,
            payloadStore: new ThrowingPayloadArchiveStore(new RequestFailedException(503, "archive unavailable")),
            payloadCaptureOptions: new PayloadCaptureOptions { ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed });

        // Act — run the batch many more times than MaxRetries would allow
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 10; i++)
            await RunSingleBatchAsync(processor, cts.Token);

        // Assert — capture failed before send, and retry budget stayed untouched
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var verifyContext = CreateDbContext(dbName);
        var message = await verifyContext.OutboxMessages.FirstAsync();
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.Error);
        Assert.Null(message.ProcessedOnUtc);
        Assert.NotNull(message.ProcessingId);
        Assert.NotNull(message.LockedUntilUtc);
    }

    [Fact]
    public async Task ProcessBatch_WhenFailClosedPayloadArchiveHasNonTransientFailure_ShouldNotPoisonMessage()
    {
        // #79: under FailClosed a NON-transient capture failure (e.g. a persistent config/serialization
        // defect, not a transient outage) must NOT consume the message's retry budget or mark it Error —
        // that would permanently lose the event though Service Bus was healthy. It must pause the batch so
        // the event is retried once the archive recovers.
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage());
            await setupContext.SaveChangesAsync();
        }

        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(
            dbName,
            senderMock.Object,
            maxRetries: 1,
            payloadStore: new ThrowingPayloadArchiveStore(new InvalidOperationException("non-transient capture defect")),
            payloadCaptureOptions: new PayloadCaptureOptions { ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed });

        // Act — run far more times than MaxRetries=1 would allow if it were counting retries
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 10; i++)
            await RunSingleBatchAsync(processor, cts.Token);

        // Assert — never published, never poisoned, retry budget untouched
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var verifyContext = CreateDbContext(dbName);
        var message = await verifyContext.OutboxMessages.FirstAsync();
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.Error);
        Assert.Null(message.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_WhenTransientFailureMidBatch_ShouldPauseRemainingMessages()
    {
        // Arrange — 3 messages; sender throws transient on the 2nd call. OccurredOnUtc is spaced
        // by 1ms per message so the processor (which ORDER BYs OccurredOnUtc) and the verify
        // query below both see the same insertion order — otherwise timestamp ties on fast
        // machines produce a non-deterministic processing order.
        var dbName = Guid.NewGuid().ToString();
        var baseTime = DateTimeOffset.UtcNow;
        await using (var setupContext = CreateDbContext(dbName))
        {
            for (var i = 0; i < 3; i++)
                setupContext.OutboxMessages.Add(CreateTestMessage(occurredOnUtc: baseTime.AddMilliseconds(i)));
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
        Assert.Null(messages[0].ProcessingId);
        Assert.Null(messages[0].LockedUntilUtc);
        Assert.Null(messages[1].ProcessedOnUtc);             // 2nd transient — unprocessed, retries intact
        Assert.Equal(0, messages[1].RetryCount);
        Assert.NotNull(messages[1].ProcessingId);
        Assert.NotNull(messages[1].LockedUntilUtc);
        Assert.Null(messages[2].ProcessedOnUtc);             // 3rd never attempted
        Assert.Equal(0, messages[2].RetryCount);
        Assert.NotNull(messages[2].ProcessingId);
        Assert.NotNull(messages[2].LockedUntilUtc);
    }

    [Fact]
    public async Task ProcessBatch_WithUnexpiredClaim_ShouldSkipMessage()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            var claimedMessage = CreateTestMessage();
            claimedMessage.Claim(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(1));
            setupContext.OutboxMessages.Add(claimedMessage);
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
        senderMock.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var verifyContext = CreateDbContext(dbName);
        var message = await verifyContext.OutboxMessages.FirstAsync();
        Assert.Null(message.ProcessedOnUtc);
        Assert.NotNull(message.ProcessingId);
        Assert.NotNull(message.LockedUntilUtc);
    }

    [Fact]
    public async Task ProcessBatch_WithExpiredClaim_ShouldReclaimAndPublishMessage()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using (var setupContext = CreateDbContext(dbName))
        {
            var claimedMessage = CreateTestMessage();
            claimedMessage.Claim(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(-1));
            setupContext.OutboxMessages.Add(claimedMessage);
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
        var message = await verifyContext.OutboxMessages.FirstAsync();
        Assert.NotNull(message.ProcessedOnUtc);
        Assert.Null(message.ProcessingId);
        Assert.Null(message.LockedUntilUtc);
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
    public async Task ProcessBatch_WhenOneClaimIsStolenMidBatch_ShouldPersistRemainingOutcomes()
    {
        // ProcessingId is a concurrency token. If another replica reclaims one row mid-batch
        // (expired lock), the batch-final SaveChanges must not discard every other outcome —
        // that would republish already-published messages outside the dedup window.
        var dbName = Guid.NewGuid().ToString();
        var baseTime = DateTimeOffset.UtcNow;
        await using (var setupContext = CreateDbContext(dbName))
        {
            setupContext.OutboxMessages.Add(CreateTestMessage(occurredOnUtc: baseTime));
            setupContext.OutboxMessages.Add(CreateTestMessage(occurredOnUtc: baseTime.AddMilliseconds(1)));
            await setupContext.SaveChangesAsync();
        }

        // Steal the SECOND message's claim while the processor is publishing the first.
        var sendCount = 0;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ServiceBusMessage, CancellationToken>(async (_, ct) =>
            {
                sendCount++;
                if (sendCount == 1)
                {
                    await using var thiefContext = CreateDbContext(dbName);
                    var victim = await thiefContext.OutboxMessages.OrderBy(m => m.OccurredOnUtc).LastAsync(ct);
                    victim.Claim(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(5));
                    await thiefContext.SaveChangesAsync(ct);
                }
            });

        var processor = CreateProcessor(dbName, senderMock.Object);

        // Act — must not throw, and must not lose the first message's outcome
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await RunSingleBatchAsync(processor, cts.Token);

        // Assert — first outcome persisted; stolen row left to its new owner
        await using var verifyContext = CreateDbContext(dbName);
        var messages = await verifyContext.OutboxMessages.OrderBy(m => m.OccurredOnUtc).ToListAsync();
        Assert.NotNull(messages[0].ProcessedOnUtc);
        Assert.Null(messages[0].ProcessingId);
        Assert.Null(messages[1].ProcessedOnUtc);
        Assert.NotNull(messages[1].ProcessingId);
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

    [Fact]
    public async Task CleanupExpiredMessages_ShouldPurgeOnlyProcessedAndErroredRowsPastRetention()
    {
        // Processed/errored rows carry full event payloads and accumulated forever; retention must
        // purge them while never touching pending (unprocessed, unerrored) rows of any age.
        var dbName = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        await using (var setupContext = CreateDbContext(dbName))
        {
            var processedOld = CreateTestMessage(occurredOnUtc: now.AddDays(-60));
            processedOld.MarkAsProcessed(now.AddDays(-60));

            var processedRecent = CreateTestMessage(occurredOnUtc: now.AddDays(-1));
            processedRecent.MarkAsProcessed(now.AddDays(-1));

            var erroredOld = CreateTestMessage(occurredOnUtc: now.AddDays(-60));
            erroredOld.MarkAsError("permanent failure");

            var pendingOld = CreateTestMessage(occurredOnUtc: now.AddDays(-60));

            setupContext.OutboxMessages.AddRange(processedOld, processedRecent, erroredOld, pendingOld);
            await setupContext.SaveChangesAsync();
        }

        var processor = CreateProcessor(dbName, new Mock<ServiceBusSender>().Object);

        var deleted = await processor.CleanupExpiredMessagesAsync(CancellationToken.None);

        Assert.Equal(2, deleted);

        await using var verifyContext = CreateDbContext(dbName);
        var remaining = await verifyContext.OutboxMessages.ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, m => m.ProcessedOnUtc != null);   // recent processed row kept
        Assert.Contains(remaining, m => m.ProcessedOnUtc == null && m.Error == null); // pending kept regardless of age
    }

    private static OutboxProcessor CreateProcessor(
        string databaseName,
        ServiceBusSender sender,
        int batchSize = 20,
        int maxRetries = 3,
        IPayloadArchiveStore? payloadStore = null,
        PayloadCaptureOptions? payloadCaptureOptions = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => CreateDbContext(databaseName));
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = batchSize,
            MaxRetries = maxRetries,
            LockDurationSeconds = 60,
            TopicName = "domain-events"
        });

        return new OutboxProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            sender,
            CreatePayloadCaptureSink(payloadStore ?? new InMemoryPayloadArchiveStore(), payloadCaptureOptions),
            options,
            new NullJobRunRecorder(),
            new LoggerFactory().CreateLogger<OutboxProcessor>());
    }

    private static PayloadCaptureSink CreatePayloadCaptureSink(IPayloadArchiveStore payloadStore, PayloadCaptureOptions? captureOptions)
    {
        var options = Options.Create(captureOptions ?? new PayloadCaptureOptions());
        var logger = new LoggerFactory().CreateLogger<PayloadCaptureSink>();
        return new PayloadCaptureSink(payloadStore, new JsonPayloadRedactor(options), TimeProvider.System, options, logger);
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

    private sealed class ThrowingPayloadArchiveStore : IPayloadArchiveStore
    {
        private readonly Exception _exception;

        public ThrowingPayloadArchiveStore(Exception exception)
        {
            _exception = exception;
        }

        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
        {
            return Task.FromException(_exception);
        }

        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
        {
            return Task.FromException<PayloadArchiveDeleteResult>(_exception);
        }
    }
}
