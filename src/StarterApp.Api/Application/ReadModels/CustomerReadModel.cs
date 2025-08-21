namespace StarterApp.Api.Application.ReadModels;

public class CustomerReadModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; }
    public bool IsActive { get; set; }
}



