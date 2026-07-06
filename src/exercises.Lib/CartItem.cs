namespace exercises.Lib;

public class CartItem
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    public CartItem(int itemId, int quantity)
    {
        ItemId = itemId;
        Quantity = quantity;
    }
}
