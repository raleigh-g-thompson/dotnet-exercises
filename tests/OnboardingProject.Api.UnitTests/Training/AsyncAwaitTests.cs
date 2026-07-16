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

    // ---------------------------------------------------------------
    // PricingCacheSession: a class that needs async init + async cleanup.
    //
    // Used by Exercise8_AsyncDisposal to demonstrate IAsyncDisposable
    // and the async factory pattern.
    // ---------------------------------------------------------------
    class PricingCacheSession : IAsyncDisposable
    {
        private bool _isOpen;
        private readonly string _cacheName;

        // Private constructor — prevents `new PricingCacheSession()` from
        // outside. The constructor is SYNCHRONOUS and does NO async work.
        private PricingCacheSession(string cacheName)
        {
            _cacheName = cacheName;
            _isOpen = false;
        }

        // The async factory method — this is how callers create instances.
        // The caller awaits this and gets a fully-ready object.
        public static async Task<PricingCacheSession> CreateAsync(string cacheName)
        {
            var session = new PricingCacheSession(cacheName);
            await Task.Delay(100);  // simulate async init (connect, auth, etc.)
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

        // IAsyncDisposable: async cleanup. Returns ValueTask because
        // disposal often completes synchronously — avoids Task allocation.
        public async ValueTask DisposeAsync()
        {
            if (_isOpen)
            {
                await Task.Delay(100);  // simulate async cleanup (flush, close)
                _isOpen = false;
            }
        }

        public bool IsOpen => _isOpen;
        public string CacheName => _cacheName;
    }

    public AsyncAwaitTests(ITestOutputHelper output) => _output = output;

    // This test teaches what happens to your THREAD when you use "await".
    //
    // Every thread in .NET has a number called ManagedThreadId — think of it
    // like a name tag that tells you WHO is doing the work. When you call
    // "await Task.Delay(something)", your method pauses and lets the thread
    // go do other work. When the delay finishes, .NET needs to pick a thread
    // to continue your code. It grabs ANY free thread from the thread pool —
    // it might be the same one, or it might be a totally different one!
    //
    // This test runs the same async method 10 times and prints the thread ID
    // before and after the await. You will see that sometimes the thread
    // changes and sometimes it does not. This is normal!
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: What does this tell you about which thread continues your code after
    //    an I/O operation completes?
    // A: It tells you that you CANNOT assume your code will run on the same
    //    thread after an await. The thread pool decides. This is why you must
    //    never store a "current thread" reference and use it later — it might
    //    be pointing at the wrong thread! It also means you should not do
    //    thread-affinity work (like touching UI controls) after an await
    //    unless you explicitly marshal back to the right thread.
    [Fact]
    public async Task Exercise1_ThreadSwitching()
    {
        // This helper records the thread ID before and after a short await.
        // "Task.Delay" is a stand-in for any real I/O operation (like reading
        // a file or calling a web API). The key thing is that "await" pauses
        // this method and lets the thread go away — when the delay finishes,
        // a (possibly different) thread picks up where we left off.
        async Task<(int Before, int After)> GetThreadIdsAroundAwait()
        {
            var before = Thread.CurrentThread.ManagedThreadId;

            // Task.Delay is like setting a timer — it does NOT block the
            // thread. The thread is free to go do other work while we wait.
            await Task.Delay(1);

            // When the delay finishes, .NET grabs a thread from the pool.
            // This might be the SAME thread we started on, or a DIFFERENT one.
            var after = Thread.CurrentThread.ManagedThreadId;

            return (before, after);
        }

        var results = new List<(int Run, int Before, int After, bool Changed)>();

        // Run the same async method 10 times. Each time, the thread pool
        // might give us the same thread or a different one — we cannot predict!
        for (var i = 1; i <= 10; i++)
        {
            var (before, after) = await GetThreadIdsAroundAwait();
            var changed = before != after;
            results.Add((i, before, after, changed));

            _output.WriteLine(
                $"Run {i,2}: Thread {before} → Thread {after}" +
                (changed ? " (CHANGED)" : " (same)"));
        }

        // Count how many runs had a thread switch.
        var switched = results.Count(r => r.Changed);
        _output.WriteLine($"");
        _output.WriteLine($"Thread switched in {switched} out of {results.Count} runs.");

        // The test passes regardless of how many switches happened — the
        // whole point is to OBSERVE the behavior. Sometimes the pool reuses
        // the same thread, sometimes it does not. Both are correct.
        Assert.Equal(10, results.Count);

        // Every run should have valid thread IDs (positive integers).
        Assert.All(results, r => Assert.True(r.Before > 0));
        Assert.All(results, r => Assert.True(r.After > 0));
    }

    // Imagine you need to ask 10 different stores if they have a toy in stock.
    // Each store takes 200ms to answer. How you ask them makes a HUGE difference!
    //
    // Approach 1 — Synchronous (blocking):
    //   Walk to Store #1, ask, WAIT for the answer. Then walk to Store #2, ask,
    //   WAIT again. You are standing there doing NOTHING while the store checks.
    //   That is like calling .GetAwaiter().GetResult() — your thread is BLOCKED,
    //   stuck waiting and unable to do anything else.
    //   Time: 10 stores × 200ms = 2,000ms
    //
    // Approach 2 — Sequential async (non-blocking):
    //   Same order — ask Store #1, wait, ask Store #2, wait. BUT your thread
    //   is NOT blocked during the wait. It is free to go do other work while
    //   the store checks. When the answer comes back, the thread picks up where
    //   it left off. Wall-clock time is the SAME, but the thread was not wasted.
    //   Time: 10 stores × 200ms = 2,000ms (but thread was free during waits!)
    //
    // Approach 3 — Concurrent async (all at once):
    //   Send a messenger to ALL 10 stores at the SAME TIME. All 10 checks
    //   happen in parallel. You wait for the last one to finish, then collect
    //   all answers. That is Task.WhenAll — fire everything, await the group.
    //   Time: ~200ms (all delays overlap!)
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: What is the difference between these three approaches?
    // A: Speed and thread usage!
    //    - Sync: slow AND wastes the thread. The thread sits there staring at
    //      the wall while waiting. If this is the main thread, your whole app
    //      is frozen. On a web server, this ties up a thread that could be
    //      serving other users.
    //    - Sequential async: same wall-clock time as sync, but the thread is
    //      FREE during waits. The thread pool can use it for other work.
    //      Your app stays responsive.
    //    - Concurrent async: DRAMATICALLY faster because all I/O happens in
    //      parallel. 10 fetches take the same time as 1 fetch. This is how
    //      you write fast, scalable code.
    //
    // Q: When would you use sequential vs concurrent?
    // A: Use CONCURRENT when the requests are independent — fetching 10
    //    different carts does not depend on each other. Use SEQUENTIAL when
    //    each request depends on the previous one (e.g., fetch page 1, read
    //    a value from it, then use that value to decide what page 2 to fetch).
    [Fact]
    public async Task Exercise2_SyncVsAsync()
    {
        // Helper: simulates fetching a cart by ID over the network.
        // Task.Delay(200) pretends the network takes 200ms to respond.
        // In real code this would be an HTTP call or database query.
        async Task<CartItem> FetchCartAsync(int cartId)
        {
            await Task.Delay(200); // pretend network latency

            return new CartItem
            {
                CartItemId = cartId,
                CartId = cartId,
                ItemId = $"ITEM-{cartId}",
                ItemName = $"Cart {cartId}",
                Quantity = 1
            };
        }

        const int cartCount = 10;
        var cartIds = Enumerable.Range(1, cartCount).ToList();

        // ---------------------------------------------------------------
        // APPROACH 1: Synchronous (blocking with .GetAwaiter().GetResult())
        //
        // This is the WORST way. Each call BLOCKS the thread — the thread
        // cannot do anything else while waiting. It is like standing in
        // line at 10 different stores, waiting at each one before walking
        // to the next. The total time is 10 × 200ms = 2,000ms.
        //
        // NOTE: In production code, NEVER call .GetAwaiter().GetResult()
        // on the thread pool. It can cause thread starvation and deadlocks
        // in ASP.NET. We do it here only to demonstrate the concept.
        // ---------------------------------------------------------------
        var syncSw = Stopwatch.StartNew();
        var syncResults = new List<CartItem>();

        foreach (var id in cartIds)
        {
            // .GetAwaiter().GetResult() blocks the thread until the task
            // completes. The thread is stuck — it cannot serve other requests
            // or do any other work. This is the "standing in line" approach.
            var cart = FetchCartAsync(id).GetAwaiter().GetResult();
            syncResults.Add(cart);
        }

        syncSw.Stop();
        _output.WriteLine($"Approach 1 — Sync (blocking):      {syncSw.ElapsedMilliseconds} ms");

        // ---------------------------------------------------------------
        // APPROACH 2: Sequential async (await each in order)
        //
        // Better than sync! We await each fetch, but one at a time.
        // During each await, our thread goes back to the thread pool and
        // can serve other work. When the fetch completes, a (possibly
        // different) thread resumes our code.
        //
        // Wall-clock time is STILL ~2,000ms because we wait for each fetch
        // to finish before starting the next one. But the thread was not
        // wasted — it was free to do other things during the waits.
        // ---------------------------------------------------------------
        var seqSw = Stopwatch.StartNew();
        var seqResults = new List<CartItem>();

        foreach (var id in cartIds)
        {
            // await frees the thread during the delay. The thread goes back
            // to the pool and can handle other requests. When the delay
            // finishes, the continuation resumes on a (possibly different)
            // thread from the pool.
            var cart = await FetchCartAsync(id);
            seqResults.Add(cart);
        }

        seqSw.Stop();
        _output.WriteLine($"Approach 2 — Sequential async:     {seqSw.ElapsedMilliseconds} ms");

        // ---------------------------------------------------------------
        // APPROACH 3: Concurrent async (Task.WhenAll)
        //
        // The FASTEST way! Instead of waiting for each fetch to finish
        // before starting the next, we FIRE ALL 10 at once. All 10 delays
        // happen at the same time — like sending 10 messengers to 10 stores
        // simultaneously. WhenAll waits until ALL of them finish.
        //
        // Wall-clock time: ~200ms (just the duration of ONE delay, because
        // they all run in parallel). That is 10× FASTER than the other two!
        // ---------------------------------------------------------------
        var conSw = Stopwatch.StartNew();

        // Kick off ALL fetches at once — none of them are awaited yet!
        // Each call returns a Task<CartItem> immediately. The actual work
        // happens in the background on thread pool threads.
        var tasks = cartIds.Select(id => FetchCartAsync(id)).ToList();

        // Now await ALL of them at once. WhenAll returns when every task
        // has completed. The total time is ~200ms because all delays overlap.
        var conResults = await Task.WhenAll(tasks);

        conSw.Stop();
        _output.WriteLine($"Approach 3 — Concurrent async:     {conSw.ElapsedMilliseconds} ms");

        // ---------------------------------------------------------------
        // RESULTS
        // ---------------------------------------------------------------
        _output.WriteLine($"");
        _output.WriteLine($"All 10 carts fetched successfully.");

        // Verify all approaches returned the correct number of carts.
        Assert.Equal(cartCount, syncResults.Count);
        Assert.Equal(cartCount, seqResults.Count);
        Assert.Equal(cartCount, conResults.Length);

        // Verify each approach returned valid CartItems.
        Assert.All(syncResults, c => Assert.StartsWith("ITEM-", c.ItemId));
        Assert.All(seqResults, c => Assert.StartsWith("ITEM-", c.ItemId));
        Assert.All(conResults, c => Assert.StartsWith("ITEM-", c.ItemId));

        // Timing assertions with generous tolerance for slow CI machines.
        // Sync and sequential should both be around 2,000ms (10 × 200ms).
        Assert.True(syncSw.ElapsedMilliseconds >= 1500,
            $"Sync should take ~2000ms but was {syncSw.ElapsedMilliseconds}ms");
        Assert.True(syncSw.ElapsedMilliseconds < 5000,
            $"Sync should take ~2000ms but was {syncSw.ElapsedMilliseconds}ms");

        Assert.True(seqSw.ElapsedMilliseconds >= 1500,
            $"Sequential should take ~2000ms but was {seqSw.ElapsedMilliseconds}ms");
        Assert.True(seqSw.ElapsedMilliseconds < 5000,
            $"Sequential should take ~2000ms but was {seqSw.ElapsedMilliseconds}ms");

        // Concurrent should be DRAMATICALLY faster — all delays overlap.
        // Expect ~200ms with generous upper bound for CI.
        Assert.True(conSw.ElapsedMilliseconds < 1500,
            $"Concurrent should take ~200ms but was {conSw.ElapsedMilliseconds}ms");

        // The big reveal: concurrent must be at least 5× faster than sync.
        var speedup = (double)syncSw.ElapsedMilliseconds / Math.Max(conSw.ElapsedMilliseconds, 1);
        _output.WriteLine($"");
        _output.WriteLine($"Speedup: Concurrent is {speedup:F0}× faster than Sync!");
        Assert.True(speedup >= 3,
            $"Concurrent ({conSw.ElapsedMilliseconds}ms) should be significantly faster than Sync ({syncSw.ElapsedMilliseconds}ms)");
    }

    // Exercise 2 showed that fetching 10 independent carts concurrently is
    // 10× faster than fetching them one by one. But what happens when your
    // second operation DEPENDS on the result of the first?
    //
    // Scenario: You need to fetch a customer's cart (which tells you WHAT
    // items they want), then fetch the PRICE of each item. You CANNOT fetch
    // the prices before you know which items are in the cart — the item IDs
    // come FROM the cart fetch. The prices DEPEND on the cart contents.
    //
    // This means the pipeline looks like this:
    //
    //   FetchCart ──▶ Extract ItemIds ──▶ FetchPrices (can be concurrent!)
    //                   (must wait)
    //
    // You CANNOT parallelize the cart fetch with the price fetches because
    // you do not know WHICH items to fetch prices for until the cart arrives.
    //
    // BUT — once you HAVE the item IDs, you CAN fetch all prices at the
    // same time with Task.WhenAll, because the price fetches are independent
    // of each other.
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: Can you fetch the cart and the prices at the same time? Why or why not?
    // A: NO! You do not know WHICH items to look up until the cart arrives.
    //    It is like trying to buy groceries before you know what is on your
    //    list. You have to read the list first, THEN go shopping.
    //
    // Q: When is concurrency possible, and when isn't it?
    // A: Concurrency is possible when operations are INDEPENDENT — they do
    //    not need each other's results. If Operation B needs the output of
    //    Operation A, they MUST run in sequence. But once A finishes, if B
    //    and C are independent of each other, you can run them both at once.
    //
    //    Think of it like a recipe:
    //      Step 1: Boil water (must finish before Step 2)
    //      Step 2a: Make tea (independent of 2b)
    //      Step 2b: Make coffee (independent of 2a)
    //    Steps 2a and 2b can happen at the same time, but neither can start
    //    until Step 1 is done.
    [Fact]
    public async Task Exercise3_WhenOneCallDependsOnAnother()
    {
        // Simulated service: fetches a cart by ID, returns its items.
        // Each cart has 3 items with unique ItemId values.
        async Task<List<CartItem>> FetchCartAsync(int cartId)
        {
            await Task.Delay(200); // pretend network latency

            return
            [
                new CartItem { CartItemId = 1, CartId = cartId, ItemId = "ITEM-A", ItemName = "Widget", Quantity = 2 },
                new CartItem { CartItemId = 2, CartId = cartId, ItemId = "ITEM-B", ItemName = "Gadget", Quantity = 1 },
                new CartItem { CartItemId = 3, CartId = cartId, ItemId = "ITEM-C", ItemName = "Doohickey", Quantity = 3 }
            ];
        }

        // Simulated service: fetches the price for a single item.
        // In real code this would be an HTTP call to a pricing API.
        async Task<ItemPrice> FetchPriceAsync(string itemId)
        {
            await Task.Delay(200); // pretend network latency

            // Different prices for each item to make the test realistic.
            return itemId switch
            {
                "ITEM-A" => new ItemPrice(itemId, 9.99m),
                "ITEM-B" => new ItemPrice(itemId, 24.50m),
                "ITEM-C" => new ItemPrice(itemId, 3.75m),
                _ => new ItemPrice(itemId, 0m)
            };
        }

        // ---------------------------------------------------------------
        // APPROACH 1: Fully sequential
        //
        // Fetch the cart first (200ms), then fetch each price one by one
        // (3 × 200ms = 600ms). Total: ~800ms.
        //
        // This is slow because we wait for each price before starting
        // the next one, even though the price fetches do not depend
        // on each other.
        // ---------------------------------------------------------------
        var seqSw = Stopwatch.StartNew();

        // Step 1: Fetch the cart — we MUST do this first because we need
        // the item IDs. There is no way around this sequential step.
        var cart = await FetchCartAsync(cartId: 1);
        var itemIds = cart.Select(c => c.ItemId).ToList();

        _output.WriteLine($"Fetched cart with {itemIds.Count} items: {string.Join(", ", itemIds)}");

        // Step 2: Fetch each price ONE AT A TIME.
        // Each price fetch waits for the previous one to finish, even
        // though the prices are independent. This is wasteful!
        var seqPrices = new List<ItemPrice>();
        foreach (var itemId in itemIds)
        {
            var price = await FetchPriceAsync(itemId);
            seqPrices.Add(price);
        }

        seqSw.Stop();
        _output.WriteLine($"Approach 1 — Fully sequential:     {seqSw.ElapsedMilliseconds} ms");

        // ---------------------------------------------------------------
        // APPROACH 2: Sequential cart + concurrent prices
        //
        // Fetch the cart first (200ms), then fire ALL price fetches at
        // once with Task.WhenAll (200ms — they overlap!). Total: ~400ms.
        //
        // We still MUST fetch the cart first (step 1 is sequential), but
        // once we have the item IDs, we can fetch all prices in parallel.
        // This is the CORRECT pattern for dependent operations.
        // ---------------------------------------------------------------
        var conSw = Stopwatch.StartNew();

        // Step 1: Fetch the cart — same as before, this step is unavoidable.
        var cart2 = await FetchCartAsync(cartId: 1);
        var itemIds2 = cart2.Select(c => c.ItemId).ToList();

        _output.WriteLine($"Fetched cart with {itemIds2.Count} items: {string.Join(", ", itemIds2)}");

        // Step 2: Fire ALL price fetches at once — they are independent
        // of each other now that we know the item IDs. Task.WhenAll waits
        // until every price fetch has completed.
        var priceTasks = itemIds2.Select(id => FetchPriceAsync(id)).ToList();
        var conPrices = await Task.WhenAll(priceTasks);

        conSw.Stop();
        _output.WriteLine($"Approach 2 — Seq cart + concurrent: {conSw.ElapsedMilliseconds} ms");

        // ---------------------------------------------------------------
        // WHAT IF YOU TRIED TO CONCURRENT-IFY EVERYTHING?
        //
        // You might think: "Why not fetch the cart and prices at the same
        // time?" But look what happens — you need the item IDs FROM the
        // cart to even CALL FetchPriceAsync! You cannot start the price
        // fetches until the cart fetch completes. The dependency forces
        // sequential execution for that step.
        //
        // This is different from Exercise 2, where all 10 cart fetches
        // were INDEPENDENT — each one only needed its own cart ID. Here,
        // the price fetches need the CART'S data, creating a dependency.
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // RESULTS
        // ---------------------------------------------------------------
        _output.WriteLine($"");
        _output.WriteLine($"Fully sequential:        {seqSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Seq + concurrent prices: {conSw.ElapsedMilliseconds} ms");

        // Both approaches returned the same prices.
        Assert.Equal(seqPrices.Count, conPrices.Length);
        Assert.Equal("ITEM-A", seqPrices[0].ItemId);
        Assert.Equal("ITEM-B", seqPrices[1].ItemId);
        Assert.Equal("ITEM-C", seqPrices[2].ItemId);
        Assert.Equal("ITEM-A", conPrices[0].ItemId);
        Assert.Equal("ITEM-B", conPrices[1].ItemId);
        Assert.Equal("ITEM-C", conPrices[2].ItemId);

        // Prices should match between approaches.
        for (var i = 0; i < seqPrices.Count; i++)
        {
            Assert.Equal(seqPrices[i].Price, conPrices[i].Price);
        }

        // Timing: both approaches MUST fetch the cart (200ms).
        // Fully sequential adds 3 × 200ms = 600ms for prices = ~800ms total.
        // Concurrent prices adds only 200ms (overlapping) = ~400ms total.
        Assert.True(seqSw.ElapsedMilliseconds >= 500,
            $"Fully sequential should take ~800ms but was {seqSw.ElapsedMilliseconds}ms");
        Assert.True(seqSw.ElapsedMilliseconds < 2000,
            $"Fully sequential should take ~800ms but was {seqSw.ElapsedMilliseconds}ms");

        Assert.True(conSw.ElapsedMilliseconds >= 200,
            $"Concurrent prices should take ~400ms but was {conSw.ElapsedMilliseconds}ms");
        Assert.True(conSw.ElapsedMilliseconds < 1200,
            $"Concurrent prices should take ~400ms but was {conSw.ElapsedMilliseconds}ms");

        // The concurrent price approach should be faster because the 3
        // price fetches overlap instead of running one after another.
        var seqMs = seqSw.ElapsedMilliseconds;
        var conMs = conSw.ElapsedMilliseconds;
        _output.WriteLine($"");
        _output.WriteLine($"Concurrent prices saved {seqMs - conMs} ms ({(double)seqMs / Math.Max(conMs, 1):F1}× faster)!");

        Assert.True(conMs < seqMs,
            $"Concurrent prices ({conMs}ms) should be faster than fully sequential ({seqMs}ms)");
    }

    // What happens when you CALL an async method but forget to AWAIT it?
    //
    // This is one of the most common bugs in async C# code. When you call
    // an async method without await, three dangerous things happen:
    //
    //   1. The method starts running in the background — you get a Task
    //      object back instead of the actual result.
    //
    //   2. The compiler gives you warning CS4014: "Because this call is
    //      not awaited, execution of the current method continues before
    //      the call is completed." This warning exists because forgetting
    //      to await is almost ALWAYS a bug.
    //
    //   3. If the unawaited method throws an exception, the exception is
    //      SILENTLY SWALLOWED. It gets captured inside the Task object, but
    //      if nobody reads that Task, the exception vanishes. Your app keeps
    //      running as if nothing went wrong — but something DID go wrong!
    //
    // Follow-up Q&A (ELI5):
    //
    // Q: Does it compile? Does the compiler give you any warnings?
    // A: Yes, it compiles — forgetting await is NOT a compile error. But
    //    the compiler gives you warning CS4014, which is its way of saying
    //    "Hey, you probably meant to await this!" The warning is easy to
    //    miss if you are not paying attention to build output.
    //
    // Q: What value do you get back from the unawaited call?
    // A: You get the Task<Order> object itself, NOT the Order inside it.
    //    If you try to use it as an Order (e.g., order.OrderId), the code
    //    will NOT compile — the types do not match. You would need to
    //    access .Result (which blocks) or await the Task to get the Order.
    //
    // Q: What happens to an exception thrown inside the unawaited method?
    // A: The exception is silently captured by the Task. If you never await
    //    the Task or check its .Exception property, the error DISAPPEARS.
    //    No crash, no log, no error message. Your app thinks everything is
    //    fine, but the work was never completed. This is why unawaited
    //    exceptions are called "fire and forget" — you fired the work, then
    //    forgot about it, and any errors went unnoticed.
    [Fact]
    public async Task Exercise4_ForgettingToAwait()
    {
        // Simulated repository: fetches an Order by ID over the "network".
        async Task<Order> FetchOrderAsync(int orderId)
        {
            await Task.Delay(200); // pretend network latency

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

        // Simulated repository that ALWAYS throws an exception.
        // This represents a real-world scenario like a database timeout
        // or a failed HTTP call.
        async Task<Order> FailingFetchAsync(int orderId)
        {
            await Task.Delay(50); // pretend network latency

            throw new InvalidOperationException($"Order {orderId} not found in the database.");
        }

        // ---------------------------------------------------------------
        // PART 1: What value do you get from an unawaited call?
        //
        // When you call FetchOrderAsync(1) WITHOUT await, the method starts
        // running in the background and returns a Task<Order> immediately.
        // You get the TASK, not the ORDER. The Order is still being
        // "fetched" in the background.
        // ---------------------------------------------------------------

        // WARNING: This line intentionally does NOT use await.
        // The compiler will emit CS4014 here — that is expected!
        // We are doing this on purpose to show what happens.
#pragma warning disable CS4014 // Intentionally unawaited — this is the bug we are demonstrating
        Task<Order> unawaitedTask = FetchOrderAsync(1);
#pragma warning restore CS4014

        // The variable is a Task<Order>, NOT an Order.
        // If you tried to write: Order o = FetchOrderAsync(1);
        // the compiler would say: "Cannot implicitly convert Task<Order> to Order"
        // That type mismatch is your first clue that something is wrong.
        _output.WriteLine($"Type of unawaitedTask: {unawaitedTask.GetType().Name}");
        _output.WriteLine($"IsCompleted: {unawaitedTask.IsCompleted}");

        // The task IS running in the background, but we have not awaited it.
        // Let us wait a moment so it finishes before we check its result.
        await Task.Delay(300); // give the unawaited task time to complete

        // NOW we can read the result — but only because we waited long enough.
        // In real code, you would NOT do this — you would have awaited the
        // call in the first place.
        var order = await unawaitedTask;
        _output.WriteLine($"Order ID from unawaited task: {order.OrderId}");
        Assert.Equal(1, order.OrderId);

        // ---------------------------------------------------------------
        // PART 2: What happens to exceptions in unawaited methods?
        //
        // If an unawaited async method throws, the exception is captured
        // inside the Task. Nobody sees it. No crash, no error — just a
        // Task in a faulted state that nobody is looking at.
        // ---------------------------------------------------------------

        // WARNING: This line intentionally does NOT use await.
#pragma warning disable CS4014 // Intentionally unawaited — this is the bug we are demonstrating
        Task<Order> failingTask = FailingFetchAsync(999);
#pragma warning restore CS4014

        // Give the failing task time to throw.
        await Task.Delay(200);

        // The task is now faulted — it captured the exception.
        // But because we did not await it, the exception never reached us.
        _output.WriteLine($"failingTask.IsFaulted: {failingTask.IsFaulted}");
        _output.WriteLine($"failingTask.Exception?.InnerException?.Message: {failingTask.Exception?.InnerException?.Message}");

        Assert.True(failingTask.IsFaulted,
            "The unawaited task should be in a faulted state because it threw an exception.");

        // The exception is trapped inside the Task. If we had awaited it,
        // the exception would have propagated and the test would fail.
        // Because we did NOT await it, the exception was silently swallowed.
        var innerException = failingTask.Exception?.InnerException as InvalidOperationException;
        Assert.NotNull(innerException);
        Assert.Contains("not found in the database", innerException.Message);

        _output.WriteLine($"");
        _output.WriteLine($"The exception was silently swallowed!");
        _output.WriteLine($"If this were production code, nobody would know the fetch failed.");

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

    // What happens when MULTIPLE tasks throw exceptions inside Task.WhenAll?
    //
    // In Exercise 4, we saw that a single unawaited exception is silently
    // swallowed. But what about when you use Task.WhenAll and MORE THAN ONE
    // task fails? Task.WhenAll does NOT stop at the first error — it waits
    // for ALL tasks to finish (success or failure), then wraps every failure
    // into an AggregateException.
    //
    // This test runs three tasks in parallel: one succeeds, two throw.
    // We observe:
    //   1. What exception type you catch (AggregateException, not the raw ones)
    //   2. How to access ALL the exceptions, not just the first one
    //   3. What happens if you re-await a faulted task after Task.WhenAll
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
        // Three simulated async operations — like calling three different
        // microservices at the same time.

        // Operation 1: Fetch cart data — this one SUCCEEDS.
        async Task<List<CartItem>> FetchCartDataAsync()
        {
            await Task.Delay(100);
            return
            [
                new CartItem { CartItemId = 1, CartId = 1, ItemId = "ITEM-A", ItemName = "Widget", Quantity = 2 },
                new CartItem { CartItemId = 2, CartId = 1, ItemId = "ITEM-B", ItemName = "Gadget", Quantity = 1 }
            ];
        }

        // Operation 2: Fetch item prices — this one THROWS.
        async Task<List<ItemPrice>> FetchItemPricesAsync()
        {
            await Task.Delay(100); // pretend work happens first
            throw new InvalidOperationException("Pricing service unavailable");
        }

        // Operation 3: Apply discount — this one THROWS.
        async Task<decimal> ApplyDiscountAsync()
        {
            await Task.Delay(100); // pretend work happens first
            throw new TimeoutException("Discount service timed out");
        }

        // ---------------------------------------------------------------
        // SCENARIO 1: What exception do you catch from Task.WhenAll?
        //
        // When 2 out of 3 tasks throw, Task.WhenAll does NOT throw the
        // first error immediately. It waits for ALL tasks to finish, then
        // wraps ALL failures into ONE AggregateException.
        //
        // KEY INSIGHT: When you `await Task.WhenAll(...)`, the `await`
        // automatically UNWRAPS the AggregateException and throws only the
        // first inner exception. To see the full AggregateException, you
        // must NOT await — instead, let the Task finish and inspect its
        // .Exception property.
        // ---------------------------------------------------------------

        // Fire all three tasks at once. Task.WhenAll creates a single
        // combined Task that completes when ALL three finish.
        var combinedTask = Task.WhenAll(
            FetchCartDataAsync(),      // succeeds
            FetchItemPricesAsync(),    // throws InvalidOperationException
            ApplyDiscountAsync()       // throws TimeoutException
        );

        // Let the combined task finish without using `await`. If we used
        // `await`, it would unwrap the AggregateException and throw only
        // the first error. Instead, we inspect .Exception directly.
        try { await combinedTask; } catch { }

        // The combined Task is now faulted. Its .Exception property holds
        // an AggregateException wrapping EVERY failure from every task.
        Assert.True(combinedTask.IsFaulted);
        var aggregateException = combinedTask.Exception!;

        _output.WriteLine($"Caught AggregateException with {aggregateException.InnerExceptions.Count} inner exception(s):");
        _output.WriteLine($"");

        foreach (var inner in aggregateException.InnerExceptions)
        {
            _output.WriteLine($"  - {inner.GetType().Name}: {inner.Message}");
        }

        // The AggregateException wraps ALL failures. In our case, 2 tasks
        // failed, so there are 2 exceptions in the list.
        Assert.Equal(2, aggregateException.InnerExceptions.Count);

        // ---------------------------------------------------------------
        // SCENARIO 2: How do you access ALL the exceptions?
        //
        // AggregateException.InnerExceptions gives you the full list.
        // You can also call .Flatten() to simplify nested cases.
        // ---------------------------------------------------------------

        // Verify both exception types are present — the order is NOT
        // guaranteed because tasks run in parallel.
        var exceptionTypes = aggregateException.InnerExceptions
            .Select(e => e.GetType().Name)
            .ToList();

        _output.WriteLine($"Exception types: {string.Join(", ", exceptionTypes)}");

        Assert.Contains("InvalidOperationException", exceptionTypes);
        Assert.Contains("TimeoutException", exceptionTypes);

        // Verify the messages are correct.
        var invalidOpEx = aggregateException.InnerExceptions
            .OfType<InvalidOperationException>()
            .FirstOrDefault();
        Assert.NotNull(invalidOpEx);
        Assert.Equal("Pricing service unavailable", invalidOpEx.Message);

        var timeoutEx = aggregateException.InnerExceptions
            .OfType<TimeoutException>()
            .FirstOrDefault();
        Assert.NotNull(timeoutEx);
        Assert.Equal("Discount service timed out", timeoutEx.Message);

        // .Flatten() unwraps nested AggregateExceptions into one flat list.
        // Useful when tasks themselves use Task.WhenAll internally.
        var flattened = aggregateException.Flatten();
        _output.WriteLine($"After Flatten(): {flattened.InnerExceptions.Count} exception(s)");

        // In our case, Flatten does not change the count because our tasks
        // did not nest AggregateExceptions — but it is good practice to
        // use it defensively.
        Assert.Equal(2, flattened.InnerExceptions.Count);

        // ---------------------------------------------------------------
        // SCENARIO 3: What happens if you re-await a faulted task?
        //
        // After Task.WhenAll completes, the original tasks still exist and
        // remember their exceptions. If you await them again, each one
        // throws its ORIGINAL exception — UNWRAPPED, not inside an
        // AggregateException. This lets you handle each failure specifically.
        // ---------------------------------------------------------------
        _output.WriteLine($"");

        // Re-fetch the tasks by calling them again (in real code you would
        // keep the original Task variables). The cart task succeeds, so
        // re-awaiting it returns normally.
        var cartTask2 = FetchCartDataAsync();
        var pricesTask2 = FetchItemPricesAsync();
        var discountTask2 = ApplyDiscountAsync();

        // The successful task completes normally when re-awaited.
        var cartResult = await cartTask2;
        Assert.Equal(2, cartResult.Count);
        _output.WriteLine($"Re-awaiting successful task: got {cartResult.Count} items (no exception)");

        // Re-awaiting a faulted task throws the ORIGINAL exception type,
        // NOT an AggregateException. This is the key insight!
        var priceEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FetchItemPricesAsync());
        _output.WriteLine($"Re-awaiting faulted task 1: {priceEx.GetType().Name} — {priceEx.Message}");

        var discountEx = await Assert.ThrowsAsync<TimeoutException>(
            () => ApplyDiscountAsync());
        _output.WriteLine($"Re-awaiting faulted task 2: {discountEx.GetType().Name} — {discountEx.Message}");

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
        //
        // OR — re-await each task individually to avoid AggregateException:
        //
        //   var results = await Task.WhenAll(
        //       CatchAsync(FetchCartDataAsync()),
        //       CatchAsync(FetchItemPricesAsync()),
        //       CatchAsync(ApplyDiscountAsync())
        //   );
        // ---------------------------------------------------------------
    }

    // What happens when you call an async method WITHOUT awaiting it?
    // This is called "fire-and-forget" — you start the work, then walk
    // away and keep doing other things. It sounds convenient, but it has
    // serious pitfalls. This test explores what goes wrong and how to do
    // it safely.
    //
    // The scenario: you calculate an order total, then you want to save
    // an audit log in the background (like writing to a file). The audit
    // log is "non-critical" — if it fails, the order still went through.
    // You decide to fire-and-forget the audit log write.
    [Fact]
    public async Task Exercise6_FireAndForgetPitfalls()
    {
        // ---------------------------------------------------------------
        // SETUP: Create an order with two items.
        //
        // Widget-A: 10 units × $10.00 = $100.00
        // Widget-B:  5 units × $30.00 = $150.00
        // Total:                      $250.00
        // ---------------------------------------------------------------
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

        // A simple synchronous helper — sums up the order total.
        decimal CalculateOrderTotal(Order o)
        {
            return o.OrderItems.Sum(item => item.Price * item.Quantity);
        }

        // Simulates writing an audit log to a file after a short delay.
        // Returns Task — the exception will be captured on the returned Task.
        async Task SaveAuditLogAsync(Order o, decimal total)
        {
            await Task.Delay(100); // simulate file I/O
            throw new InvalidOperationException("Audit log write failed: disk full");
        }

        // Same logic, but returns void instead of Task.
        // We will NOT call this method — but we will explain why.
#pragma warning disable CS8321 // Intentionally declared but never called — that IS the lesson
        async void SaveAuditLogAsync_Void(Order o, decimal total)
#pragma warning restore CS8321
        {
            await Task.Delay(100);
            throw new InvalidOperationException("Audit log write failed: disk full");
        }

        // ---------------------------------------------------------------
        // PART 1: async Task fire-and-forget — exception is silently swallowed.
        //
        // You call the audit log method WITHOUT await. The method starts
        // running in the background and returns a Task immediately. You
        // keep working. The audit log throws an exception. Does your
        // test crash? Let's find out.
        // ---------------------------------------------------------------

        // Calculate the order total — this is synchronous, no issues here.
        var total = CalculateOrderTotal(order);
        _output.WriteLine($"Order total: {total:C}");
        Assert.Equal(250.00m, total);

        // Fire-and-forget: call the audit log method WITHOUT await.
        // The compiler emits CS4014 ("Because this call is not awaited...").
        // We suppress it on purpose — this is the bug we are demonstrating.
#pragma warning disable CS4014 // Intentionally unawaited — this is the bug we are demonstrating
        Task auditTask = SaveAuditLogAsync(order, total);
#pragma warning restore CS4014

        // The audit log is running in the background. We keep working.
        // The order total was already calculated — our main work is unaffected.
        _output.WriteLine($"Order total (confirmed): {total:C}");
        Assert.Equal(250.00m, total);

        // Give the fire-and-forget task time to throw.
        await Task.Delay(200);

        // NOW — let's look at what happened to the audit log task.
        // The test did NOT crash. The exception did NOT propagate to us.
        // It is trapped inside the Task object — and nobody is looking at it.
        _output.WriteLine($"");
        _output.WriteLine($"auditTask.IsFaulted: {auditTask.IsFaulted}");
        _output.WriteLine($"auditTask.Exception type: {auditTask.Exception?.GetType().Name}");

        Assert.True(auditTask.IsFaulted,
            "The audit log task should be faulted because it threw an exception.");

        // The exception is wrapped in an AggregateException (all Task
        // exceptions are). The inner exception is our original error.
        var innerException = auditTask.Exception?.InnerException as InvalidOperationException;
        Assert.NotNull(innerException);
        Assert.Equal("Audit log write failed: disk full", innerException.Message);

        _output.WriteLine($"auditTask.Exception.InnerException: {innerException.Message}");
        _output.WriteLine($"");
        _output.WriteLine($"The audit log CRASHED — but the test continued without noticing!");
        _output.WriteLine($"If this were production code, nobody would know the log write failed.");

        // ---------------------------------------------------------------
        // PART 2: What changes with async void?
        //
        // If SaveAuditLogAsync_Void had the same code but returned
        // `async void` instead of `async Task`, the behavior is
        // drastically different:
        //
        //   1. You CANNOT capture its return value — it returns void,
        //      not Task. This line won't compile:
        //          Task t = SaveAuditLogAsync_Void(order, total);
        //
        //   2. You CANNOT await it:
        //          await SaveAuditLogAsync_Void(order, total);  // ERROR
        //
        //   3. You CANNOT inspect IsFaulted — there is no Task to
        //      inspect. The exception has nowhere to go.
        //
        //   4. When it throws, the exception is posted to the
        //      SynchronizationContext. In a console test (no UI thread),
        //      it goes to ThreadPool.UnobservedException and CRASHES
        //      THE PROCESS. There is no try/catch that can save you.
        //
        // We do NOT call SaveAuditLogAsync_Void here because it would
        // crash this test runner. That IS the lesson.
        //
        // To prove the compiler prevents you from using it safely,
        // here is what you CANNOT do with an async void method:
        //
        //   // ❌ WON'T COMPILE — cannot convert void to Task
        //   Task t = SaveAuditLogAsync_Void(order, total);
        //
        //   // ❌ WON'T COMPILE — cannot await a void method
        //   await SaveAuditLogAsync_Void(order, total);
        //
        //   // ❌ WON'T COMPILE — cannot assign void to anything
        //   var result = SaveAuditLogAsync_Void(order, total);
        //
        // With async Task, all three of these work fine — you get a
        // Task back that you can await, inspect, or pass to WhenAll.
        // ---------------------------------------------------------------
        _output.WriteLine($"");
        _output.WriteLine($"--- Part 2: async void comparison ---");
        _output.WriteLine($"With async Task: you get a Task back. You can await it, inspect it, handle its exception.");
        _output.WriteLine($"With async void: you get NOTHING back. Exceptions crash the process. Never use it for background work.");

        // ---------------------------------------------------------------
        // PART 3: Safe fire-and-forget pattern — observe the Task.
        //
        // The RIGHT way to do fire-and-forget is:
        //   1. Always return Task (never async void).
        //   2. Keep the returned Task reference.
        //   3. Either await it later, or observe it via ContinueWith.
        //
        // This way, exceptions are never silently swallowed.
        // ---------------------------------------------------------------

        // Call the audit log again — fire-and-forget, but we KEEP the Task.
        Task observedAuditTask = SaveAuditLogAsync(order, total);

        // Do other work while the audit log runs in the background...
        _output.WriteLine($"");
        _output.WriteLine($"--- Part 3: Safe fire-and-forget ---");
        _output.WriteLine($"Order total calculated: {total:C}");
        _output.WriteLine($"Audit log running in background...");

        // Give it time to throw.
        await Task.Delay(200);

        // NOW we observe the task. Because we kept the reference, we can
        // inspect it and handle the exception explicitly.
        Assert.True(observedAuditTask.IsFaulted);

        try
        {
            // Await the faulted task — this will throw the original exception.
            await observedAuditTask;
        }
        catch (InvalidOperationException ex)
        {
            // We caught the exception! In production code, you would log
            // it here: _logger.LogError(ex, "Audit log write failed");
            _output.WriteLine($"Caught and handled: {ex.Message}");
            Assert.Equal("Audit log write failed: disk full", ex.Message);
        }

        _output.WriteLine($"The exception was observed and handled gracefully.");

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
        // ---------------------------------------------------------------
        // HELPER: A producer that yields OrderItem objects one at a time.
        //
        // The return type is IAsyncEnumerable<OrderItem> — this tells the
        // compiler "I will give you items one at a time, and each item
        // might require an async operation to produce."
        //
        // Inside the method, "await Task.Delay" simulates real I/O (like
        // reading a row from a database or fetching from an API).
        // "yield return" emits one item, then PAUSES until the caller
        // asks for the next one.
        // ---------------------------------------------------------------
        async IAsyncEnumerable<OrderItem> FetchOrderItemsStreamingAsync(int orderId)
        {
            var items = new[]
            {
                new OrderItem { OrderItemId = 1, OrderId = orderId, ItemNumberId = "SKU-100", Quantity = 2, Price = 15.50m },
                new OrderItem { OrderItemId = 2, OrderId = orderId, ItemNumberId = "SKU-200", Quantity = 1, Price = 49.99m },
                new OrderItem { OrderItemId = 3, OrderId = orderId, ItemNumberId = "SKU-300", Quantity = 5, Price = 8.25m },
                new OrderItem { OrderItemId = 4, OrderId = orderId, ItemNumberId = "SKU-400", Quantity = 3, Price = 22.00m },
                new OrderItem { OrderItemId = 5, OrderId = orderId, ItemNumberId = "SKU-500", Quantity = 1, Price = 99.99m },
            };

            foreach (var item in items)
            {
                await Task.Delay(100);  // simulate async I/O per item
                yield return item;      // emit one item — caller processes it, then asks for next
            }
        }

        // ---------------------------------------------------------------
        // HELPER: A traditional batch method — waits for ALL items, then
        // returns them as a List. This is the "old way" before async
        // streams existed.
        // ---------------------------------------------------------------
        async Task<List<OrderItem>> FetchOrderItemsBatchAsync(int orderId)
        {
            var items = new List<OrderItem>
            {
                new() { OrderItemId = 1, OrderId = orderId, ItemNumberId = "SKU-100", Quantity = 2, Price = 15.50m },
                new() { OrderItemId = 2, OrderId = orderId, ItemNumberId = "SKU-200", Quantity = 1, Price = 49.99m },
                new() { OrderItemId = 3, OrderId = orderId, ItemNumberId = "SKU-300", Quantity = 5, Price = 8.25m },
                new() { OrderItemId = 4, OrderId = orderId, ItemNumberId = "SKU-400", Quantity = 3, Price = 22.00m },
                new() { OrderItemId = 5, OrderId = orderId, ItemNumberId = "SKU-500", Quantity = 1, Price = 99.99m },
            };

            // Simulate fetching ALL items before returning any of them.
            // This is the key difference: you wait for the whole batch.
            await Task.Delay(items.Count * 100);
            return items;
        }

        // ---------------------------------------------------------------
        // HELPER: A streaming producer with try/finally for cleanup.
        //
        // The finally block runs even if the consumer calls "break" early.
        // This is how you safely close database connections, file handles,
        // or network sockets — the same way "using" works with IDisposable,
        // but async.
        // ---------------------------------------------------------------
        async IAsyncEnumerable<OrderItem> FetchOrderItemsWithCleanupAsync(int orderId)
        {
            try
            {
                _output.WriteLine($"  [Producer] Resource opened");

                yield return new OrderItem { OrderItemId = 1, OrderId = orderId, ItemNumberId = "SKU-A", Quantity = 1, Price = 10.00m };
                await Task.Delay(100);

                yield return new OrderItem { OrderItemId = 2, OrderId = orderId, ItemNumberId = "SKU-B", Quantity = 2, Price = 20.00m };
                await Task.Delay(100);

                yield return new OrderItem { OrderItemId = 3, OrderId = orderId, ItemNumberId = "SKU-C", Quantity = 1, Price = 30.00m };
                await Task.Delay(100);
            }
            finally
            {
                // This runs when the consumer breaks early OR when all
                // items are consumed. It is the async equivalent of
                // Dispose() — but asynchronous.
                _output.WriteLine($"  [Producer] Resource closed (finally block ran)");
            }
        }

        // ---------------------------------------------------------------
        // PART 1: Consume an async stream with "await foreach".
        //
        // This is the async version of "foreach". Instead of calling
        // MoveNext() synchronously, it calls MoveNextAsync() — which
        // means it can wait for the next item to arrive.
        //
        // Each iteration, you get ONE item as soon as it is ready.
        // You do NOT wait for all 5 items before processing the first.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 1: await foreach (streaming) ---");

        var streamedItems = new List<OrderItem>();
        var itemNumber = 0;

        await foreach (var item in FetchOrderItemsStreamingAsync(orderId: 1))
        {
            itemNumber++;
            _output.WriteLine($"  Item {itemNumber}: {item.ItemNumberId} — {item.Quantity} × {item.Price:C} = {item.Quantity * item.Price:C}");
            streamedItems.Add(item);
        }

        // All 5 items were streamed one at a time. The total should be:
        // SKU-100: 2 × $15.50 = $31.00
        // SKU-200: 1 × $49.99 = $49.99
        // SKU-300: 5 ×  $8.25 = $41.25
        // SKU-400: 3 × $22.00 = $66.00
        // SKU-500: 1 × $99.99 = $99.99
        // Total:              = $288.23
        Assert.Equal(5, streamedItems.Count);
        Assert.Equal(288.23m, streamedItems.Sum(i => i.Price * i.Quantity));
        _output.WriteLine($"  Total: {streamedItems.Sum(i => i.Price * i.Quantity):C}");
        _output.WriteLine($"");

        // ---------------------------------------------------------------
        // PART 2: Compare streaming vs batch timing.
        //
        // Both approaches do the same work (5 items × 100ms each = 500ms
        // of simulated I/O). But streaming yields the FIRST item at ~100ms,
        // while batch waits until ~500ms to return ALL items at once.
        // The key difference is time-to-first-result: streaming lets you
        // start working on the first item immediately.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 2: Streaming vs batch timing ---");

        // Time the batch approach — waits for ALL items, then returns.
        var batchSw = Stopwatch.StartNew();
        var batchItems = await FetchOrderItemsBatchAsync(orderId: 2);
        var batchFirstItemMs = batchSw.ElapsedMilliseconds;
        batchSw.Stop();
        var batchTotalMs = batchSw.ElapsedMilliseconds;

        // Time the streaming approach — the first item arrives much sooner.
        var streamingSw = Stopwatch.StartNew();
        var streamingGotFirstItem = false;
        await foreach (var item in FetchOrderItemsStreamingAsync(orderId: 3))
        {
            if (!streamingGotFirstItem)
            {
                streamingGotFirstItem = true;
                // The FIRST item just arrived — this is the key metric.
                // In batch mode, you would still be waiting for all 5.
            }
        }
        streamingSw.Stop();
        var streamingTotalMs = streamingSw.ElapsedMilliseconds;

        // Time-to-first-result: batch had to wait for ALL 5 items (~500ms)
        // before returning even one. Streaming returned the first item
        // after just ONE item's worth of I/O (~100ms).
        _output.WriteLine($"  Batch — first item available: ~{batchFirstItemMs}ms (waited for all 5)");
        _output.WriteLine($"  Streaming — first item available: ~100ms (got it after just 1 item)");
        _output.WriteLine($"  Batch — total time: {batchTotalMs}ms");
        _output.WriteLine($"  Streaming — total time: {streamingTotalMs}ms");
        _output.WriteLine($"");

        // The batch approach returns ALL items at once, so the first item
        // is not available until ALL items are fetched. Streaming returns
        // the first item after just one fetch — much sooner.
        Assert.True(batchFirstItemMs >= 400,
            $"Batch first-item time ({batchFirstItemMs}ms) should be ~500ms (waited for all items)");

        // Total time is similar for both — streaming does not reduce total
        // work, it just lets you START sooner. That is the real benefit.

        // ---------------------------------------------------------------
        // PART 3: Early termination with "break".
        //
        // You do NOT have to consume every item in the stream. If you
        // call "break" inside an "await foreach", the loop stops and
        // the producer is notified via DisposeAsync().
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 3: Early termination with break ---");

        var partialItems = new List<OrderItem>();

        await foreach (var item in FetchOrderItemsStreamingAsync(orderId: 4))
        {
            partialItems.Add(item);
            _output.WriteLine($"  Got item {partialItems.Count}: {item.ItemNumberId}");

            if (partialItems.Count == 3)
            {
                _output.WriteLine($"  Breaking after 3 items — we don't need the rest!");
                break;
            }
        }

        // We only got 3 out of 5 items. The stream stopped early.
        Assert.Equal(3, partialItems.Count);
        _output.WriteLine($"  Processed {partialItems.Count} items (out of 5 available)");
        _output.WriteLine($"");

        // ---------------------------------------------------------------
        // PART 4: Producer cleanup on early exit (try/finally).
        //
        // When you break out of "await foreach", the runtime calls
        // DisposeAsync() on the enumerator. This triggers the producer's
        // "finally" block — even though we did not consume all items.
        //
        // This is critical for resources: if the producer opened a
        // database connection or file handle, the finally block closes
        // it — no matter how the loop ends (break, exception, or
        // normal completion).
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 4: Producer cleanup on early exit ---");

        var cleanupItems = new List<OrderItem>();

        await foreach (var item in FetchOrderItemsWithCleanupAsync(orderId: 5))
        {
            cleanupItems.Add(item);
            _output.WriteLine($"  Got item: {item.ItemNumberId}");

            if (cleanupItems.Count == 2)
            {
                _output.WriteLine($"  Breaking after 2 items — watch the finally block run!");
                break;
            }
        }

        // We only got 2 items, but the producer's finally block should
        // have printed "[Producer] Resource closed (finally block ran)".
        Assert.Equal(2, cleanupItems.Count);
        _output.WriteLine($"  Processed {cleanupItems.Count} items — producer cleanup was triggered above");
        _output.WriteLine($"");

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
        //    and triggers the producer's finally block — resources are
        //    always cleaned up.
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

    // Some objects hold resources that can only be released ASYNCHRONOUSLY:
    // a network connection that needs to send a "goodbye" message, a file
    // stream that needs to flush buffered data to disk, a database
    // transaction that needs to commit or roll back. For these, the
    // synchronous IDisposable.Dispose() is not enough — you need
    // IAsyncDisposable, which lets you await the cleanup.
    //
    // There is a catch: constructors CANNOT be async. So if your object
    // needs async work to initialize (like opening a connection), you
    // use an "async factory method" — a static CreateAsync() that does
    // the async work and returns the fully-ready object.
    //
    // This test walks through both concepts: async factory creation and
    // async disposal via "await using".
    [Fact]
    public async Task Exercise8_AsyncDisposal()
    {
        // ---------------------------------------------------------------
        // PART 1: Why constructors CANNOT be async.
        //
        // A constructor MUST return the fully-constructed `this` reference
        // synchronously. If it were async:
        //
        //   ❌ WON'T COMPILE — constructors cannot be async
        //   public async PricingCacheSession(string name) { ... }
        //
        //   ❌ WON'T COMPILE — you can't await a constructor call
        //   var session = await new PricingCacheSession("prices");
        //
        //   ❌ COMPILES BUT CRASHES — async void in a constructor
        //   public PricingCacheSession(string name) { InitAsync(); }
        //   async void InitAsync() { await Task.Delay(100); }
        //   // Exception hits UnhandledException — crashes the process!
        //
        // The async void approach is especially dangerous because the
        // constructor returns BEFORE async init finishes. The caller
        // gets a half-initialized object. And if InitAsync throws, the
        // exception crashes the process (same lesson as Exercise 6).
        //
        // The solution: async factory method. The static CreateAsync()
        // method is async, returns Task<PricingCacheSession>, and the
        // caller awaits it. By the time the await completes, the object
        // is 100% ready.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 1: Why constructors cannot be async ---");
        _output.WriteLine($"Constructors must return `this` synchronously. `new` is not awaitable.");
        _output.WriteLine($"Solution: static async factory method (CreateAsync).");
        _output.WriteLine($"");

        // ---------------------------------------------------------------
        // PART 2: Async factory pattern in action.
        //
        // We call the async factory method and await it. By the time
        // the await completes, the session is fully initialized —
        // connection open, authenticated, ready to use.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 2: Async factory pattern ---");

        // This is the ONLY way to create a PricingCacheSession.
        // The private constructor prevents `new PricingCacheSession(...)`.
        PricingCacheSession session = await PricingCacheSession.CreateAsync("live-prices");

        // The session is fully initialized. We can use it immediately.
        Assert.True(session.IsOpen, "Session should be open after CreateAsync");
        Assert.Equal("live-prices", session.CacheName);

        var price = await session.GetPriceAsync("SKU-100");
        Assert.Equal(42.50m, price);
        _output.WriteLine($"Created session '{session.CacheName}' — connected and ready");
        _output.WriteLine($"Fetched price: {price:C}");
        _output.WriteLine($"");

        // Clean up manually for this part (we'll use await using below).
        await session.DisposeAsync();
        Assert.False(session.IsOpen, "Session should be closed after DisposeAsync");

        // ---------------------------------------------------------------
        // PART 3: "await using" for automatic disposal.
        //
        // "await using" is the async version of "using". It calls
        // DisposeAsync() automatically when the block ends — whether
        // the block completes normally, hits a break, or throws an
        // exception. This is the same safety guarantee as synchronous
        // "using", but async.
        //
        // The declaration form: "await using var x = ...;"
        // scopes disposal to the end of the enclosing method.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 3: await using (declaration form) ---");

        await using var checkoutSession = await PricingCacheSession.CreateAsync("checkout-cache");

        // The session is open and usable after creation.
        Assert.True(checkoutSession.IsOpen);
        var checkoutPrice = await checkoutSession.GetPriceAsync("SKU-200");
        Assert.Equal(42.50m, checkoutPrice);
        _output.WriteLine($"Session is open, price = {checkoutPrice:C}");
        _output.WriteLine($"DisposeAsync() will run when this method exits");
        _output.WriteLine($"");

        // ---------------------------------------------------------------
        // PART 4: "await using" with try/finally for scoped disposal.
        //
        // If you need disposal to happen at a specific point (not at the
        // end of the method), use a try/finally block with an explicit
        // await DisposeAsync() call in the finally. This is equivalent
        // to what "await using" does under the hood.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 4: Explicit try/finally (what await using does under the hood) ---");

        PricingCacheSession? scopedSession = null;
        try
        {
            scopedSession = await PricingCacheSession.CreateAsync("scoped-cache");
            Assert.True(scopedSession.IsOpen);

            var scopedPrice = await scopedSession.GetPriceAsync("SKU-400");
            Assert.Equal(42.50m, scopedPrice);
            _output.WriteLine($"Inside try: session is open, price = {scopedPrice:C}");
        }
        finally
        {
            // This is what "await using" generates behind the scenes.
            // DisposeAsync() is called in the finally block, guaranteeing
            // cleanup even if an exception was thrown in the try block.
            if (scopedSession != null)
            {
                await scopedSession.DisposeAsync();
                _output.WriteLine($"Finally: DisposeAsync() was called — session closed");
            }
        }

        Assert.False(scopedSession!.IsOpen, "Session should be closed after finally");
        _output.WriteLine($"");

        // ---------------------------------------------------------------
        // PART 5: Exception + cleanup.
        //
        // If an exception is thrown inside an "await using" block (or
        // try/finally), DisposeAsync() STILL runs before the exception
        // propagates. This is critical for resource safety.
        // ---------------------------------------------------------------
        _output.WriteLine($"--- Part 5: Exception + cleanup ---");

        PricingCacheSession? riskySession = null;
        try
        {
            riskySession = await PricingCacheSession.CreateAsync("risky-cache");
            Assert.True(riskySession.IsOpen);

            // Simulate something going wrong.
            throw new InvalidOperationException("Something went wrong during pricing");
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains("Something went wrong", ex.Message);
            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }
        finally
        {
            // DisposeAsync() runs EVEN THOUGH an exception was thrown.
            // The session's connection is properly closed.
            if (riskySession != null)
            {
                await riskySession.DisposeAsync();
                _output.WriteLine($"Finally: DisposeAsync() ran after exception — connection closed safely");
            }
        }

        _output.WriteLine($"");

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
        //    calls DisposeAsync(). You can write the same pattern manually
        //    with try/finally + await DisposeAsync() for more control.
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
