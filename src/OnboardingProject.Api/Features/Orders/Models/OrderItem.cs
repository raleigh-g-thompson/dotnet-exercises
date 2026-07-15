namespace OnboardingProject.Api.Features.Orders.Models;

public record OrderItem
{
    public int OrderItemId { get; init; }
    
    public int OrderId { get; init; }

    public string ItemNumberId { get; init; }

    public int Quantity { get; init; }

    public decimal Price { get; init; }
}
