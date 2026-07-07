namespace exercises.Lib;

public class OrderItem : IEquatable<OrderItem>
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

    public bool Equals(OrderItem? other) =>
        other is not null &&
        ItemId == other.ItemId &&
        Name == other.Name &&
        Quantity == other.Quantity &&
        UnitPrice == other.UnitPrice;

    public override bool Equals(object? obj) =>
        obj is OrderItem other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(ItemId, Name, Quantity, UnitPrice);
}
