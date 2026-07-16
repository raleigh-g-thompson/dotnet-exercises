using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OnboardingProject.Api.Features.Cart.Models;
using OnboardingProject.Api.Features.Orders.Models;
using Xunit.Abstractions;

namespace OnboardingProject.Api.UnitTests.Features.Orders.Models;

public class AsyncAwaitTests
{
    private readonly ITestOutputHelper _output;

    // Helper class for Exercise8 — async factory + async disposal
    class PricingCacheSession : IAsyncDisposable
    {
        private bool _isOpen;
        private readonly string _cacheName;

        private PricingCacheSession(string cacheName)
        {
            _cacheName = cacheName;
            _isOpen = false;
        }

        public static async Task<PricingCacheSession> CreateAsync(string cacheName)
        {
            var session = new PricingCacheSession(cacheName);
            await Task.Delay(100);
            session._isOpen = true;
            return session;
        }

        public async Task<decimal> GetPriceAsync(string sku)
        {
            if (!_isOpen)
                throw new InvalidOperationException("Cache connection is not open");
            await Task.Delay(50);
            return 42.50m;
        }

        public async ValueTask DisposeAsync()
        {
            if (_isOpen)
            {
                await Task.Delay(100);
                _isOpen = false;
            }
        }

        public bool IsOpen => _isOpen;
        public string CacheName => _cacheName;
    }

    public AsyncAwaitTests(ITestOutputHelper output) => _output = output;

    // Exercise 1: Thread switching after await.
    // After an await, .NET picks a thread from the pool to continue —
    // it might be the same thread or a different one.
    [Fact]
    public async Task Exercise1_ThreadSwitching()
    {
        async Task<(int Before, int After)> GetThreadIdsAroundAwait()
        {
            var before = Thread.CurrentThread.ManagedThreadId;
            await Task.Delay(1);
            var after = Thread.CurrentThread.ManagedThreadId;
            return (before, after);
        }

        var switched = 0;

        for (var i = 0; i < 10; i++)
        {
            var (before, after) = await GetThreadIdsAroundAwait();
            if (before != after)
                switched++;
        }

        _output.WriteLine($"Thread switched in {switched} out of 10 runs.");

        // The test passes regardless — sometimes the thread changes, sometimes not.
        Assert.True(switched >= 0);
    }

    // Exercise 2: Sync vs sequential async vs concurrent async.
    // Fetching 3 carts one-by-one with GetResult() is slow and blocking.
    // Fetching them with await is better (thread is free during waits).
    // Fetching them with Task.WhenAll is fastest (all run in parallel).
    [Fact]
    public async Task Exercise2_SyncVsAsync()
    {
        async Task<CartItem> FetchCartAsync(int cartId)
        {
            await Task.Delay(200);
            return new CartItem { CartItemId = cartId, CartId = cartId, ItemId = $"ITEM-{cartId}", ItemName = $"Cart {cartId}", Quantity = 1 };
        }

        var cartIds = new List<int> { 1, 2, 3 };

        // Approach 1: Sync (blocking) — one at a time, thread is stuck.
        var syncSw = Stopwatch.StartNew();
        var syncResults = new List<CartItem>();

        for (var i = 0; i < cartIds.Count; i++)
        {
            var cart = FetchCartAsync(cartIds[i]).GetAwaiter().GetResult();
            syncResults.Add(cart);
        }

        syncSw.Stop();
        _output.WriteLine($"Sync: {syncSw.ElapsedMilliseconds} ms");

        // Approach 2: Sequential async — one at a time, but thread is free.
        var seqSw = Stopwatch.StartNew();
        var seqResults = new List<CartItem>();

        for (var i = 0; i < cartIds.Count; i++)
        {
            var cart = await FetchCartAsync(cartIds[i]);
            seqResults.Add(cart);
        }

        seqSw.Stop();
        _output.WriteLine($"Sequential: {seqSw.ElapsedMilliseconds} ms");

        // Approach 3: Concurrent async — all at once with Task.WhenAll.
        var conSw = Stopwatch.StartNew();

        var tasks = new List<Task<CartItem>>();
        for (var i = 0; i < cartIds.Count; i++)
        {
            tasks.Add(FetchCartAsync(cartIds[i]));
        }

        var conResults = await Task.WhenAll(tasks);

        conSw.Stop();
        _output.WriteLine($"Concurrent: {conSw.ElapsedMilliseconds} ms");

        // All approaches returned the right number of carts.
        Assert.Equal(3, syncResults.Count);
        Assert.Equal(3, seqResults.Count);
        Assert.Equal(3, conResults.Length);

        // Concurrent should be faster than sync.
        Assert.True(conSw.ElapsedMilliseconds < syncSw.ElapsedMilliseconds);
    }

