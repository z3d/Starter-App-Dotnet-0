namespace StarterApp.Application.Infrastructure.Worker;

public sealed record OrderConfirmation(
    Guid OrderId,
    string Status,
    decimal TotalIncludingGst,
    string Currency,
    string CustomerName,
    string CustomerEmail);
