namespace OnboardingProject.Api.Features.Cart.Models;

public record ItemPrice
{
    public required string ItemId { get; set; }

    public decimal BasePrice { get; set; }

    public bool IsOnSale { get; set; }

    public decimal? SalePrice { get; set; }
}
