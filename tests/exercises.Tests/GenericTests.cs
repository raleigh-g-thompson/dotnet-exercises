using exercises.Lib;

namespace exercises.Tests;

public class GenericTests
{
    // This test teaches how a generic method works, and what happens when you
    // print an object that doesn't override ToString().
    //
    // PrintAll<T> is a generic method — it works with ANY type T. You give it
    // a List<T> and it walks through each item and calls Console.WriteLine(item).
    // The magic is that one method body works for OrderItem, Customer, CartItem,
    // or any other type you can think of.
    //
    // When you call Console.WriteLine on an object, C# secretly calls
    // that object's ToString() method. If the class does NOT override ToString(),
    // the default behavior from System.Object kicks in and just prints the
    // fully-qualified type name (e.g. "exercises.Lib.OrderItem").
    //
    // To control what gets printed, you override ToString() in your class
    // to return whatever string you want — like "Order #5: Widget" or
    // "Customer: Alice (ID 42)".
    [Fact]
    public void PrintAll_GenericMethod_PrintsEachItemToConsole()
    {
        // Capture all console output so we can inspect what was printed.
        var consoleOut = new StringWriter();
        Console.SetOut(consoleOut);

        // Three different lists, three different types.
        var orders = new List<OrderItem>
        {
            new(1, "Widget", 2, 10.00m),
            new(2, "Gadget", 1, 25.00m)
        };

        var customers = new List<Customer>
        {
            new(1, "Alice"),
            new(2, "Bob")
        };

        var cartItems = new List<CartItem>
        {
            new(1, 2),
            new(2, 1)
        };

        // Call the SAME generic method with three different types.
        // The compiler figures out T from the argument:
        //   PrintAll<OrderItem>(orders)  — T = OrderItem
        //   PrintAll<Customer>(customers) — T = Customer
        //   PrintAll<CartItem>(cartItems) — T = CartItem
        GenericsHelper.PrintAll(orders);
        GenericsHelper.PrintAll(customers);
        GenericsHelper.PrintAll(cartItems);

        var output = consoleOut.ToString();
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        // 2 OrderItems + 2 Customers + 2 CartItems = 6 lines printed.
        Assert.Equal(6, lines.Count);

        // Without a custom ToString(), these classes print their
        // fully-qualified namespace + type name.
        Assert.Equal("exercises.Lib.OrderItem", lines[0]);
        Assert.Equal("exercises.Lib.OrderItem", lines[1]);
        Assert.Equal("exercises.Lib.Customer", lines[2]);
        Assert.Equal("exercises.Lib.Customer", lines[3]);
        Assert.Equal("exercises.Lib.CartItem", lines[4]);
        Assert.Equal("exercises.Lib.CartItem", lines[5]);

        // --- BONUS: controlling what gets printed ---
        // To make objects print nicely, you override the ToString() method.
        // For example, if OrderItem had:
        //   public override string ToString() => $"{Name} (qty {Quantity})";
        // Then Console.WriteLine would print "Widget (qty 2)" instead of
        // "exercises.Lib.OrderItem".
        //
        // Try uncommenting the ToString() override in OrderItem.cs and
        // re-run — you'll see the output change!
    }

    // This test demonstrates why AreEqual(object, object) is dangerous.
    // Imagine a treasure chest (object) that can hold ANYTHING — a coin,
    // a toy, a shoe. When you toss two items into the chest (pass them as
    // 'object'), C# forgets what they really are. You can toss in a coin
    // and a shoe and ask "are these the same?" — C# will try to answer,
    // even though comparing a coin to a shoe makes no sense!
    //
    // Problems with the object approach:
    //   1. Value types (like decimal, int) get boxed — wrapped in a box
    //      that wastes memory and CPU.
    //   2. You can compare completely unrelated types (OrderItem vs string)
    //      and the compiler won't stop you — the bug hides until runtime.
    //   3. It relies on Object.Equals(), which for reference types defaults
    //      to reference equality (are they the SAME object in memory?),
    //      not value equality (do they have the same data?).
    [Fact]
    public void AreEqual_ObjectVersion_ComparesWithNoTypeSafety()
    {
        // Two OrderItems with identical data.
        var item1 = new OrderItem(1, "Widget", 2, 10.00m);
        var item2 = new OrderItem(1, "Widget", 2, 10.00m);

        // Two decimal prices.
        decimal priceA = 10.00m;
        decimal priceB = 10.00m;

        // The object version compiles and runs even for unrelated types.
        // This is the PROBLEM — C# should have caught this mistake!
        var sameItemAndString = GenericsHelper.AreEqual(item1, "I am a string, not an OrderItem");

        // ✅ Same type, same values → true
        Assert.True(GenericsHelper.AreEqual(item1, item2));

        // ✅ Same type (decimal), same values → true
        Assert.True(GenericsHelper.AreEqual(priceA, priceB));

        // ⚠️ OrderItem vs string — compiles but is ALWAYS false.
        // The compiler lets this through because both are object.
        Assert.False(sameItemAndString);
    }

