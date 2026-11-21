namespace StarterApp.Api.Infrastructure.ClientEvents;

public class ClientEvent
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? Route { get; set; }
    public string? Build { get; set; }
    public long? ElapsedMs { get; set; }
}
