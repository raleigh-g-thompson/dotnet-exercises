namespace OnboardingProject.Api.Features.Orders.Models;

public class Order
{
    public int OrderId { get; init; }

    public Customer Customer { get; init; }
    
    public List<OrderItem> OrderItems { get; init; }

    public void Add(OrderItem orderItem)
    {
        OrderItems.Add(orderItem);
    }

    public void Remove(int orderItemId)
    {
        OrderItem orderItemToDelete = null;
        foreach(var orderItem in OrderItems)
        {
            if (orderItem.OrderItemId == orderItemId)
            {
                orderItemToDelete = orderItem;
                break;
            }
        }
        if (orderItemToDelete != null)
        {
            OrderItems.Remove(orderItemToDelete);
        }
    }
}
