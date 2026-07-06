namespace exercises.Lib;

public static class FilterHelper
{
    public static IEnumerable<OrderItem> FilterExpensiveItems(List<OrderItem> source)
    {
        return source.Where(item =>
        {
            Console.WriteLine($"Filtering {item.ItemId}");
            return item.UnitPrice > 5.00m;
        });
    }
}