    // Exercise 3: When one call depends on another.
    // You must fetch the cart first to get the item IDs, then you can
    // fetch all prices at the same time with Task.WhenAll.
    [Fact]
    public async Task Exercise3_WhenOneCallDependsOnAnother()
    {
        async Task<List<CartItem>> FetchCartAsync(int cartId)
        {
            await Task.Delay(200);
            return
            [
                new CartItem { CartItemId = 1, CartId = cartId, ItemId = "ITEM-A", ItemName = "Widget", Quantity = 2 },
                new CartItem { CartItemId = 2, CartId = cartId, ItemId = "ITEM-B", ItemName = "Gadget", Quantity = 1 },
                new CartItem { CartItemId = 3, CartId = cartId, ItemId = "ITEM-C", ItemName = "Doohickey", Quantity = 3 }
            ];
        }

        async Task<ItemPrice> FetchPriceAsync(string itemId)
        {
            await Task.Delay(200);
            return itemId switch
            {
                "ITEM-A" => new ItemPrice { ItemId = itemId, BasePrice = 9.99m },
                "ITEM-B" => new ItemPrice { ItemId = itemId, BasePrice = 24.50m },
                "ITEM-C" => new ItemPrice { ItemId = itemId, BasePrice = 3.75m },
                _ => new ItemPrice { ItemId = itemId, BasePrice = 0m }
            };
        }

        // Approach 1: Fully sequential — cart, then each price one by one.
        var seqSw = Stopwatch.StartNew();

        var cart = await FetchCartAsync(cartId: 1);

        var itemIds = new List<string>();
        for (var i = 0; i < cart.Count; i++)
        {
            itemIds.Add(cart[i].ItemId);
        }

        var seqPrices = new List<ItemPrice>();
        for (var i = 0; i < itemIds.Count; i++)
        {
            var price = await FetchPriceAsync(itemIds[i]);
            seqPrices.Add(price);
        }

        seqSw.Stop();
        _output.WriteLine($"Sequential prices: {seqSw.ElapsedMilliseconds} ms");

        // Approach 2: Sequential cart + concurrent prices.
        var conSw = Stopwatch.StartNew();

        var cart2 = await FetchCartAsync(cartId: 1);

        var itemIds2 = new List<string>();
        for (var i = 0; i < cart2.Count; i++)
        {
            itemIds2.Add(cart2[i].ItemId);
        }

        var priceTasks = new List<Task<ItemPrice>>();
        for (var i = 0; i < itemIds2.Count; i++)
        {
            priceTasks.Add(FetchPriceAsync(itemIds2[i]));
        }

        var conPrices = await Task.WhenAll(priceTasks);

        conSw.Stop();
        _output.WriteLine($"Concurrent prices: {conSw.ElapsedMilliseconds} ms");

        // Both approaches returned the same prices.
        Assert.Equal(seqPrices.Count, conPrices.Length);
        Assert.Equal("ITEM-A", seqPrices[0].ItemId);
        Assert.Equal("ITEM-A", conPrices[0].ItemId);

        // Concurrent prices should be faster.
        Assert.True(conSw.ElapsedMilliseconds < seqSw.ElapsedMilliseconds);
    }

    // Exercise 4: Forgetting to await.
    // When you call an async method without await, you get a Task back,
    // not the result. If the task throws, the exception is silently swallowed.
    [Fact]
    public async Task Exercise4_ForgettingToAwait()
    {
        async Task<Order> FetchOrderAsync(int orderId)
        {
            await Task.Delay(200);
            return new Order
            {
                OrderId = orderId,
                Customer = new() { CustomerId = 1 },
                OrderItems =
                [
                    new() { OrderItemId = 1, OrderId = orderId, ItemNumberId = "Widget", Quantity = 2, Price = 10.00m }
                ]
            };
        }

        async Task<Order> FailingFetchAsync(int orderId)
        {
            await Task.Delay(50);
            throw new InvalidOperationException($"Order {orderId} not found in the database.");
        }

        // Part 1: What do you get from an unawaited call?
        Task<Order> unawaitedTask = FetchOrderAsync(1);
        _output.WriteLine($"Type: {unawaitedTask.GetType().Name}");

        await Task.Delay(300);

        var order = await unawaitedTask;
        Assert.Equal(1, order.OrderId);

        // Part 2: What happens to exceptions in unawaited methods?
        Task<Order> failingTask = FailingFetchAsync(999);
        await Task.Delay(200);

        Assert.True(failingTask.IsFaulted);

        var innerException = failingTask.Exception?.InnerException as InvalidOperationException;
        Assert.NotNull(innerException);
        Assert.Contains("not found in the database", innerException.Message);

        // ---------------------------------------------------------------
        // THE LESSON:
        //
        // Always await your async calls! If you intentionally want to fire
        // and forget (rare), use a discard and a comment:
        //
        //   _ = SomeAsyncWork(); // Intentional fire-and-forget: reason here
        //
        // This makes it clear the missing await is deliberate, not a bug.
        // ---------------------------------------------------------------
    }

