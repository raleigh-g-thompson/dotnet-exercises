using System.Diagnostics;
using OnboardingProject.Api.Features.Cart.Models;
using OnboardingProject.Api.Features.Orders.Models;
using Xunit.Abstractions;

namespace OnboardingProject.Api.UnitTests.Features.Orders.Models;

public class CollectionsTests
{
    private readonly ITestOutputHelper _output;

    public CollectionsTests(ITestOutputHelper output) => _output = output;

    // A CartItem subclass that overrides BOTH Equals and GetHashCode.
    // When you override both, the HashSet can correctly identify two objects
    // as "the same thing" even if they are different objects in memory.
    // This is the RIGHT way to make custom equality work.
    private class CartItemWithEquality : CartItem
    {
        public CartItemWithEquality(int cartItemId, int quantity)
        {
            CartItemId = cartItemId;
            Quantity = quantity;
        }

        // Equals checks if the ItemId matches — two items are "equal"
        // if they represent the same product, regardless of CartItemId.
        public override bool Equals(object? obj)
        {
            return obj is CartItemWithEquality other
                && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal);
        }

        // GetHashCode MUST match Equals — if two items are "equal" by Equals,
        // they MUST return the same hash code. This is the #1 rule of GetHashCode.
        // If you break this rule, dictionaries and HashSets break silently.
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ItemId);
    }

    // A CartItem subclass that overrides ONLY Equals — no GetHashCode override.
    // This is the DANGEROUS pitfall: Equals says "these are the same" but
    // GetHashCode says "these are different." The HashSet uses GetHashCode first
    // to decide which bucket to look in, so two "equal" items can end up in
    // different buckets and the HashSet thinks they are distinct.
    private class CartItemWithEqualsOnly : CartItem
    {
        public CartItemWithEqualsOnly(int cartItemId, int quantity)
        {
            CartItemId = cartItemId;
            Quantity = quantity;
        }

        // Equals matches on ItemId — same logic as CartItemWithEquality.
        public override bool Equals(object? obj)
        {
            return obj is CartItemWithEqualsOnly other
                && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal);
        }

        // NO GetHashCode override! This is the bug.
        // GetHashCode falls back to System.Object's default, which returns
        // a unique number for each object instance. So even though Equals
        // says two items are equal, GetHashCode says they are different,
        // and the HashSet puts them in separate buckets.
    }

    // A custom comparer that matches CartItems by ItemId.
    // This is the CLEANEST approach: you do not need to modify CartItem at all.
    // You just create a small helper class that implements IEqualityComparer<T>
    // and pass it to the HashSet constructor. The HashSet then uses YOUR rules
    // to decide what counts as "equal."
    private class CartItemByItemIdComparer : IEqualityComparer<CartItem>
    {
        public bool Equals(CartItem? x, CartItem? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.ItemId, y.ItemId, StringComparison.Ordinal);
        }

        // Same rule: if Equals matches on ItemId, GetHashCode MUST match on ItemId.
        public int GetHashCode(CartItem obj) => StringComparer.Ordinal.GetHashCode(obj.ItemId);
    }

    // This test shows what happens when you do NOT override Equals or GetHashCode.
    // CartItem is a regular C# class, so it uses REFERENCE equality by default.
    // That means two CartItem objects with the exact same ItemId and ItemName
    // are still considered DIFFERENT because they live at different spots in
    // memory. It is like having two identical-looking toy boxes — C# treats
    // them as separate boxes because they are separate objects.
    [Fact]
    public void DefaultHashSet_ReferenceEquality_TreatsIdenticalItemsAsDistinct()
    {
        var item1 = new CartItem { CartItemId = 1, ItemId = "A", ItemName = "Widget", Quantity = 2 };
        var item2 = new CartItem { CartItemId = 2, ItemId = "A", ItemName = "Widget", Quantity = 2 };

        // HashSet uses GetHashCode() first to find the right "bucket,"
        // then Equals() to confirm the match. With the default behavior:
        //   GetHashCode() → different numbers (each object gets a unique ID)
        //   Equals()      → false (reference equality — different objects)
        // So the HashSet puts them in different buckets and treats them
        // as two separate items.
        var cart = new HashSet<CartItem> { item1, item2 };

        // Two items with the same data but different references → Count = 2.
        // This is usually NOT what you want in a shopping cart!
        _output.WriteLine($"item1 == item2: {ReferenceEquals(item1, item2)}");
        _output.WriteLine($"item1.GetHashCode(): {item1.GetHashCode()}");
        _output.WriteLine($"item2.GetHashCode(): {item2.GetHashCode()}");
        _output.WriteLine($"HashSet count: {cart.Count}");

        Assert.Equal(2, cart.Count);
        Assert.Contains(item1, cart);
    }

    // This test shows the RIGHT way to make custom equality work: override
    // BOTH Equals and GetHashCode together. When both are consistent, the
    // HashSet correctly identifies two objects as "the same thing" based
    // on the ItemId property.
    //
    // The golden rule: if you override Equals, you MUST override GetHashCode.
    // If you do not, hash-based collections (HashSet, Dictionary, etc.)
    // will silently break — they rely on the contract that two equal
    // objects MUST have the same hash code.
    [Fact]
    public void OverridingBoth_EqualsAndGetHashCode_HashSetDeduplicatesCorrectly()
    {
        // CartItemId and Quantity go in the constructor; ItemId and ItemName
        // are required members so they go in the object initializer.
        var item1 = new CartItemWithEquality(1, 2) { ItemId = "A", ItemName = "Widget" };
        var item2 = new CartItemWithEquality(2, 5) { ItemId = "A", ItemName = "Gadget" };

        // Both overrides are in place:
        //   GetHashCode() → same number (both hash by ItemId = "A")
        //   Equals()      → true (both check ItemId == "A")
        // So the HashSet puts them in the SAME bucket and confirms the
        // match with Equals. It keeps only one.
        var cart = new HashSet<CartItemWithEquality> { item1, item2 };

        _output.WriteLine($"item1.ItemId: {item1.ItemId}, item2.ItemId: {item2.ItemId}");
        _output.WriteLine($"item1.GetHashCode(): {item1.GetHashCode()}");
        _output.WriteLine($"item2.GetHashCode(): {item2.GetHashCode()}");
        _output.WriteLine($"HashSet count: {cart.Count}");

        // Same ItemId → treated as equal → only one item kept.
        Assert.Single(cart);

        // The surviving item should be the FIRST one added (item1).
        var onlyItem = cart.First();
        Assert.Equal(1, onlyItem.CartItemId);
        Assert.Equal("Widget", onlyItem.ItemName);

        // Different ItemId items DO coexist in the same set.
        var item3 = new CartItemWithEquality(3, 5) { ItemId = "B", ItemName = "Gadget" };
        cart.Add(item3);
        Assert.Equal(2, cart.Count);
    }

    // This test shows the DANGEROUS trap: overriding Equals WITHOUT overriding
    // GetHashCode. The Equals method says "these two items are equal" but
    // GetHashCode gives them different numbers.
    //
    // The HashSet uses GetHashCode to decide which bucket to look in. Since
    // the two items have different hash codes, they go into different buckets.
    // The HashSet never even calls Equals to check — it already thinks they
    // are in different buckets, so it just adds both.
    //
    // This is why the rule exists: if Equals says two objects are equal,
    // GetHashCode MUST return the same value for both. Breaking this rule
    // leads to silent, hard-to-find bugs.
    [Fact]
    public void OverridingOnlyEquals_HashSetBehavesInconsistently()
    {
        var item1 = new CartItemWithEqualsOnly(1, 2) { ItemId = "A", ItemName = "Widget" };
        var item2 = new CartItemWithEqualsOnly(2, 5) { ItemId = "A", ItemName = "Gadget" };

        // Equals()  → true (matches on ItemId = "A")
        // GetHashCode() → DIFFERENT (falls back to System.Object default)
        //
        // The HashSet checks GetHashCode first:
        //   item1.GetHashCode() = 48291038  (some random number)
        //   item2.GetHashCode() = 59102847  (a different random number)
        // Different hashes → different buckets → HashSet adds both.
        //
        // But Equals says they are equal! This is the inconsistency.
        var cart = new HashSet<CartItemWithEqualsOnly> { item1, item2 };

        _output.WriteLine($"item1.GetHashCode(): {item1.GetHashCode()}");
        _output.WriteLine($"item2.GetHashCode(): {item2.GetHashCode()}");
        _output.WriteLine($"Equals: {item1.Equals(item2)}");
        _output.WriteLine($"HashSet count: {cart.Count}");

        // Despite Equals returning true, the HashSet has 2 items because
        // GetHashCode sent them to different buckets. This is the bug.
        Assert.Equal(2, cart.Count);

        // HOWEVER — Equals does work if you ask the object directly.
        // This is the sneaky part: the bug only shows up with hash-based
        // collections, not with plain .Equals() calls.
        Assert.True(item1.Equals(item2));
    }

    // This test shows the CLEANEST solution: a custom IEqualityComparer<T>.
    // Instead of modifying CartItem, you create a small comparer class and
    // pass it to the HashSet constructor. The HashSet then uses YOUR rules
    // instead of the object's default Equals/GetHashCode.
    //
    // This is the preferred approach when:
    //   1. You cannot modify the original class (it is a library type).
    //   2. You want different equality rules in different situations.
    //   3. You want to keep the original class's equality behavior intact.
    [Fact]
    public void CustomComparer_PassedToHashSet_DeduplicatesByItemId()
    {
        var item1 = new CartItem { CartItemId = 1, ItemId = "A", ItemName = "Widget", Quantity = 2 };
        var item2 = new CartItem { CartItemId = 2, ItemId = "A", ItemName = "Gadget", Quantity = 5 };
        var item3 = new CartItem { CartItemId = 3, ItemId = "B", ItemName = "Doohickey", Quantity = 1 };

        // Pass our custom comparer to the HashSet constructor.
        // The HashSet now uses CartItemByItemIdComparer instead of
        // CartItem's default Equals/GetHashCode. The comparer matches
        // by ItemId and hashes by ItemId — consistent and correct.
        var cart = new HashSet<CartItem>(new CartItemByItemIdComparer())
        {
            item1,
            item2,
            item3
        };

        _output.WriteLine($"item1.ItemId: {item1.ItemId}, item2.ItemId: {item2.ItemId}");
        _output.WriteLine($"Comparer GetHashCode(item1): {new CartItemByItemIdComparer().GetHashCode(item1)}");
        _output.WriteLine($"Comparer GetHashCode(item2): {new CartItemByItemIdComparer().GetHashCode(item2)}");
        _output.WriteLine($"HashSet count: {cart.Count}");

        // item1 and item2 have the same ItemId ("A") → deduplicated.
        // item3 has a different ItemId ("B") → kept as a second entry.
        Assert.Equal(2, cart.Count);
        Assert.Contains(item1, cart);
        Assert.Contains(item3, cart);

        // item2 was deduplicated because ItemId = "A" matches item1.
        // The surviving item is item1 (the first one added).
        var firstItem = cart.First();
        Assert.Equal(1, firstItem.CartItemId);
        Assert.Equal("Widget", firstItem.ItemName);
    }

    // This test shows why List<T> is BAD for frequent mid-list insertions and
    // removals. A List<T> is backed by an array — a single block of memory
    // where elements sit side by side, one after another, like kids standing
    // in a straight line.
    //
    // When you INSERT a new kid in the middle of the line, every kid after
    // that spot has to scoot over to make room. When you REMOVE a kid from
    // the middle, every kid after that spot has to scoot back to fill the gap.
    // With 100,000 kids in line, inserting in the middle means ~50,000 kids
    // have to move. That is O(n) work — the bigger the list, the slower
    // each insert/remove gets.
    //
    // This is why List<T> is great for:
    //   - Adding/removing at the END (O(1) amortized — no shifting needed)
    //   - Reading by index (O(1) — direct memory access)
    // But terrible for:
    //   - Inserting/removing in the MIDDLE or BEGINNING (O(n) — shifting)
    //
    // For frequent mid-list mutations, use LinkedList<T> (O(1) at any position)
    // or HashSet<T> (O(1) add/remove, but unordered).
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: Why are middle insertions and removals slow on a List<T>?
    //    Think about what has to happen internally when you insert at position
    //    500 in a list of 100,000 items.
    // A: A List<T> is backed by a single contiguous array in memory — think
    //    of it as a row of numbered mailboxes all next to each other. To INSERT
    //    at position 500, the list must:
    //      1. Make room by shifting EVERY item from position 500 onward one
    //         spot to the right (positions 500→501, 501→502, ..., 99999→100000).
    //      2. That is 99,500 elements being copied in memory.
    //    To REMOVE at position 500, the reverse happens — every item after
    //    position 500 shifts LEFT to fill the gap (501→500, 502→501, ...).
    //    Both operations are O(n) — the cost grows linearly with the list
    //    size. A list of 200,000 items would be ~2× slower than 100,000.
    //
    // Q: Research what other collection type in .NET is specifically designed
    //    for fast insertions and removals at arbitrary positions. What trade-off
    //    does it achieve this?
    // A: LinkedList<T> — a doubly-linked list where each element (node) holds
    //    a value plus pointers to the previous and next nodes. Inserting or
    //    removing at a known node is O(1) because you just update the two
    //    neighboring pointers — no shifting at all. It is like a treasure
    //    hunt where each clue points to the next: to remove a clue, you just
    //    change the previous clue to point past it.
    //
    //    The TRADE-OFF is lookup: LinkedList<T> has no indexing. You cannot
    //    say list[500] and get an answer in O(1). To find the 500th element,
    //    you must start at the head and follow 500 pointers — that is O(n).
    //    List<T> can jump directly to any index in O(1) because array
    //    indexing is just pointer arithmetic: base_address + index × element_size.
    //
    //    Summary:
    //      List<T>         → fast index (O(1)), slow insert/remove middle (O(n))
    //      LinkedList<T>   → slow index (O(n)), fast insert/remove at node (O(1))
    //      HashSet<T>      → fast add/remove (O(1)), no index, no ordering
    [Fact]
    public void FrequentInsertionsAndRemovals_ListIsSlowForMidOperations()
    {
        // Build a list of 100,000 CartItem objects.
        // Each item has a unique CartItemId so we can tell them apart.
        var items = Enumerable
            .Range(0, 100_000)
            .Select(i => new CartItem
            {
                CartItemId = i,
                ItemId = $"Item-{i}",
                ItemName = $"Widget-{i}",
                Quantity = 1
            })
            .ToList();

        // We will insert and remove 1,000 items in the middle of the list.
        // The middle index is roughly 50,000 — so every operation shifts
        // about half the list. That is a LOT of scooting!
        const int operations = 1_000;
        var sw = Stopwatch.StartNew();

        // --- TIMING INSERTIONS ---
        // Each Insert at the middle forces all elements after index 50,000
        // to shift one position to the right. With 100K items, that is
        // ~50,000 shifts per insert × 1,000 inserts = 50 million shifts.
        var insertStart = sw.ElapsedMilliseconds;

        for (var i = 0; i < operations; i++)
        {
            var newItem = new CartItem
            {
                CartItemId = 100_000 + i,
                ItemId = $"New-{i}",
                ItemName = $"NewWidget-{i}",
                Quantity = 1
            };

            // Insert right in the middle — worst case for a List.
            items.Insert(items.Count / 2, newItem);
        }

        var insertElapsed = sw.ElapsedMilliseconds - insertStart;
        _output.WriteLine($"Inserted {operations} items at mid-list: {insertElapsed} ms");

        // List grew by 1,000 items.
        Assert.Equal(100_000 + operations, items.Count);

        // --- TIMING REMOVALS ---
        // Each RemoveAt at the middle forces all elements after that index
        // to shift one position to the left. Same cost as insertion.
        var removeStart = sw.ElapsedMilliseconds;

        for (var i = 0; i < operations; i++)
        {
            // Remove from the middle — each removal shifts ~50K elements.
            // The index shrinks as we remove, but it stays near the middle.
            items.RemoveAt(items.Count / 2);
        }

        var removeElapsed = sw.ElapsedMilliseconds - removeStart;
        _output.WriteLine($"Removed {operations} items from mid-list: {removeElapsed} ms");

        // List is back to original size.
        Assert.Equal(100_000, items.Count);

        // Log the total for comparison.
        sw.Stop();
        _output.WriteLine($"Total time: {sw.ElapsedMilliseconds} ms");
    }

    // This test shows why Dictionary<K,V> is dramatically faster than List<T>
    // for key-based lookups. Imagine you have a phone book (Dictionary) versus
    // a stack of 10,000 index cards (List) — one for each OrderItem.
    //
    // To find OrderItemId 7,777:
    //   - List approach: start at card #1 and flip through EVERY card until
    //     you find #7,777. On average you flip through half the stack (~5,000
    //     cards). That is O(n) — the bigger the list, the more flipping.
    //   - Dictionary approach: the dictionary hashes the key (7,777) to
    //     calculate a bucket number, jumps DIRECTLY to that bucket, and
    //     finds the item. That is O(1) — constant time, regardless of how
    //     many items are stored.
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: When would you choose a List<T> over a Dictionary<K,V> for lookups?
    // A: Use a List when:
    //      - You need to iterate in order (Dictionary has no guaranteed order).
    //      - You need index-based access (list[0], list[^1]).
    //      - You rarely look up by key — the overhead of building a Dictionary
    //        is not worth it if you only search a few times.
    //      - Memory is tight — Dictionary uses extra memory for its hash table.
    //    Use a Dictionary when:
    //      - You frequently look up by a unique key (OrderItemId, SKU, etc.).
    //      - You need O(1) lookups, insertions, and removals by key.
    //      - You want to enforce key uniqueness (no duplicate keys allowed).
    //
    // Q: What is the time complexity difference?
    // A: For n items and m lookups:
    //      List linear search:  O(n) per lookup × m lookups = O(n × m)
    //      Dictionary lookup:  O(1) per lookup × m lookups = O(m)
    //    With n = 10,000 and m = 10,000:
    //      List: up to 100 million comparisons
    //      Dictionary: ~10,000 hash computations
    [Fact]
    public void DictionaryLookupsVsListSearching_O1BeatsOn()
    {
        var sw = Stopwatch.StartNew();

        // Build 10,000 OrderItem objects.
        var allItems = Enumerable
            .Range(1, 10_000)
            .Select(i => new OrderItem
            {
                OrderItemId = i,
                ItemNumberId = $"SKU-{i:D5}",
                Quantity = i % 100,
                Price = i * 0.01m
            })
            .ToList();

        _output.WriteLine($"Built {allItems.Count} items in {sw.ElapsedMilliseconds} ms");

        // --- SETUP: store in both a List and a Dictionary ---
        var list = new List<OrderItem>(allItems);
        var dict = allItems.ToDictionary(item => item.OrderItemId);

        _output.WriteLine($"Dictionary built in {sw.ElapsedMilliseconds} ms (cumulative)");

        // Pick 10,000 random IDs to look up — some exist, some do not.
        // Using a fixed seed so the test is reproducible.
        var rng = new Random(42);
        var lookupIds = Enumerable
            .Range(0, 10_000)
            .Select(_ => rng.Next(1, 20_000)) // IDs 1–20,000 (only 1–10,000 exist)
            .ToList();

        // --- APPROACH 1: List linear search ---
        // For each ID, scan the entire list from start to finish until we
        // find a match or run out of items. This is what List.Find() does
        // internally — it is O(n) per lookup.
        sw.Restart();

        var listHits = 0;
        foreach (var id in lookupIds)
        {
            var found = list.Find(item => item.OrderItemId == id);
            if (found is not null)
                listHits++;
        }

        var listMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"List search:   {listMs} ms ({listHits} hits out of {lookupIds.Count} lookups)");

        // --- APPROACH 2: Dictionary hash lookup ---
        // For each ID, the dictionary hashes the key and jumps directly to
        // the bucket. No scanning needed — O(1) per lookup.
        sw.Restart();

        var dictHits = 0;
        foreach (var id in lookupIds)
        {
            if (dict.TryGetValue(id, out _))
                dictHits++;
        }

        var dictMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"Dict lookup:   {dictMs} ms ({dictHits} hits out of {lookupIds.Count} lookups)");

        // Both approaches find the same number of hits.
        Assert.Equal(listHits, dictHits);

        // The dictionary MUST be faster. In practice it is typically
        // 100–1000× faster for this dataset size. We use a generous
        // upper bound to avoid flaky timing failures on slow CI machines.
        _output.WriteLine($"Speedup: {listMs / Math.Max(dictMs, 1):F0}×");

        // The dictionary should find items at least 2× faster (generous bound).
        Assert.True(dictMs < listMs / 2,
            $"Dictionary ({dictMs} ms) should be at least 2× faster than List ({listMs} ms)");

        // --- BONUS: verify correctness ---
        // The dictionary should return the exact same objects as the list search.
        foreach (var id in lookupIds.Where(id => id <= 10_000))
        {
            var fromList = list.Find(item => item.OrderItemId == id);
            var fromDict = dict.TryGetValue(id, out var lookedUp) ? lookedUp : null;

            Assert.NotNull(fromList);
            Assert.NotNull(fromDict);
            Assert.Equal(fromList!.OrderItemId, fromDict!.OrderItemId);
            Assert.Equal(fromList.ItemNumberId, fromDict.ItemNumberId);
        }
    }

    // A test-only record representing an item with a price.
    // Needed for scenarios 3 and 5. Defined at the class level because
    // C# does not support local types inside methods.
    private record ItemPrice(int ItemId, decimal Price);

    // This test teaches you to pick the RIGHT collection for each job.
    // Not all collections are the same — a screwdriver and a hammer are
    // both tools, but you would not use a hammer to turn a screw! Same
    // idea: List, Dictionary, HashSet, Queue, and IReadOnlyList each
    // shine in different situations.
    //
    // For each scenario below, we explain WHY a specific collection is
    // the best choice, then show it working with real code.
    [Fact]
    public void ChoosingTheRightCollection_MatchCollectionToScenario()
    {
        // ---------------------------------------------------------------
        // SCENARIO 1: Unique discount codes, check if already used.
        //
        // Collection: HashSet<string>
        //
        // Why: A HashSet stores only UNIQUE items and can check "is this
        // item in the set?" in O(1) — constant time, no matter how many
        // codes you have. It is like a bouncer with a guest list: she
        // does not flip through 10,000 names — she checks her clipboard
        // instantly.
        //
        // If you tried a List<string>, every "has this code been used?"
        // check would scan the entire list — O(n) — which gets slower
        // as more codes are added.
        // ---------------------------------------------------------------
        var usedCodes = new HashSet<string>
        {
            "SUMMER25",
            "WELCOME10",
            "FREESHIP"
        };

        // O(1) lookup — the HashSet hashes "SAVE20" and checks one bucket.
        Assert.Contains("SUMMER25", usedCodes);
        Assert.DoesNotContain("SAVE20", usedCodes);

        // Adding a duplicate does nothing — HashSet enforces uniqueness.
        var wasAdded = usedCodes.Add("SUMMER25");
        Assert.False(wasAdded);               // already exists → not added
        Assert.Equal(3, usedCodes.Count);      // count stays at 3

        // Adding a new code succeeds.
        Assert.True(usedCodes.Add("SAVE20"));  // new code → added
        Assert.Equal(4, usedCodes.Count);

        // HashSet<string> → O(1) contains check, enforces uniqueness

        // ---------------------------------------------------------------
        // SCENARIO 2: Process incoming messages in the order received.
        //
        // Collection: Queue<T>
        //
        // Why: A Queue is FIFO (First In, First Out) — like a line at a
        // store. The first person to get in line is the first to be
        // served. Enqueue adds to the back, Dequeue removes from the
        // front — both are O(1).
        //
        // If you used a List, removing from the front (index 0) shifts
        // every other element left — O(n) — wasteful when you never
        // need random access.
        // ---------------------------------------------------------------
        var messageQueue = new Queue<string>();

        // Messages arrive in order: "OrderCreated", "PaymentReceived",
        // "Shipped". Enqueue puts each at the back of the line.
        messageQueue.Enqueue("OrderCreated");
        messageQueue.Enqueue("PaymentReceived");
        messageQueue.Enqueue("Shipped");

        Assert.Equal(3, messageQueue.Count);

        // Dequeue removes from the front — FIFO order guaranteed.
        Assert.Equal("OrderCreated", messageQueue.Dequeue());
        Assert.Equal("PaymentReceived", messageQueue.Dequeue());
        Assert.Equal("Shipped", messageQueue.Dequeue());
        Assert.Empty(messageQueue);

        // Peek looks at the front without removing — useful for
        // "what is next?" without committing to process it.
        messageQueue.Enqueue("OrderCreated");
        messageQueue.Enqueue("PaymentReceived");
        Assert.Equal("OrderCreated", messageQueue.Peek());
        Assert.Equal(2, messageQueue.Count); // Peek does not remove

        // Queue<T> → O(1) enqueue/dequeue, FIFO order guaranteed

        // ---------------------------------------------------------------
        // SCENARIO 3: Map an ItemId to its ItemPrice for quick lookup.
        //
        // Collection: Dictionary<int, ItemPrice>
        //
        // Why: A Dictionary is a key→value map. You give it a key
        // (ItemId) and it returns the associated value (ItemPrice) in
        // O(1). It is like a real dictionary: you do not read every
        // word to find "apple" — you flip to the A section directly.
        //
        // If you used a List<ItemPrice>, you would have to scan every
        // element comparing ItemId until you found a match — O(n).
        // ---------------------------------------------------------------
        var priceCatalog = new Dictionary<int, ItemPrice>
        {
            [1] = new ItemPrice(1, 10.00m),
            [2] = new ItemPrice(2, 25.50m),
            [3] = new ItemPrice(3, 3.75m)
        };

        // O(1) lookup by key.
        Assert.True(priceCatalog.TryGetValue(2, out var price));
        Assert.Equal(25.50m, price!.Price);

        // Missing key returns false — no exception thrown.
        Assert.False(priceCatalog.TryGetValue(99, out _));

        // Count shows how many key→value pairs exist.
        Assert.Equal(3, priceCatalog.Count);

        // Dictionary<int, ItemPrice> → O(1) key-based lookup

        // ---------------------------------------------------------------
        // SCENARIO 4: Return a list of OrderItems, but the caller should
        //             not be able to modify it.
        //
        // Collection: IReadOnlyList<OrderItem>
        //
        // Why: IReadOnlyList<T> is a read-only interface — it exposes
        // Count, index access, and enumeration, but has NO Add, Remove,
        // or Clear methods. It is like handing someone a photograph of
        // your shopping cart — they can LOOK at it but cannot take items
        // out or add new ones.
        //
        // If you returned a plain List<T>, the caller could call
        // .Add(), .Remove(), or .Clear() and mess up your internal state.
        // ---------------------------------------------------------------
        var internalStorage = new List<OrderItem>
        {
            new() { OrderItemId = 1, ItemNumberId = "A", Quantity = 2, Price = 10.00m },
            new() { OrderItemId = 2, ItemNumberId = "B", Quantity = 1, Price = 25.00m }
        };

        // The method returns IReadOnlyList — callers see read-only surface.
        IReadOnlyList<OrderItem> readOnlyView = internalStorage;

        // Reading works: index access, Count, foreach.
        Assert.Equal(2, readOnlyView.Count);
        Assert.Equal("A", readOnlyView[0].ItemNumberId);

        var enumerated = readOnlyView.ToList();
        Assert.Equal(2, enumerated.Count);

        // The following would NOT compile if uncommented:
        //
        //   readOnlyView.Add(new OrderItem());     // CS1061: no Add
        //   readOnlyView.RemoveAt(0);              // CS1061: no RemoveAt
        //   readOnlyView.Clear();                  // CS1061: no Clear
        //
        // The interface simply does not expose mutation methods.

        // Internal storage is still intact — nothing was modified.
        Assert.Equal(2, internalStorage.Count);

        // IReadOnlyList<OrderItem> → read-only view, no mutation methods

        // ---------------------------------------------------------------
        // SCENARIO 5: Sequential history of price changes, access by
        //             position in the sequence.
        //
        // Collection: List<ItemPrice>
        //
        // Why: A List preserves insertion order and supports indexed
        // access — list[0] gives you the first price change, list[^1]
        // gives you the most recent. It is like a timeline drawn on
        // paper: you can jump to any point by counting from the start.
        //
        // Other collections cannot do this:
        //   - HashSet: no order at all
        //   - Dictionary: keyed by ItemId, not by position in history
        //   - Queue: FIFO only, no random index access
        // ---------------------------------------------------------------
        var priceHistory = new List<ItemPrice>
        {
            new(1, 10.00m),   // Original price
            new(1, 12.50m),   // First increase
            new(1, 9.99m),    // Sale price
            new(1, 15.00m)    // Current price
        };

        // Indexed access — jump to any position in O(1).
        Assert.Equal(10.00m, priceHistory[0].Price);     // first price
        Assert.Equal(15.00m, priceHistory[^1].Price);    // most recent (^1 = last)

        // Count tells you how many price changes occurred.
        Assert.Equal(4, priceHistory.Count);

        // Add a new price change — preserves order, appends to end.
        priceHistory.Add(new ItemPrice(1, 8.50m));
        Assert.Equal(5, priceHistory.Count);
        Assert.Equal(8.50m, priceHistory[^1].Price);

        // Find all price increases using LINQ — List supports full enumeration.
        var increases = priceHistory
            .Zip(priceHistory.Skip(1), (prev, curr) => new { From = prev.Price, To = curr.Price })
            .Where(x => x.To > x.From)
            .ToList();

        Assert.Equal(2, increases.Count); // 10→12.50 and 12.50→15.00

        // List<ItemPrice> → indexed access, preserves insertion order
    }
}
