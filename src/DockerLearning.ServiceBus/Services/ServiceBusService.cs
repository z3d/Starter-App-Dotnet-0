namespace DockerLearning.ServiceBus.Services;

public class ServiceBusService : IServiceBusService, IDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<ServiceBusService> _logger;
    private bool _disposed;

    public ServiceBusService(IOptions<ServiceBusOptions> options, ILogger<ServiceBusService> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        try
        {
            _client = new ServiceBusClient(_options.ConnectionString);
            _sender = _client.CreateSender(_options.QueueName);
            _adminClient = new ServiceBusAdministrationClient(_options.ConnectionString);
            
            _logger.LogInformation("ServiceBus client initialized for queue: {QueueName}", _options.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ServiceBus client");
            throw;
        }
    }

    public async Task<bool> SendMessageAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var envelope = new MessageEnvelope<T> { Data = message };
            var json = JsonSerializer.Serialize(envelope);
            var serviceBusMessage = new ServiceBusMessage(json)
            {
                MessageId = envelope.Id,
                CorrelationId = envelope.CorrelationId,
                ContentType = "application/json"
            };

            await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
            
            _logger.LogInformation("Message sent successfully. MessageId: {MessageId}, Type: {MessageType}", 
                envelope.Id, envelope.MessageType);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message of type {MessageType}", typeof(T).Name);
            return false;
        }
    }

    public async Task<bool> SendMessagesAsync<T>(IEnumerable<T> messages, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var serviceBusMessages = messages.Select(message =>
            {
                var envelope = new MessageEnvelope<T> { Data = message };
                var json = JsonSerializer.Serialize(envelope);
                return new ServiceBusMessage(json)
                {
                    MessageId = envelope.Id,
                    CorrelationId = envelope.CorrelationId,
                    ContentType = "application/json"
                };
            }).ToList();

            using var messageBatch = await _sender.CreateMessageBatchAsync(cancellationToken);
            
            foreach (var message in serviceBusMessages)
            {
                if (!messageBatch.TryAddMessage(message))
                {
                    _logger.LogWarning("Message batch is full, sending current batch and creating new one");
                    await _sender.SendMessagesAsync(messageBatch, cancellationToken);
                    messageBatch.Dispose();
                    
                    var newBatch = await _sender.CreateMessageBatchAsync(cancellationToken);
                    newBatch.TryAddMessage(message);
                    await _sender.SendMessagesAsync(newBatch, cancellationToken);
                    newBatch.Dispose();
                }
            }

            if (messageBatch.Count > 0)
            {
                await _sender.SendMessagesAsync(messageBatch, cancellationToken);
            }

            _logger.LogInformation("Batch of {MessageCount} messages sent successfully", serviceBusMessages.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message batch of type {MessageType}", typeof(T).Name);
            return false;
        }
    }

    public async Task<QueueInfo> GetQueueInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var queueRuntimeProperties = await _adminClient.GetQueueRuntimePropertiesAsync(_options.QueueName, cancellationToken);
            
            return new QueueInfo
            {
                QueueName = _options.QueueName,
                ActiveMessageCount = queueRuntimeProperties.Value.ActiveMessageCount,
                DeadLetterMessageCount = queueRuntimeProperties.Value.DeadLetterMessageCount,
                ScheduledMessageCount = queueRuntimeProperties.Value.ScheduledMessageCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue info for {QueueName}", _options.QueueName);
            throw;
        }
    }

    public async Task<bool> PurgeQueueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var receiver = _client.CreateReceiver(_options.QueueName);
            var messages = new List<ServiceBusReceivedMessage>();
            
            do
            {
                messages = (await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1), cancellationToken)).ToList();
                
                foreach (var message in messages)
                {
                    await receiver.CompleteMessageAsync(message, cancellationToken);
                }
                
            } while (messages.Any());

            await receiver.DisposeAsync();
            
            _logger.LogInformation("Queue {QueueName} purged successfully", _options.QueueName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge queue {QueueName}", _options.QueueName);
            return false;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _adminClient.GetQueueRuntimePropertiesAsync(_options.QueueName, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ServiceBus health check failed");
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _sender?.DisposeAsync().AsTask().Wait();
            _client?.DisposeAsync().AsTask().Wait();
            // ServiceBusAdministrationClient doesn't implement IDisposable
            _disposed = true;
        }
    }
}