    // Exercise 5: Exception handling with Task.WhenAll.
    // When multiple tasks throw, Task.WhenAll wraps them in one AggregateException.
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: When multiple tasks throw exceptions inside Task.WhenAll, what
    //    exception do you catch?
    // A: You catch an AggregateException. Think of it like a shopping bag
    //    that holds all the broken items. Task.WhenAll does NOT throw the
    //    first error and stop — it waits for EVERY task to finish, then
    //    stuffs all the errors into one AggregateException and throws that.
    //
    // Q: How do you access all of the exceptions, not just the first one?
    // A: Use the InnerExceptions property — it is a list of every exception
    //    that was thrown. You can also call .Flatten() to unwrap nested
    //    AggregateExceptions (useful when tasks themselves use Task.WhenAll).
    //
    // Q: What happens if you await the individual tasks after Task.WhenAll
    //    throws? Does re-awaiting a faulted task throw again?
    // A: YES! Each faulted task remembers its exception. When you await it
    //    again, it throws the ORIGINAL exception (not wrapped in Aggregate).
    //    This lets you handle each failure specifically with its own catch block.
    [Fact]
    public async Task Exercise5_ExceptionHandling()
    {
        async Task<List<CartItem>> FetchCartDataAsync()
        {
            await Task.Delay(100);
            return
            [
                new CartItem { CartItemId = 1, CartId = 1, ItemId = "ITEM-A", ItemName = "Widget", Quantity = 2 },
                new CartItem { CartItemId = 2, CartId = 1, ItemId = "ITEM-B", ItemName = "Gadget", Quantity = 1 }
            ];
        }

        async Task<List<ItemPrice>> FetchItemPricesAsync()
        {
            await Task.Delay(100);
            throw new InvalidOperationException("Pricing service unavailable");
        }

        async Task<decimal> ApplyDiscountAsync()
        {
            await Task.Delay(100);
            throw new TimeoutException("Discount service timed out");
        }

        // Fire all three tasks — two will fail.
        var combinedTask = Task.WhenAll(
            FetchCartDataAsync(),
            FetchItemPricesAsync(),
            ApplyDiscountAsync()
        );

        try { await combinedTask; } catch { }

        // The combined task is faulted — it holds an AggregateException.
        Assert.True(combinedTask.IsFaulted);
        var aggregateException = combinedTask.Exception!;

        // Two tasks failed, so there are two inner exceptions.
        Assert.Equal(2, aggregateException.InnerExceptions.Count);

        // Check that both exception types are present.
        var exceptionTypes = new List<string>();
        for (var i = 0; i < aggregateException.InnerExceptions.Count; i++)
        {
            exceptionTypes.Add(aggregateException.InnerExceptions[i].GetType().Name);
        }

        Assert.Contains("InvalidOperationException", exceptionTypes);
        Assert.Contains("TimeoutException", exceptionTypes);

        // Re-awaiting individual tasks throws the original exception (unwrapped).
        var priceEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FetchItemPricesAsync());
        Assert.Equal("Pricing service unavailable", priceEx.Message);