    // This test teaches why > doesn't work on generic types and how
    // IComparable<T> solves the problem.
    //
    // Imagine you have two piles of coins — one with $10 and one with $25.
    // You want to write ONE method that can find the larger of ANY two
    // things: prices, toy names, dates, etc. You try 'if (a > b)' but C#
    // says "Nope! I don't know how to compare T values."
    //
    // Why doesn't > work on T? Because > is an operator — a static method
    // baked into specific types (int, decimal, string have their own >).
    // C# generics do NOT let you add "where T : has > operator" as a
    // constraint. Operators can't be constrained.
    //
    // The fix: use the IComparable<T> interface. It's like a "comparison
    // contract" — any type that signs it promises to have a CompareTo
    // method. decimal, string, DateTime, int all implement it out of the
    // box. CompareTo returns:
    //   negative if a < b,
    //   zero     if a == b,
    //   positive if a > b.
    [Fact]
    public void GetLarger_ComparableConstraint_ReturnsLargerValue()
    {
        // --- decimal: compare prices ---
        decimal cheap = 10.00m;
        decimal expensive = 25.00m;

        var largerPrice = GenericsHelper.GetLarger(cheap, expensive);
        Assert.Equal(25.00m, largerPrice);

        // --- string: compare lexicographically ---
        // "apple" < "zebra" because 'a' comes before 'z' in the alphabet.
        var largerString = GenericsHelper.GetLarger("apple", "zebra");
        Assert.Equal("zebra", largerString);

        // --- DateTime: compare dates ---
        var earlier = new DateTime(2024, 1, 1);
        var later = new DateTime(2025, 6, 15);

        var largerDate = GenericsHelper.GetLarger(earlier, later);
        Assert.Equal(later, largerDate);

        // --- What if you try a type WITHOUT IComparable<T>? ---
        // OrderItem does NOT implement IComparable<OrderItem> (yet).
        // The line below would NOT COMPILE:
        //
        //   GenericsHelper.GetLarger(item1, item2);
        //
        // Error: "The type OrderItem cannot be used as type parameter T
        // in GetLarger<T>. There is no implicit reference conversion from
        // OrderItem to IComparable<OrderItem>."
        //
        // The constraint protected us from calling CompareTo on a type
        // that doesn't support it, which would crash at runtime.
    }

    // This test teaches how default(T) provides a safe "plan B" value
    // for any generic type T.
    //
    // TryGet<T> is like a vending machine that gives you a snack at a
    // certain slot number. If the slot exists, you get the snack. If
    // the slot is empty or there is no such slot, you get a "nothing"
    // instead of the machine yelling at you (throwing an exception).
    //
    // In C#, default(T) is the "nothing" value for any type:
    //   - Reference types (string, OrderItem, etc.) → null ("no object")
    //   - Numeric types (int, decimal, etc.)       → 0 ("no amount")
    //   - bool                                     → false
    //   - DateTime                                 → 0001-01-01
    [Fact]
    public void TryGet_DefaultOfT_ReturnsFallbackWhenOutOfBounds()
    {
        // --- List<int>: default is 0 ---
        var numbers = new List<int> { 10, 20, 30 };

        Assert.Equal(10, GenericsHelper.TryGet(numbers, 0));   // in bounds
        Assert.Equal(20, GenericsHelper.TryGet(numbers, 1));   // in bounds

        // Out of bounds → default(int) = 0  (no exception thrown)
        Assert.Equal(0, GenericsHelper.TryGet(numbers, 99));
        Assert.Equal(0, GenericsHelper.TryGet(numbers, -1));

        // --- List<string>: default is null ---
        var names = new List<string> { "Alice", "Bob" };

        Assert.Equal("Alice", GenericsHelper.TryGet(names, 0));
        Assert.Equal("Bob", GenericsHelper.TryGet(names, 1));

        // Out of bounds → default(string) = null
        Assert.Null(GenericsHelper.TryGet(names, 99));

        // --- List<OrderItem>: default is null (reference type) ---
        var items = new List<OrderItem>
        {
            new(1, "Widget", 2, 10.00m)
        };

        Assert.NotNull(GenericsHelper.TryGet(items, 0));
        Assert.Equal("Widget", GenericsHelper.TryGet(items, 0)!.Name);

        // Out of bounds → default(OrderItem) = null
        Assert.Null(GenericsHelper.TryGet(items, 99));
    }

