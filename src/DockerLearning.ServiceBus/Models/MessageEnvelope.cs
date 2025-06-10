namespace DockerLearning.ServiceBus.Models;

public class MessageEnvelope<T> where T : class
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MessageType { get; set; } = typeof(T).Name;
    public T Data { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class ProductMessage
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Action { get; set; } = string.Empty; // Created, Updated, Deleted
}