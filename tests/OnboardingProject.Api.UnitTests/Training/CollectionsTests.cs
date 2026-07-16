using System.Diagnostics;
using OnboardingProject.Api.Features.Cart.Models;
using OnboardingProject.Api.Features.Orders.Models;
using Xunit.Abstractions;

namespace OnboardingProject.Api.UnitTests.Features.Orders.Models;

public class CollectionsTests
{
    private readonly ITestOutputHelper _output;

    public CollectionsTests(ITestOutputHelper output) => _output = output;

    // CartItem that overrides BOTH Equals and GetHashCode — the right way.
    private class CartItemWithEquality : CartItem
    {
        public CartItemWithEquality(int cartItemId, int quantity)
        {
            CartItemId = cartItemId;
            Quantity = quantity;
        }

        public override bool Equals(object? obj)
        {
            return obj is CartItemWithEquality other
                && ItemId == other.ItemId;
        }

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ItemId);
    }

    // CartItem that overrides ONLY Equals — the dangerous trap.
    private class CartItemWithEqualsOnly : CartItem
    {
        public CartItemWithEqualsOnly(int cartItemId, int quantity)
        {
            CartItemId = cartItemId;
            Quantity = quantity;
        }

        public override bool Equals(object? obj)
        {
            return obj is CartItemWithEqualsOnly other
                && ItemId == other.ItemId;
        }

        // No GetHashCode override — this is the bug.
    }

    // Custom comparer that matches CartItems by ItemId.
    private class CartItemByItemIdComparer : IEqualityComparer<CartItem>
    {
        public bool Equals(CartItem? x, CartItem? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.ItemId == y.ItemId;
        }

        public int GetHashCode(CartItem obj) => StringComparer.Ordinal.GetHashCode(obj.ItemId);
    }

    // Default HashSet uses reference equality — identical data still counts as 2 items.
    [Fact]
    public void DefaultHashSet_ReferenceEquality_TreatsIdenticalItemsAsDistinct()
    {
        var item1 = new CartItem { CartItemId = 1, ItemId = "A", ItemName = "Widget", Quantity = 2 };
        var item2 = new CartItem { CartItemId = 2, ItemId = "A", ItemName = "Widget", Quantity = 2 };

        var cart = new HashSet<CartItem> { item1, item2 };

        // Two items with the same data but different references → Count = 2.
        Assert.Equal(2, cart.Count);
        Assert.Contains(item1, cart);
    }

    // Override BOTH Equals and GetHashCode — HashSet deduplicates correctly.
    [Fact]
    public void OverridingBoth_EqualsAndGetHashCode_HashSetDeduplicatesCorrectly()
    {
        var item1 = new CartItemWithEquality(1, 2) { ItemId = "A", ItemName = "Widget" };
        var item2 = new CartItemWithEquality(2, 5) { ItemId = "A", ItemName = "Gadget" };

        var cart = new HashSet<CartItemWithEquality> { item1, item2 };

        // Same ItemId → only one item kept.
        Assert.Single(cart);

        var onlyItem = cart.First();
        Assert.Equal(1, onlyItem.CartItemId);

        // Different ItemId items coexist.
        var item3 = new CartItemWithEquality(3, 5) { ItemId = "B", ItemName = "Gadget" };
        cart.Add(item3);
        Assert.Equal(2, cart.Count);
    }

    // Override ONLY Equals — HashSet still treats them as distinct (the bug).
    [Fact]
    public void OverridingOnlyEquals_HashSetBehavesInconsistently()
    {
        var item1 = new CartItemWithEqualsOnly(1, 2) { ItemId = "A", ItemName = "Widget" };
        var item2 = new CartItemWithEqualsOnly(2, 5) { ItemId = "A", ItemName = "Gadget" };

        var cart = new HashSet<CartItemWithEqualsOnly> { item1, item2 };

        // GetHashCode is different → HashSet puts them in different buckets → Count = 2.
        Assert.Equal(2, cart.Count);

        // But direct .Equals() says they are equal — that is the inconsistency.
        Assert.True(item1.Equals(item2));
    }

    // Custom IEqualityComparer — cleanest approach, no need to modify the model.
    [Fact]
    public void CustomComparer_PassedToHashSet_DeduplicatesByItemId()
    {
        var item1 = new CartItem { CartItemId = 1, ItemId = "A", ItemName = "Widget", Quantity = 2 };
        var item2 = new CartItem { CartItemId = 2, ItemId = "A", ItemName = "Gadget", Quantity = 5 };
        var item3 = new CartItem { CartItemId = 3, ItemId = "B", ItemName = "Doohickey", Quantity = 1 };

        var cart = new HashSet<CartItem>(new CartItemByItemIdComparer())
        {
            item1,
            item2,
            item3
        };

        // item1 and item2 share ItemId "A" → deduplicated. item3 is different.
        Assert.Equal(2, cart.Count);
        Assert.Contains(item1, cart);
        Assert.Contains(item3, cart);
    }

    // List is slow for mid-list insertions and removals because it shifts elements.
    [Fact]
    public void FrequentInsertionsAndRemovals_ListIsSlowForMidOperations()
    {
        var items = new List<CartItem>();
        for (var i = 0; i < 100_000; i++)
        {
            items.Add(new CartItem { CartItemId = i, ItemId = $"Item-{i}", ItemName = $"Widget-{i}", Quantity = 1 });
        }

        const int operations = 1_000;
        var sw = Stopwatch.StartNew();

        // Insert in the middle — each one shifts ~50K elements.
        for (var i = 0; i < operations; i++)
        {
            var newItem = new CartItem { CartItemId = 100_000 + i, ItemId = $"New-{i}", ItemName = $"NewWidget-{i}", Quantity = 1 };
            items.Insert(items.Count / 2, newItem);
        }

        _output.WriteLine($"Inserted {operations} items at mid-list: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(100_000 + operations, items.Count);

        // Remove from the middle — same shifting cost.
        for (var i = 0; i < operations; i++)
        {
            items.RemoveAt(items.Count / 2);
        }

        sw.Stop();
        _output.WriteLine($"Removed {operations} items from mid-list: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(100_000, items.Count);
        _output.WriteLine($"Total time: {sw.ElapsedMilliseconds} ms");
    }

    // Dictionary lookups are O(1) — much faster than List linear search which is O(n).
    [Fact]
    public void DictionaryLookupsVsListSearching_O1BeatsOn()
    {
        var sw = Stopwatch.StartNew();

        // Build 10,000 OrderItem objects.
        var allItems = new List<OrderItem>();
        for (var i = 1; i <= 10_000; i++)
        {
            allItems.Add(new OrderItem
            {
                OrderItemId = i,
                ItemNumberId = $"SKU-{i:D5}",
                Quantity = i % 100,
                Price = i * 0.01m
            });
        }

        var list = new List<OrderItem>(allItems);
        var dict = allItems.ToDictionary(item => item.OrderItemId);

        // Pick 10,000 random IDs to look up.
        var rng = new Random(42);
        var lookupIds = new List<int>();
        for (var i = 0; i < 10_000; i++)
        {
            lookupIds.Add(rng.Next(1, 20_000));
        }

        // List linear search — O(n) per lookup.
        sw.Restart();
        var listHits = 0;
        for (var i = 0; i < lookupIds.Count; i++)
        {
            var found = list.Find(item => item.OrderItemId == lookupIds[i]);
            if (found is not null)
                listHits++;
        }

        var listMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"List search: {listMs} ms");

        // Dictionary hash lookup — O(1) per lookup.
        sw.Restart();
        var dictHits = 0;
        for (var i = 0; i < lookupIds.Count; i++)
        {
            if (dict.TryGetValue(lookupIds[i], out _))
                dictHits++;
        }

        var dictMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"Dict lookup: {dictMs} ms");

        Assert.Equal(listHits, dictHits);
        Assert.True(dictMs < listMs / 2);
    }

    // Test-only record for scenario 3 and 5.
    private record ItemPrice(int ItemId, decimal Price);

    // Picking the right collection for each job.
    [Fact]
    public void ChoosingTheRightCollection_MatchCollectionToScenario()
    {
        // Scenario 1: Unique discount codes — HashSet<string>.
        var usedCodes = new HashSet<string> { "SUMMER25", "WELCOME10", "FREESHIP" };
        Assert.Contains("SUMMER25", usedCodes);
        Assert.DoesNotContain("SAVE20", usedCodes);
        Assert.False(usedCodes.Add("SUMMER25"));
        Assert.Equal(3, usedCodes.Count);

        // Scenario 2: Process messages in order — Queue<string>.
        var messageQueue = new Queue<string>();
        messageQueue.Enqueue("OrderCreated");
        messageQueue.Enqueue("PaymentReceived");
        messageQueue.Enqueue("Shipped");
        Assert.Equal(3, messageQueue.Count);
        Assert.Equal("OrderCreated", messageQueue.Dequeue());
        Assert.Equal("PaymentReceived", messageQueue.Dequeue());
        Assert.Equal("Shipped", messageQueue.Dequeue());
        Assert.Empty(messageQueue);

        // Scenario 3: Map ItemId to ItemPrice — Dictionary<int, ItemPrice>.
        var priceCatalog = new Dictionary<int, ItemPrice>
        {
            [1] = new ItemPrice(1, 10.00m),
            [2] = new ItemPrice(2, 25.50m),
            [3] = new ItemPrice(3, 3.75m)
        };
        Assert.True(priceCatalog.TryGetValue(2, out var price));
        Assert.Equal(25.50m, price!.Price);
        Assert.False(priceCatalog.TryGetValue(99, out _));

        // Scenario 4: Read-only list — IReadOnlyList<OrderItem>.
        var internalStorage = new List<OrderItem>
        {
            new() { OrderItemId = 1, ItemNumberId = "A", Quantity = 2, Price = 10.00m },
            new() { OrderItemId = 2, ItemNumberId = "B", Quantity = 1, Price = 25.00m }
        };
        IReadOnlyList<OrderItem> readOnlyView = internalStorage;
        Assert.Equal(2, readOnlyView.Count);
        Assert.Equal("A", readOnlyView[0].ItemNumberId);

        // Scenario 5: Price history with indexed access — List<ItemPrice>.
        var priceHistory = new List<ItemPrice>
        {
            new(1, 10.00m),
            new(1, 12.50m),
            new(1, 9.99m),
            new(1, 15.00m)
        };
        Assert.Equal(10.00m, priceHistory[0].Price);
        Assert.Equal(15.00m, priceHistory[^1].Price);
        Assert.Equal(4, priceHistory.Count);
    }
}
