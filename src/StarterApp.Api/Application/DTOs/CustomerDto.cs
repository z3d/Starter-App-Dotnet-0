namespace StarterApp.Api.Application.DTOs;

public class CustomerDto
{
    public int Id { get; set; }

    [Required]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(320, ErrorMessage = "Email cannot exceed 320 characters")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; set; } = string.Empty;

    public DateTime DateCreated { get; set; }

    public bool IsActive { get; set; }
}



