using OnboardingProject.Api.Features.Cart.Models;
using OnboardingProject.Api.Features.Orders.Models;
using Xunit.Abstractions;

namespace OnboardingProject.Api.UnitTests.Features.Orders.Models;

public class LinqTests
{
    private readonly ITestOutputHelper _output;

    public LinqTests(ITestOutputHelper output) => _output = output;
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
        // Each Order has its own OrderItems list tucked inside it.
        var orders = new List<Order>
        {
            new()
            {
                OrderId = 1,
                Customer = new() { CustomerId = 1 },
                OrderItems =
                [
                    new() { OrderItemId = 1, ItemNumberId = "Widget", Quantity = 2, Price = 10.00m },
                    new() { OrderItemId = 2, ItemNumberId = "Gadget", Quantity = 1, Price = 25.00m }
                ]
            },
            new()
            {
                OrderId = 2,
                Customer = new() { CustomerId = 2 },
                OrderItems =
                [
                    new() { OrderItemId = 3, ItemNumberId = "Doohickey", Quantity = 5, Price = 3.50m }
                ]
            }
        };

        // Here is the LINQ magic: SelectMany.
        // For each Order in the list, it reaches inside and grabs the OrderItems list,
        // then smashes all those little lists together into one big flat list.
        // Without SelectMany we would get a "list of lists" — hard to work with.
        // With SelectMany we get one simple list of all OrderItems across all Orders.
        var allItems = orders.SelectMany(o => o.OrderItems).ToList();

        // Alice had 2 items + Bob had 1 item = 3 items total in the flat list.
        Assert.Equal(3, allItems.Count);

        // Make sure each item number survived the flattening and is in the final list.
        Assert.Contains(allItems, i => i is { ItemNumberId: "Widget" });
        Assert.Contains(allItems, i => i is { ItemNumberId: "Gadget" });
        Assert.Contains(allItems, i => i is { ItemNumberId: "Doohickey" });
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
        // Local helper that returns a single page from a list.
        // Skip jumps over the items before this page, Take grabs just this page.
        static List<T> GetPage<T>(List<T> source, int pageNumber, int pageSize)
        {
            return source
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        // Make 100 pretend items with OrderItemId 0 through 99.
        var items = Enumerable
            .Range(0, 100)
            .Select(i => new OrderItem { OrderItemId = i, ItemNumberId = $"Item {i}", Quantity = 1, Price = 1.00m })
            .ToList();

        // Page 1 with 10 items per page: skip nothing, take 10.
        // So we expect OrderItemId 0, 1, 2, ..., 9.
        var page1 = GetPage(items, pageNumber: 1, pageSize: 10);

        Assert.Equal(10, page1.Count);
        Assert.Equal(0, page1[0].OrderItemId);
        Assert.Equal(9, page1[^1].OrderItemId);

        // Page 2 with 10 items per page: skip the first 10, take the next 10.
        // So we expect OrderItemId 10, 11, 12, ..., 19.
        var page2 = GetPage(items, pageNumber: 2, pageSize: 10);

        Assert.Equal(10, page2.Count);
        Assert.Equal(10, page2[0].OrderItemId);
        Assert.Equal(19, page2[^1].OrderItemId);

        // Page 10 with 10 items per page: the last page has items 90-99.
        var page10 = GetPage(items, pageNumber: 10, pageSize: 10);

        Assert.Equal(10, page10.Count);
        Assert.Equal(90, page10[0].OrderItemId);
        Assert.Equal(99, page10[^1].OrderItemId);

        // Page 11 with 10 items per page: there are only 100 items, so
        // page 11 asks for items 100-109 which do not exist. We get an
        // empty list — that is how pagination says "no more pages".
        var page11 = GetPage(items, pageNumber: 11, pageSize: 10);

        Assert.Empty(page11);
    }

