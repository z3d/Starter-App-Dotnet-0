namespace DockerLearningApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServiceBusController : ControllerBase
{
    private readonly IServiceBusService _serviceBusService;
    private readonly ILogger<ServiceBusController> _logger;

    public ServiceBusController(IServiceBusService serviceBusService, ILogger<ServiceBusController> logger)
    {
        _serviceBusService = serviceBusService;
        _logger = logger;
    }

    [HttpGet("queue-info")]
    public async Task<IActionResult> GetQueueInfo(CancellationToken cancellationToken)
    {
        try
        {
            var queueInfo = await _serviceBusService.GetQueueInfoAsync(cancellationToken);
            return Ok(queueInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving queue information");
            return StatusCode(500, new { Error = "Failed to retrieve queue information" });
        }
    }

    [HttpDelete("purge")]
    public async Task<IActionResult> PurgeQueue(CancellationToken cancellationToken)
    {
        try
        {
            var success = await _serviceBusService.PurgeQueueAsync(cancellationToken);
            
            if (success)
            {
                _logger.LogInformation("Queue purged successfully");
                return NoContent();
            }
            
            _logger.LogWarning("Failed to purge queue");
            return StatusCode(500, new { Error = "Failed to purge queue" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error purging queue");
            return StatusCode(500, new { Error = "Internal server error" });
        }
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetServiceBusHealth(CancellationToken cancellationToken)
    {
        try
        {
            var isHealthy = await _serviceBusService.IsHealthyAsync(cancellationToken);
            
            if (isHealthy)
            {
                return Ok(new { Status = "Healthy", Service = "ServiceBus" });
            }
            
            return StatusCode(503, new { Status = "Unhealthy", Service = "ServiceBus" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ServiceBus health");
            return StatusCode(503, new { Status = "Unhealthy", Service = "ServiceBus", Error = ex.Message });
        }
    }
}