    // This test teaches how the generic version with a constraint fixes
    // the problems from the object version.
    //
    // The constraint 'where T : IEquatable<T>' says: "T must be a type
    // that knows how to compare itself to another T." Types like decimal,
    // int, and string already know how (they implement IEquatable<T>
    // built-in). Our OrderItem also knows now (we added it above).
    //
    // What the generic version prevents:
    //   1. ← COMPILE ERROR: AreEqual<OrderItem>(orderItem, "hello")
    //      The compiler infers T as OrderItem from the first argument,
    //      then sees "hello" is a string (not an OrderItem) and says
    //      "No, these types don't match!" — caught at build time.
    //   2. No boxing — decimal stays decimal, no wrapping needed.
    //   3. Only types with IEquatable<T> can call this method — you
    //      cannot accidentally pass a type that doesn't support equality.
    [Fact]
    public void AreEqual_GenericVersion_RequiresSameTypeAndIEquatable()
    {
        var item1 = new OrderItem(1, "Widget", 2, 10.00m);
        var item2 = new OrderItem(1, "Widget", 2, 10.00m);

        decimal priceA = 10.00m;
        decimal priceB = 10.00m;

        // ✅ Same type (OrderItem), same values → true.
        // OrderItem implements IEquatable<OrderItem>, so it satisfies
        // the constraint.
        Assert.True(GenericsHelper.AreEqual(item1, item2));

        // ✅ Same type (decimal), same values → true.
        // decimal implements IEquatable<decimal>, no boxing occurs.
        Assert.True(GenericsHelper.AreEqual(priceA, priceB));

        // The line below would NOT COMPILE — try uncommenting it:
        //
        //   GenericsHelper.AreEqual(item1, "I am a string");
        //
        // Error: "cannot be inferred from usage" or "type string cannot
        // be used as type parameter T in AreEqual<T>". The compiler
        // sees that the first arg is OrderItem (T = OrderItem) and the
        // second arg is string — NOT an OrderItem — so it refuses to
        // build. Bug caught before the program ever runs!
    }

