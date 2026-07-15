namespace OnboardingProject.Api.Features.Cart.Models;

public class CartItem
{
    public int CartItemId { get; set; }

    public int CartId { get; set; }

    public required string ItemId { get; set; }

    public required string ItemName { get; set; }

    public int Quantity { get; set; }
}