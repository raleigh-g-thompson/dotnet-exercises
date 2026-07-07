namespace exercises.Lib;

public static class GenericsHelper
{
    public static void PrintAll<T>(List<T> items)
    {
        foreach (var item in items)
        {
            Console.WriteLine(item);
        }
    }

    // Non-generic version: accepts anything as object.
    // ○ Compiles with ANY two arguments — even unrelated types.
    // ○ Boxes value types (e.g. decimal gets wrapped in an object).
    // × No compile-time safety — comparing an OrderItem to a string is allowed.
    public static bool AreEqual(object a, object b)
    {
        return a.Equals(b);
    }

    // Generic version: both arguments must be the same type T,
    // and T must implement IEquatable<T>.
    // ○ No boxing for value types — T stays as its real type.
    // ○ Compiler catches mismatched types at build time.
    // ○ Only types that "know how to compare themselves" are allowed.
    public static bool AreEqual<T>(T a, T b) where T : IEquatable<T>
    {
        return a.Equals(b);
    }

    // Returns whichever of a or b is "larger" using CompareTo.
    // The constraint 'where T : IComparable<T>' is required because
    // the > operator does NOT work on generic type parameters — C#
    // does not let you constrain by operators. Instead, types that
    // support ordering implement IComparable<T>, which provides a
    // CompareTo(T other) method that returns:
    //   < 0  if this  < other
    //   = 0  if this == other
    //   > 0  if this  > other
    public static T GetLarger<T>(T a, T b) where T : IComparable<T>
    {
        return a.CompareTo(b) > 0 ? a : b;
    }

    // Safely get an element from a list at the given index.
    // If the index is inside the list, return the element.
    // If the index is outside, return default(T) — no exception.
    //
    // default(T) is a C# keyword that gives you the "zero" value for
    // whatever type T is:
    //   - reference types (string, OrderItem, Customer) → null
    //   - numeric types (int, decimal, double)          → 0
    //   - bool                                          → false
    //   - DateTime                                      → DateTime.MinValue
    public static T TryGet<T>(List<T> items, int index)
    {
        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return default!;
    }

    // Prints every property name and value on an instance of T.
    //
    // Generics alone CANNOT do this — the compiler has no idea what
    // properties T has at compile time. At runtime, T could be
    // OrderItem, Customer, CartItem, Address, or anything else.
    // The compiler cannot check "does T have a Street property?"
    // because generic constraints don't include property names.
    //
    // The feature that makes this possible is REFLECTION
    // (System.Reflection, System.Type). Reflection lets you inspect
    // the structure of a type at runtime: get its properties, methods,
    // fields, attributes, and more. It's like giving X-ray vision to
    // your code — you can see inside any object without knowing its
    // shape ahead of time.
    //
    // The trade-off: reflection is slower than normal code because
    // it works by looking up metadata by string names. Use it when
    // you need to handle unknown types (serializers, ORMs, debug
    // tools), not for performance-sensitive hot paths.
    public static void PrintProperties<T>(T obj)
    {
        var type = typeof(T);
        var properties = type.GetProperties();

        Console.WriteLine($"{type.Name}:");

        foreach (var prop in properties)
        {
            var value = prop.GetValue(obj);
            Console.WriteLine($"  {prop.Name} = {value ?? "(null)"}");
        }
    }
}