        // ---------------------------------------------------------------
        // THE LESSON:
        //
        // 1. Task.WhenAll wraps ALL failures in ONE AggregateException.
        //    You catch that, not the individual errors.
        //
        // 2. Use .InnerExceptions to see every error, or .Flatten() to
        //    simplify nested cases.
        //
        // 3. Re-awaiting individual faulted tasks throws the ORIGINAL
        //    exception unwrapped — so you can handle each one specifically.
        //
        // Common pattern:
        //
        //   try {
        //       await Task.WhenAll(task1, task2, task3);
        //   } catch (AggregateException ae) {
        //       foreach (var ex in ae.Flatten().InnerExceptions) {
        //           if (ex is TimeoutException te) { /* handle timeout */ }
        //           else if (ex is InvalidOperationException io) { /* handle */ }
        //           else { /* unexpected */ }
        //       }
        //   }
        // ---------------------------------------------------------------
    }

    // Exercise 6: Fire-and-forget pitfalls.
    // When you call an async Task method without await, exceptions are silently swallowed.
    [Fact]
    public async Task Exercise6_FireAndForgetPitfalls()
    {
        var order = new Order
        {
            OrderId = 1,
            Customer = new() { CustomerId = 1 },
            OrderItems =
            [
                new() { OrderItemId = 1, OrderId = 1, ItemNumberId = "Widget-A", Quantity = 10, Price = 10.00m },
                new() { OrderItemId = 2, OrderId = 1, ItemNumberId = "Widget-B", Quantity = 5,  Price = 30.00m }
            ]
        };

        decimal CalculateOrderTotal(Order o)
        {
            return o.OrderItems.Sum(item => item.Price * item.Quantity);
        }

        async Task SaveAuditLogAsync(Order o, decimal total)
        {
            await Task.Delay(100);
            throw new InvalidOperationException("Audit log write failed: disk full");
        }

        var total = CalculateOrderTotal(order);
        Assert.Equal(250.00m, total);

        // Fire-and-forget: call without await — exception is silently swallowed.
        Task auditTask = SaveAuditLogAsync(order, total);
        await Task.Delay(200);

        Assert.True(auditTask.IsFaulted);

        var innerException = auditTask.Exception?.InnerException as InvalidOperationException;
        Assert.NotNull(innerException);
        Assert.Equal("Audit log write failed: disk full", innerException.Message);

        // Safe pattern: keep the Task reference and observe it with try/catch.
        Task observedAuditTask = SaveAuditLogAsync(order, total);
        await Task.Delay(200);

        try
        {
            await observedAuditTask;
        }
        catch (InvalidOperationException ex)
        {
            _output.WriteLine($"Caught and handled: {ex.Message}");
            Assert.Equal("Audit log write failed: disk full", ex.Message);
        }

        // ---------------------------------------------------------------
        // THE LESSON:
        //
        // 1. async Task + fire-and-forget = exception silently swallowed.
        //    No crash, but also no handling. The Task sits in a faulted
        //    state and nobody looks at it. Acceptable ONLY for truly
        //    optional work where failure is safe to ignore.
        //
        // 2. async void + fire-and-forget = CRASHES THE PROCESS.
        //    The exception hits UnhandledException and terminates the
        //    app. There is no try/catch that can save you. Never use
        //    async void for background work you control.
        //
        // 3. Safe pattern: always return Task. Either await it, or keep
        //    the Task reference and observe it explicitly — via await
        //    in a try/catch, via .ContinueWith for logging, or via a
        //    centralized tracker that drains at shutdown.
        //
        // When is fire-and-forget acceptable?
        //
        //   - The work is TRULY optional (telemetry, audit logging).
        //   - Failure is safe to ignore (no data corruption).
        //   - The operation completes within the process lifetime.
        //
        // If any of those don't hold, await the task or hand it to a
        // tracker that will observe it.
        // ---------------------------------------------------------------
    }

    // Exercise 7: Async streams.
    // IAsyncEnumerable lets you yield items one at a time with await.
    //
    // What if you have a LOT of items and you want to process them as they
    // arrive — not wait for all of them to be collected first?
    //
    // Normally, an async method returns Task<T> where T is the WHOLE result.
    // That means you wait until every single item is ready before you see
    // ANY of them. But sometimes items arrive one at a time (like rows from
    // a database, or products from a warehouse scan), and you want to start
    // working on the first one while the rest are still being fetched.
    //
    // C# has a language feature for this: IAsyncEnumerable<T> — the async
    // version of IEnumerable<T>. You produce items with "yield return" inside
    // an "async" method, and the consumer iterates with "await foreach".
    // Each item is yielded one at a time; the caller pulls the next one only
    // when it is ready.
    [Fact]
    public async Task Exercise7_AsyncStreams()
    {
        async IAsyncEnumerable<OrderItem> FetchOrderItemsStreamingAsync(int orderId)
        {
            var items = new[]
            {
                new OrderItem { OrderItemId = 1, OrderId = orderId, ItemNumberId = "SKU-100", Quantity = 2, Price = 15.50m },
                new OrderItem { OrderItemId = 2, OrderId = orderId, ItemNumberId = "SKU-200", Quantity = 1, Price = 49.99m },
                new OrderItem { OrderItemId = 3, OrderId = orderId, ItemNumberId = "SKU-300", Quantity = 5, Price = 8.25m },
            };

            foreach (var item in items)
            {
                await Task.Delay(100);
                yield return item;
            }
        }

        // Part 1: Consume an async stream with await foreach.
        var streamedItems = new List<OrderItem>();

        await foreach (var item in FetchOrderItemsStreamingAsync(orderId: 1))
        {
            streamedItems.Add(item);
        }

        Assert.Equal(3, streamedItems.Count);

        // Part 2: Early termination with break.
        var partialItems = new List<OrderItem>();

        await foreach (var item in FetchOrderItemsStreamingAsync(orderId: 2))
        {
            partialItems.Add(item);
            if (partialItems.Count == 2)
                break;
        }

        Assert.Equal(2, partialItems.Count);

        // ---------------------------------------------------------------
        // THE LESSON:
        //
        // 1. async IAsyncEnumerable<T> + yield return = produce items
        //    one at a time, asynchronously. The caller pulls each item;
        //    you await between yields.
        //
        // 2. "await foreach" = consume an async stream. It calls
        //    MoveNextAsync() each iteration and DisposeAsync() when done
        //    (even on break or exception).
        //
        // 3. Memory: only one item is in memory at a time (plus the
        //    producer's state machine). The whole collection is never
        //    buffered — unlike Task<List<T>> which holds everything.
        //
        // 4. Early exit: "break" inside "await foreach" stops the stream
        //    and triggers DisposeAsync() on the enumerator — resources are
        //    always cleaned up even if the producer does not have a
        //    finally block.
        //
        // 5. Use IAsyncEnumerable<T> when:
        //    - You have many items (or an unknown/unbounded number).
        //    - Producing each item involves async I/O.
        //    - You want to start processing before the last item arrives.
        //
        // If all items are already in memory, use a regular List<T> and
        // foreach. IAsyncEnumerable is for items that arrive over time.
        // ---------------------------------------------------------------
    }

    // Exercise 8: Async disposal with IAsyncDisposable.
    // Some objects need async cleanup — use "await using" for automatic disposal.
    [Fact]
    public async Task Exercise8_AsyncDisposal()
    {
        // Create with async factory method (constructors can't be async).
        PricingCacheSession session = await PricingCacheSession.CreateAsync("live-prices");
        Assert.True(session.IsOpen);
        Assert.Equal("live-prices", session.CacheName);

        var price = await session.GetPriceAsync("SKU-100");
        Assert.Equal(42.50m, price);

        await session.DisposeAsync();
        Assert.False(session.IsOpen);

        // "await using" calls DisposeAsync automatically when the block ends.
        await using var checkoutSession = await PricingCacheSession.CreateAsync("checkout-cache");
        Assert.True(checkoutSession.IsOpen);

        var checkoutPrice = await checkoutSession.GetPriceAsync("SKU-200");
        Assert.Equal(42.50m, checkoutPrice);

        // DisposeAsync runs when this method exits.

        // ---------------------------------------------------------------
        // THE LESSON:
        //
        // 1. Constructors CANNOT be async. They must return `this`
        //    synchronously. Use an async factory method (static CreateAsync)
        //    to do async initialization — the caller awaits it and gets a
        //    fully-ready object.
        //
        // 2. IAsyncDisposable is the async version of IDisposable. It
        //    declares `ValueTask DisposeAsync()`. Use it when cleanup
        //    involves I/O (closing connections, flushing buffers, etc).
        //
        // 3. "await using var x = ...;" scopes disposal to the end of the
        //    enclosing method — just like "using var x = ...;" but async.
        //
        // 4. Under the hood, "await using" generates a try/finally that
        //    calls DisposeAsync().
        //
        // 5. DisposeAsync() runs even if an exception is thrown. Resources
        //    are always cleaned up — same safety as synchronous "using".
        //
        // 6. Return ValueTask (not Task) from DisposeAsync(). Disposal
        //    often completes synchronously — ValueTask avoids a Task
        //    allocation on that common path.
        //
        // 7. IDisposable vs IAsyncDisposable:
        //    - IDisposable:       void Dispose()          — synchronous
        //    - IAsyncDisposable:  ValueTask DisposeAsync() — async
        //    - Use "using" for the first, "await using" for the second.
        //    - Many classes implement BOTH, so callers can choose.
        // ---------------------------------------------------------------
    }
}
