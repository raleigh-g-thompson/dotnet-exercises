namespace exercises.Lib;

public class OrderItem
{
    public int ItemId { get; set; }
    public string Name { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal Total => Quantity * UnitPrice;

    public OrderItem(int itemId, string name, int quantity, decimal unitPrice)
    {
        ItemId = itemId;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
