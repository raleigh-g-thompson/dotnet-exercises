namespace OnboardingProject.Api.Features.Orders.Models;

public class Customer
{
    public int CustomerId { get; init; }

    public bool IsPlatinumCustomer { get; set; }

    public bool IsPlusCustomer { get; set; }
}