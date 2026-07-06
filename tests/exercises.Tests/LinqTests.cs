using exercises.Lib;

namespace exercises.Tests;

public class LinqTests
{
    // This test teaches how SelectMany works.
    // Imagine you have several shopping carts (Orders), and each shopping cart
    // holds a list of items (OrderItems). You want to dump ALL items from ALL
    // carts onto one big table so you can count them together. SelectMany does
    // exactly that — it takes a list of lists and flattens them into one list.
    [Fact]
    public void FlatteningNestedCollections_SelectMany_ReturnsAllOrderItems()
    {
        // First we build two pretend shopping orders, like two people ordering stuff.
        // Alice buys a Widget (qty 2) and a Gadget (qty 1).
        // Bob buys a Doohickey (qty 5).
        // Each Order has its own Items list tucked inside it.
        var orders = new List<Order>
        {
            new(1, "Alice")
            {
                Items =
                [
                    new(1, "Widget", 2, 10.00m),
                    new(2, "Gadget", 1, 25.00m)
                ]
            },
            new(2, "Bob")
            {
                Items =
                [
                    new(3, "Doohickey", 5, 3.50m)
                ]
            }
        };

        // Here is the LINQ magic: SelectMany.
        // For each Order in the list, it reaches inside and grabs the Items list,
        // then smashes all those little lists together into one big flat list.
        // Without SelectMany we would get a "list of lists" — hard to work with.
        // With SelectMany we get one simple list of all OrderItems across all Orders.
        var allItems = orders.SelectMany(o => o.Items).ToList();

        // Alice had 2 items + Bob had 1 item = 3 items total in the flat list.
        Assert.Equal(3, allItems.Count);

        // Make sure each item name survived the flattening and is in the final list.
        Assert.Contains(allItems, i => i is { Name: "Widget" });
        Assert.Contains(allItems, i => i is { Name: "Gadget" });
        Assert.Contains(allItems, i => i is { Name: "Doohickey" });
    }

    // This test teaches how paging (pagination) works with Skip and Take.
    // Imagine you have a huge toy catalog with 100 pages. You do not want to
    // look at ALL of them at once — that would take forever! Instead you look
    // at them one page at a time. Page 1 shows toys 1-10, page 2 shows toys
    // 11-20, and so on. That is exactly what Skip and Take do: Skip jumps
    // over the items you already saw, Take grabs the next handful.
    [Fact]
    public void Pagination_SkipAndTake_ReturnsCorrectPage()
    {
        // Make 100 pretend items with ItemId 0 through 99.
        var items = Enumerable
            .Range(0, 100)
            .Select(i => new OrderItem(i, $"Item {i}", 1, 1.00m))
            .ToList();

        // Page 1 with 10 items per page: skip nothing, take 10.
        // So we expect ItemId 0, 1, 2, ..., 9.
        var page1 = PaginationHelper.GetPage(items, pageNumber: 1, pageSize: 10);

        Assert.Equal(10, page1.Count);
        Assert.Equal(0, page1[0].ItemId);
        Assert.Equal(9, page1[^1].ItemId);

        // Page 2 with 10 items per page: skip the first 10, take the next 10.
        // So we expect ItemId 10, 11, 12, ..., 19.
        var page2 = PaginationHelper.GetPage(items, pageNumber: 2, pageSize: 10);

        Assert.Equal(10, page2.Count);
        Assert.Equal(10, page2[0].ItemId);
        Assert.Equal(19, page2[^1].ItemId);

        // Page 10 with 10 items per page: the last page has items 90-99.
        var page10 = PaginationHelper.GetPage(items, pageNumber: 10, pageSize: 10);

        Assert.Equal(10, page10.Count);
        Assert.Equal(90, page10[0].ItemId);
        Assert.Equal(99, page10[^1].ItemId);

        // Page 11 with 10 items per page: there are only 100 items, so
        // page 11 asks for items 100-109 which do not exist. We get an
        // empty list — that is how pagination says "no more pages".
        var page11 = PaginationHelper.GetPage(items, pageNumber: 11, pageSize: 10);

        Assert.Empty(page11);
    }

    // This test teaches set operations: Intersect, Except, and Union.
    // Imagine you have two lists of toy IDs. One list is the toys in your
    // shopping cart. The other list is the toys that have a price tag.
    //   - Intersect: toys that are in BOTH the cart AND have a price.
    //   - Except (cart minus pricing): toys in the cart that do NOT have a price.
    //   - Union: all unique toy IDs from both lists combined, with no repeats.
    [Fact]
    public void SetOperations_IntersectExceptUnion_ReturnsCorrectIds()
    {
        // The IDs of items sitting in someone's shopping cart.
        // They want to buy items 1, 2, 3, and 4.
        var cartItems = new List<CartItem>
        {
            new(1, 2),
            new(2, 1),
            new(3, 5),
            new(4, 1)
        };

        // The IDs of items that have a price in the system.
        // Only items 1, 2, and 3 have prices — item 4 is missing.
        var pricing = new List<ItemPrice>
        {
            new(1, 10.00m),
            new(2, 25.00m),
            new(3, 3.50m)
        };

        // Pull out just the ID numbers so we can compare plain lists.
        var cartIds = cartItems.Select(c => c.ItemId);
        var pricingIds = pricing.Select(p => p.ItemId);

        // Intersect: which IDs are in BOTH the cart AND the pricing list?
        // cart = {1,2,3,4}, pricing = {1,2,3} → Intersect = {1,2,3}
        var inCartAndPriced = cartIds.Intersect(pricingIds).ToList();

        Assert.Equal(3, inCartAndPriced.Count);
        Assert.Contains(1, inCartAndPriced);
        Assert.Contains(2, inCartAndPriced);
        Assert.Contains(3, inCartAndPriced);

        // Except: which IDs are in the cart but NOT in the pricing list?
        // cart = {1,2,3,4}, pricing = {1,2,3} → Except = {4}
        var inCartOnly = cartIds.Except(pricingIds).ToList();

        Assert.Single(inCartOnly);
        Assert.Equal(4, inCartOnly[0]);

        // Union: a combined list of every unique ID from both lists with no
        // duplicates. cart = {1,2,3,4}, pricing = {1,2,3} → Union = {1,2,3,4}
        var allUniqueIds = cartIds.Union(pricingIds).ToList();

        Assert.Equal(4, allUniqueIds.Count);
        Assert.Contains(1, allUniqueIds);
        Assert.Contains(2, allUniqueIds);
        Assert.Contains(3, allUniqueIds);
        Assert.Contains(4, allUniqueIds);
    }

