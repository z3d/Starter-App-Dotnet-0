namespace DockerLearningApi.BackgroundServices;

public class ProductMessageHandler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductMessageHandler> _logger;
    private readonly ServiceBusOptions _serviceBusOptions;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public ProductMessageHandler(
        IServiceProvider serviceProvider, 
        ILogger<ProductMessageHandler> logger,
        IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _serviceBusOptions = serviceBusOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _client = new ServiceBusClient(_serviceBusOptions.ConnectionString);
            _processor = _client.CreateProcessor(_serviceBusOptions.QueueName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _serviceBusOptions.MaxConcurrentCalls,
                AutoCompleteMessages = _serviceBusOptions.EnableAutoComplete,
                PrefetchCount = _serviceBusOptions.PrefetchCount,
                MaxAutoLockRenewalDuration = TimeSpan.FromSeconds(_serviceBusOptions.MaxAutoLockRenewalDuration)
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            
            _logger.LogInformation("ProductMessageHandler started processing messages from queue: {QueueName}", 
                _serviceBusOptions.QueueName);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProductMessageHandler");
        }
        finally
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync();
                await _processor.DisposeAsync();
            }
            
            if (_client != null)
            {
                await _client.DisposeAsync();
            }
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageBody = args.Message.Body.ToString();
        var messageId = args.Message.MessageId;
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            
            _logger.LogInformation("Processing message: {MessageId}", messageId);

            var envelope = JsonSerializer.Deserialize<MessageEnvelope<ProductMessage>>(messageBody);
            if (envelope?.Data == null)
            {
                _logger.LogWarning("Invalid message format: {MessageId}", messageId);
                await args.DeadLetterMessageAsync(args.Message, "Invalid message format");
                return;
            }

            var productMessage = envelope.Data;
            
            switch (productMessage.Action.ToUpperInvariant())
            {
                case "CREATED":
                    await HandleProductCreated(productMessage, mediator);
                    break;
                case "UPDATED":
                    await HandleProductUpdated(productMessage, mediator);
                    break;
                case "DELETED":
                    await HandleProductDeleted(productMessage, mediator);
                    break;
                default:
                    _logger.LogWarning("Unknown action: {Action} for message: {MessageId}", 
                        productMessage.Action, messageId);
                    break;
            }

            await args.CompleteMessageAsync(args.Message);
            
            _logger.LogInformation("Successfully processed message: {MessageId}, Action: {Action}, ProductId: {ProductId}", 
                messageId, productMessage.Action, productMessage.ProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {MessageId}", messageId);
            
            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message, "Max delivery count exceeded", ex.Message);
                _logger.LogWarning("Message {MessageId} moved to dead letter queue after {DeliveryCount} attempts", 
                    messageId, args.Message.DeliveryCount);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private async Task HandleProductCreated(ProductMessage productMessage, IMediator mediator)
    {
        _logger.LogInformation("Processing product created event for ProductId: {ProductId}, Name: {Name}, Price: {Price}", 
            productMessage.ProductId, productMessage.Name, productMessage.Price);
        
        // Example integrations that would happen in real scenarios:
        // 1. Send to analytics system
        _logger.LogInformation("📊 Sending product creation analytics for {ProductName}", productMessage.Name);
        
        // 2. Update search index
        _logger.LogInformation("🔍 Updating search index for product {ProductId}", productMessage.ProductId);
        
        // 3. Send notifications
        _logger.LogInformation("📧 Sending new product notifications for {ProductName}", productMessage.Name);
        
        // 4. Update inventory system
        _logger.LogInformation("📦 Notifying inventory system of new product {ProductId}", productMessage.ProductId);
        
        await Task.Delay(100); // Simulate processing time
        _logger.LogInformation("✅ Completed processing product creation event for {ProductId}", productMessage.ProductId);
    }

    private async Task HandleProductUpdated(ProductMessage productMessage, IMediator mediator)
    {
        _logger.LogInformation("Processing product updated event for ProductId: {ProductId}, Name: {Name}, Price: {Price}", 
            productMessage.ProductId, productMessage.Name, productMessage.Price);
        
        // Example integrations:
        // 1. Update cache
        _logger.LogInformation("🔄 Invalidating cache for product {ProductId}", productMessage.ProductId);
        
        // 2. Update search index
        _logger.LogInformation("🔍 Updating search index for product changes {ProductId}", productMessage.ProductId);
        
        // 3. Price monitoring alerts
        _logger.LogInformation("💰 Checking price change alerts for {ProductName}: ${Price}", productMessage.Name, productMessage.Price);
        
        // 4. Sync with external systems
        _logger.LogInformation("🔗 Syncing product updates to external systems for {ProductId}", productMessage.ProductId);
        
        await Task.Delay(100);
        _logger.LogInformation("✅ Completed processing product update event for {ProductId}", productMessage.ProductId);
    }

    private async Task HandleProductDeleted(ProductMessage productMessage, IMediator mediator)
    {
        _logger.LogInformation("Processing product deleted event for ProductId: {ProductId}, Name: {Name}", 
            productMessage.ProductId, productMessage.Name);
        
        // Example cleanup integrations:
        // 1. Remove from cache
        _logger.LogInformation("🗑️ Removing product {ProductId} from cache", productMessage.ProductId);
        
        // 2. Remove from search index
        _logger.LogInformation("🔍 Removing product {ProductId} from search index", productMessage.ProductId);
        
        // 3. Archive analytics data
        _logger.LogInformation("📊 Archiving analytics data for {ProductName}", productMessage.Name);
        
        // 4. Cleanup related data
        _logger.LogInformation("🧹 Cleaning up related data for product {ProductId}", productMessage.ProductId);
        
        // 5. Notify dependent systems
        _logger.LogInformation("📢 Notifying dependent systems of product {ProductId} deletion", productMessage.ProductId);
        
        await Task.Delay(100);
        _logger.LogInformation("✅ Completed processing product deletion event for {ProductId}", productMessage.ProductId);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error occurred in message processor. Source: {ErrorSource}, EntityPath: {EntityPath}", 
            args.ErrorSource, args.EntityPath);
        
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ProductMessageHandler is stopping");
        
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
        
        await base.StopAsync(cancellationToken);
    }
}