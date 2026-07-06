namespace exercises.Lib;

public class Order
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public string CustomerName { get; set; }
    public List<OrderItem> Items { get; set; }

    public decimal Total => Items.Sum(item => item.Total);

    public Order(int id, string customerName)
    {
        Id = id;
        CustomerName = customerName;
        OrderDate = DateTime.UtcNow;
        Items = [];
    }
}