    // This test demonstrates the "multiple enumeration" problem with IEnumerable.
    // Imagine a lazy filter that checks each toy and says "Filtering toy X" every
    // time it looks at one. If you ask for the COUNT, then the SUM, then print
    // each toy individually — each of those passes through the ENTIRE list again,
    // re-running the filter from scratch each time. That is wasteful!
    // The fix is to call .ToList() once and reuse the list instead.
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: How many times does "Filtering" print? Why is it more than you might expect?
    // A: It prints 15 times (5 items × 3 passes). Most people expect it to print
    //    5 times — once per item when the items are first filtered. But IEnumerable
    //    does not remember results. Each time you ask for the data (Count, Sum,
    //    foreach) it re-runs the filter from scratch because it is like a conveyor
    //    belt with no bucket at the end to catch the output.
    //
    // Q: What is the performance implication?
    // A: If the filter is expensive (slow database query, complex math, reading a
    //    file), running it 3× instead of 1× can triple your work. In real code with
    //    thousands of items and multiple loops, this can turn a fast program into a
    //    slow one without you realizing it.
    //
    // Q: How would you fix it?
    // A: Call .ToList() or .ToArray() once right after the filter to capture the
    //    results into a real list. A list is like a bucket — it holds the filtered
    //    items so you can look at them over and over without re-running the filter.
    //    Example:
    //      var expensive = FilterHelper.FilterExpensiveItems(items).ToList();
    //    Then .Count(), .Sum(), and foreach all read from the same snapshot.
    [Fact]
    public void MultipleEnumeration_FilterRunsOncePerEnumeration()
    {
        // Five items: three cheap (<=5) and two expensive (>5).
        var items = new List<OrderItem>
        {
            new(1, "Ball", 1, 3.00m),
            new(2, "Car", 1, 7.00m),
            new(3, "Doll", 1, 4.00m),
            new(4, "Plane", 1, 8.00m),
            new(5, "Bike", 1, 6.50m)
        };

        // Capture everything printed to the console so we can count how many
        // times each item was filtered.
        var consoleOut = new StringWriter();
        Console.SetOut(consoleOut);

        // This does NOT run the filter yet — IEnumerable is lazy.
        // It just sets up the pipeline: "when someone asks for items,
        // check if each one is expensive."
        var expensive = FilterHelper.FilterExpensiveItems(items);

        // First enumeration: Count() walks the whole list, running the filter
        // on every item. Expect 2 expensive items (Car=7, Plane=8, Bike=6.50).
        Assert.Equal(3, expensive.Count());

        // Second enumeration: Sum() walks the whole list AGAIN, re-running
        // the filter on every item from scratch.
        Assert.Equal(21.50m, expensive.Sum(i => i.UnitPrice));

        // Third enumeration: the foreach walks the whole list YET AGAIN.
        foreach (var item in expensive)
        {
            Console.WriteLine($"  -> {item.ItemId}");
        }

        // Each of the 5 items was filtered 3 times (once per enumeration),
        // so the console should say "Filtering 1" through "Filtering 5"
        // three times each = 15 lines, plus the 3 print lines from the foreach.
        var output = consoleOut.ToString();
        var filterCount = output.Split('\n').Count(l => l.StartsWith("Filtering "));

        Assert.Equal(15, filterCount);
    }

    // This test teaches how Aggregate works (also called "fold" or "reduce").
    // Imagine you have a pile of coins from different toys and you want to
    // know how much money you have total. Instead of using a foreach loop
    // (which is like counting coins one-by-one with your fingers), Aggregate
    // lets you say: "Start with zero, then for each item add its value to
    // the running total." It is a general-purpose way to boil a list down
    // to a single number — way more flexible than Sum() because you can do
    // any kind of accumulation you want.
    [Fact]
    public void CustomAggregation_Aggregate_ComputesTotalValue()
    {
        // Three items with different prices and quantities.
        var items = new List<OrderItem>
        {
            new(1, "Ball", 2, 3.00m),     // 2 ×  $3.00 =  $6.00
            new(2, "Car", 1, 7.00m),      // 1 ×  $7.00 =  $7.00
            new(3, "Plane", 3, 8.00m)     // 3 ×  $8.00 = $24.00
        };                                 // Total:       $37.00

        // Aggregate walks the list one item at a time. It keeps a "running
        // total" in a variable called 'acc' (short for accumulator). On the
        // first item, acc starts at 0 and becomes 0 + 6.00 = 6.00. On the
        // second item, acc = 6.00 + 7.00 = 13.00. On the third item,
        // acc = 13.00 + 24.00 = 37.00. That final value is the result.
        var total = items.Aggregate(
            0m,
            (acc, item) => acc + item.UnitPrice * item.Quantity
        );

        Assert.Equal(37.00m, total);
    }
}