    // This test teaches covariance — why you CAN treat a List<OrderItem>
    // as an IEnumerable<object> but NOT as a List<object>.
    //
    // Imagine a toy box labeled "OrderItems" (List<OrderItem>). It should
    // only ever hold OrderItem toys. Now someone says "just treat it as
    // a box of 'anything' (List<object>)." That sounds innocent, but if
    // your box thinks it holds "anything," someone might toss a Banana
    // (a string) inside. When you later reach in expecting an OrderItem,
    // you'd get a Banana and your code would break!
    //
    // That is the difference between 'out' (covariant) and 'in' (contravariant):
    //
    //   IEnumerable<out T>   → you can ONLY READ items — safe to widen T
    //   IReadOnlyList<out T> → you can ONLY READ items — safe to widen T
    //   IList<T>             → you can READ AND WRITE — NOT safe to widen
    //   List<T>              → you can READ AND WRITE — NOT safe to widen
    //
    // Interfaces with 'out T' are covariant: a List<OrderItem> can become
    // an IEnumerable<object> because the enumerator only yields items.
    // Nobody can insert a Banana through IEnumerable — the interface
    // has no Add() method.
    [Fact]
    public void Covariance_ReadOnlyInterfacesPermitWidening()
    {
        // Setup: a list of OrderItems.
        var items = new List<OrderItem>
        {
            new(1, "Widget", 2, 10.00m),
            new(2, "Gadget", 1, 25.00m)
        };

        // --- These compile because the interfaces are covariant (out T) ---

        // IEnumerable<out T>: the T only appears in output positions (return
        // values). You can foreach over it but never Add to it. Safe!
        IEnumerable<object> asEnumerable = items;
        Assert.Equal(2, asEnumerable.Count());

        // IReadOnlyList<out T>: same idea — read-only, no way to insert.
        // Also covariant, so this assignment compiles.
        IReadOnlyList<object> asReadOnly = items;
        Assert.Equal(2, asReadOnly.Count);

        // --- These would NOT compile — try uncommenting them: ---
        //
        //   List<object> a = items;
        //   // CS0029: Cannot implicitly convert type 'List<OrderItem>' to
        //   // 'List<object>'. List<T> is invariant — it has Add(T),
        //   // and you could add a string to what claims to be a list of
        //   // OrderItems.
        //
        //   IList<object> b = items;
        //   // CS0266: Cannot implicitly convert type 'List<OrderItem>' to
        //   // 'IList<object>'. IList<T> is also invariant (it supports
        //   // writing via Insert, RemoveAt, etc.).
        //
        // WHY this matters (the Banana problem):
        //
        //   List<OrderItem> orderList = GetOrderItems();
        //   List<object> objectList = orderList;  // ← imagine this compiled
        //   objectList.Add("I am a Banana!");      // ← Banana sneaks in!
        //   OrderItem item = orderList[2];          // ← CRASH! Banana is
        //                                            //   not an OrderItem
        //
        // The compiler prevents the assignment because List<T> and IList<T>
        // allow writing, and writing could violate type safety. Covariance
        // is only safe when the type parameter is read-only (out).
        //
        // Built-in covariant interfaces:
        //   IEnumerable<out T>   — foreach, LINQ
        //   IReadOnlyList<out T> — read-only list access
        //   IReadOnlyCollection<out T> — Count, Contains, etc.
        //   IGrouping<out TKey, out TElement> — grouping results
        //   IEnumerable<out T>   — the most common one you will see
    }

    // This test teaches how generics and reflection work together to
    // inspect objects at runtime.
    //
    // Generic methods let us write ONE method that works with ANY type T.
    // But generics only know about types at compile time — the compiler
    // checks that T has certain methods via constraints (IComparable<T>,
    // IEquatable<T>, etc.). The compiler does NOT know "T has a Street
    // property" because there is no way to say 'where T : has Street'.
    //
    // Reflection (System.Reflection) solves this. It lets you ask "what
    // properties does this type have?" at RUNTIME. The code uses
    // typeof(T).GetProperties() to discover the shape of the object
    // and then loops through each property reading its value.
    //
    // This is how JSON serializers (System.Text.Json), ORMs (EF Core),
    // debuggers, and UI builders work — they use reflection to handle
    // types they never knew about when the code was compiled.
    [Fact]
    public void PrintProperties_UsesReflectionToInspectAnyType()
    {
        var consoleOut = new StringWriter();
        Console.SetOut(consoleOut);

        var orderItem = new OrderItem(1, "Widget", 2, 10.00m);
        var customer = new Customer(42, "Alice");
        var cartItem = new CartItem(5, 3);
        var address = new Address("123 Main St", "Portland", "OR", "97201");

        GenericsHelper.PrintProperties(orderItem);
        GenericsHelper.PrintProperties(customer);
        GenericsHelper.PrintProperties(cartItem);
        GenericsHelper.PrintProperties(address);

        var output = consoleOut.ToString();

        // --- OrderItem ---
        Assert.Contains("OrderItem:", output);
        Assert.Contains("ItemId = 1", output);
        Assert.Contains("Name = Widget", output);
        Assert.Contains("Quantity = 2", output);
        Assert.Contains("UnitPrice = 10.00", output);

        // --- Customer ---
        Assert.Contains("Customer:", output);
        Assert.Contains("Id = 42", output);
        Assert.Contains("Name = Alice", output);

        // --- CartItem ---
        Assert.Contains("CartItem:", output);
        Assert.Contains("ItemId = 5", output);
        Assert.Contains("Quantity = 3", output);

        // --- Address ---
        Assert.Contains("Address:", output);
        Assert.Contains("Street = 123 Main St", output);
        Assert.Contains("City = Portland", output);
        Assert.Contains("State = OR", output);
        Assert.Contains("ZipCode = 97201", output);

        // The key insight: this one method works for ANY type without
        // knowing its properties at compile time. That is the power of
        // generics (write-once-for-any-type) + reflection (inspect
        // structure at runtime).
    }
}
