namespace exercises.Lib;

public class ItemPrice
{
    public int ItemId { get; set; }
    public decimal Price { get; set; }

    public ItemPrice(int itemId, decimal price)
    {
        ItemId = itemId;
        Price = price;
    }
}