    // A test-only record to represent a priced item — this type does not exist
    // in the production code, but we need it to teach set operations.
    // Defined at the class level because C# does not support local types inside methods.
    private record ItemPrice(int ItemId, decimal Price);

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
            new() { CartItemId = 1, ItemId = "A", ItemName = "Widget", Quantity = 2 },
            new() { CartItemId = 2, ItemId = "B", ItemName = "Gadget", Quantity = 1 },
            new() { CartItemId = 3, ItemId = "C", ItemName = "Doohickey", Quantity = 5 },
            new() { CartItemId = 4, ItemId = "D", ItemName = "Thingamajig", Quantity = 1 }
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
        // We use CartItemId (int) for set operations — ItemId is a string
        // and would not match the int IDs in the pricing list.
        var cartIds = cartItems.Select(c => c.CartItemId);
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
    //      var expensive = FilterExpensiveItems(items).ToList();
    //    Then .Count(), .Sum(), and foreach all read from the same snapshot.
    [Fact]
    public void MultipleEnumeration_FilterRunsOncePerEnumeration()
    {
        // A local helper that returns items costing more than $5.
        // Each time the filter is evaluated, it records the item ID in a list
        // so we can COUNT how many times each item is inspected — proof that
        // the filter re-runs on every enumeration.
        var filterLog = new List<int>();

        IEnumerable<OrderItem> FilterExpensiveItems(List<OrderItem> source)
        {
            return source.Where(item =>
            {
                filterLog.Add(item.OrderItemId);
                _output.WriteLine($"Filtering {item.OrderItemId}");
                return item.Price > 5.00m;
            });
        }

        // Five items: three cheap (<=5) and two expensive (>5).
        var items = new List<OrderItem>
        {
            new() { OrderItemId = 1, ItemNumberId = "Ball", Quantity = 1, Price = 3.00m },
            new() { OrderItemId = 2, ItemNumberId = "Car", Quantity = 1, Price = 7.00m },
            new() { OrderItemId = 3, ItemNumberId = "Doll", Quantity = 1, Price = 4.00m },
            new() { OrderItemId = 4, ItemNumberId = "Plane", Quantity = 1, Price = 8.00m },
            new() { OrderItemId = 5, ItemNumberId = "Bike", Quantity = 1, Price = 6.50m }
        };

        // This does NOT run the filter yet — IEnumerable is lazy.
        // It just sets up the pipeline: "when someone asks for items,
        // check if each one is expensive."
        var expensive = FilterExpensiveItems(items);

        // First enumeration: Count() walks the whole list, running the filter
        // on every item. Expect 3 expensive items (Car=7, Plane=8, Bike=6.50).
        Assert.Equal(3, expensive.Count());

        // Second enumeration: Sum() walks the whole list AGAIN, re-running
        // the filter on every item from scratch.
        Assert.Equal(21.50m, expensive.Sum(i => i.Price));

        // Third enumeration: the foreach walks the whole list YET AGAIN.
        foreach (var item in expensive)
        {
            _output.WriteLine($"  -> {item.OrderItemId}");
        }

        // Each of the 5 items was filtered 3 times (once per enumeration),
        // so the log should have 15 entries (5 items × 3 passes).
        Assert.Equal(15, filterLog.Count);
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
            new() { OrderItemId = 1, ItemNumberId = "Ball", Quantity = 2, Price = 3.00m },     // 2 ×  $3.00 =  $6.00
            new() { OrderItemId = 2, ItemNumberId = "Car", Quantity = 1, Price = 7.00m },      // 1 ×  $7.00 =  $7.00
            new() { OrderItemId = 3, ItemNumberId = "Plane", Quantity = 3, Price = 8.00m }     // 3 ×  $8.00 = $24.00
        };                                                                                      // Total:       $37.00

        // Aggregate walks the list one item at a time. It keeps a "running
        // total" in a variable called 'acc' (short for accumulator). On the
        // first item, acc starts at 0 and becomes 0 + 6.00 = 6.00. On the
        // second item, acc = 6.00 + 7.00 = 13.00. On the third item,
        // acc = 13.00 + 24.00 = 37.00. That final value is the result.
        var total = items.Aggregate(
            0m,
            (acc, item) => acc + item.Price * item.Quantity
        );

        Assert.Equal(37.00m, total);
    }
}
