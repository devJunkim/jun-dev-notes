---
title: "C# IEnumerable vs ICollection vs IList: What's the Difference?"
excerpt: "Learn how IEnumerable, ICollection, and IList relate in C#, what capabilities each interface exposes, and how to choose better API parameter and return types."
category: "C#"

seo:
  focusKeyword: "C# IEnumerable vs ICollection vs IList"
  description: "Compare IEnumerable, ICollection, and IList in C#, including enumeration, modification, indexing, read-only alternatives, and practical API design."
  socialTitle: "C# IEnumerable vs ICollection vs IList"
  socialDescription: "Understand the generic collection interfaces in C# and choose the least-capable abstraction that fits your API."
---

# C# IEnumerable vs ICollection vs IList: What's the Difference?

A method that accepts `List<T>` can enumerate, count, add, remove, reorder, and index its argument. But what if it only needs to read each item once? Requiring a concrete list exposes capabilities the method does not use and prevents callers from supplying other sequence types.

`IEnumerable<T>`, `ICollection<T>`, and `IList<T>` form a capability hierarchy. `IEnumerable<T>` supports iteration. `ICollection<T>` adds size and collection modification. `IList<T>` adds positional access and insertion. Choosing among them is an API-design decision about the guarantees a caller provides and the operations a method is allowed to perform.

> **Quick answer:** Accept `IEnumerable<T>` for iteration, `ICollection<T>` when you need collection operations such as `Count` or `Add`, and `IList<T>` when indexed position is part of the contract. Prefer read-only interfaces when consumers should observe rather than mutate.

## `IEnumerable<T>`, `ICollection<T>`, and `IList<T>` Compared

| Capability | `IEnumerable<T>` | `ICollection<T>` | `IList<T>` |
| --- | --- | --- | --- |
| Enumerate with `foreach` | Yes | Yes | Yes |
| `Count` property | No | Yes | Yes |
| Add and remove | No | Yes | Yes |
| Indexed access | No | No | Yes |
| Insert/remove by index | No | No | Yes |
| Implies repeatable enumeration | No | Usually as a collection contract | Yes |
| Typical intent | Sequence | Mutable collection | Mutable ordered/indexed list |

The inheritance relationship is:

```text
IEnumerable<T>
    ↑
ICollection<T>
    ↑
IList<T>
```

`IList<T>` inherits `ICollection<T>`, which inherits `IEnumerable<T>`. A value implementing `IList<T>` can therefore be used wherever either less-capable interface is required. Microsoft's [`IList<T>` reference](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1) lists the inherited and list-specific members.

## What `IEnumerable<T>` Represents

`IEnumerable<T>` represents a sequence that can produce an enumerator. Its essential capability is iteration.

```csharp
public static decimal CalculateTotal(IEnumerable<decimal> amounts)
{
    decimal total = 0;

    foreach (var amount in amounts)
    {
        total += amount;
    }

    return total;
}
```

The method works with arrays, lists, sets, iterator methods, and LINQ pipelines because it asks only for enumeration.

```csharp
decimal[] array = [10m, 20m];
List<decimal> list = [5m, 15m];

Console.WriteLine(CalculateTotal(array)); // 30
Console.WriteLine(CalculateTotal(list));  // 20
```

### Enumeration is not necessarily storage

An `IEnumerable<T>` might wrap an in-memory collection, but the interface does not promise that. It can generate values lazily, read a file, or execute another operation as it is enumerated.

```csharp
static IEnumerable<int> CountUpTo(int maximum)
{
    for (var number = 1; number <= maximum; number++)
    {
        Console.WriteLine($"Producing {number}");
        yield return number;
    }
}

var numbers = CountUpTo(3);
Console.WriteLine("Sequence created");

foreach (var number in numbers)
{
    Console.WriteLine($"Received {number}");
}
```

Calling `CountUpTo` creates the sequence, but its body runs during enumeration. Enumerating it twice runs the iterator twice.

This matters when an API accepts `IEnumerable<T>`. Avoid assuming enumeration is cheap, repeatable, finite, or free of side effects. If a method needs several passes, materialize once when appropriate:

```csharp
public static (int Count, decimal Total) Summarize(
    IEnumerable<decimal> source)
{
    ArgumentNullException.ThrowIfNull(source);

    var values = source as IReadOnlyCollection<decimal> ?? source.ToArray();
    return (values.Count, values.Sum());
}
```

Materialization trades time and memory for stable repeated access. Do it because the algorithm requires it, not automatically at every boundary.

## What `ICollection<T>` Represents

`ICollection<T>` represents a collection with a known `Count` and operations for adding, removing, clearing, membership checking, and copying.

Important members include:

- `Count`
- `IsReadOnly`
- `Add(T)` and `Remove(T)`
- `Clear()` and `Contains(T)`
- `CopyTo(T[], int)`

```csharp
public static void AddIfMissing(
    ICollection<string> tags,
    string tag)
{
    if (!tags.Contains(tag))
    {
        tags.Add(tag);
    }
}

ICollection<string> tags = new HashSet<string>(
    StringComparer.OrdinalIgnoreCase);

AddIfMissing(tags, "dotnet");
AddIfMissing(tags, "DOTNET");

Console.WriteLine(tags.Count); // 1
```

This method does not care about numeric positions, so `IList<T>` would demand more than necessary. Accepting `ICollection<T>` also allows a `HashSet<T>` whose membership semantics may be useful.

### `IsReadOnly` does not mean the interface is read-only

`ICollection<T>` includes mutation members even when `IsReadOnly` returns `true`. Such an implementation can throw `NotSupportedException` from `Add`, `Remove`, or `Clear`. The property describes the particular collection instance; it does not remove mutation from the static API.

If callers should not mutate through your contract, use `IReadOnlyCollection<T>` rather than relying on `IsReadOnly`.

## What `IList<T>` Represents

`IList<T>` is an ordered collection whose elements can be accessed by zero-based index. It inherits every `ICollection<T>` member and adds:

- The `this[int index]` getter and setter
- `IndexOf(T)`
- `Insert(int, T)`
- `RemoveAt(int)`

```csharp
public static void MoveToFront<T>(IList<T> items, int index)
{
    ArgumentNullException.ThrowIfNull(items);

    if ((uint)index >= (uint)items.Count)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    var item = items[index];
    items.RemoveAt(index);
    items.Insert(0, item);
}

IList<string> queue = new List<string> { "A", "B", "C" };
MoveToFront(queue, 2);

Console.WriteLine(string.Join(", ", queue)); // C, A, B
```

The index is meaningful to the operation, so `IList<T>` is honest. Replacing it with `ICollection<T>` would force awkward enumeration and would not express positional mutation.

Do not infer performance from the interface alone. Arrays and `List<T>` provide constant-time indexing, but another `IList<T>` implementation can have different characteristics. The contract promises indexed access, not a complexity bound.

## `List<T>` Implements All Three

`List<T>` implements `IList<T>`, `ICollection<T>`, and `IEnumerable<T>`, as well as their read-only counterparts. The same object can be viewed through different contracts.

```csharp
var products = new List<string> { "Keyboard", "Mouse" };

IEnumerable<string> sequence = products;
ICollection<string> collection = products;
IList<string> indexed = products;

foreach (var product in sequence)
{
    Console.WriteLine(product);
}

collection.Add("Monitor");
indexed[0] = "Mechanical Keyboard";
```

The runtime object remains one `List<string>`. The variable's declared interface controls which operations are available through that reference.

An interface is not a security boundary. Code can sometimes cast back to the concrete type. Use encapsulation and appropriate immutable/read-only representations rather than assuming an interface makes mutation impossible.

## Fixed-Size Collections Are an Important Edge Case

Arrays implement `IList<T>` and `ICollection<T>`, but their length cannot change. Indexed reads and replacements work, while size-changing operations throw `NotSupportedException`.

```csharp
IList<string> names = new[] { "Ada", "Grace" };

names[0] = "Margaret"; // Valid: replaces an existing element.

Console.WriteLine(names.Count);      // 2
Console.WriteLine(names.IsReadOnly); // True

// names.Add("Barbara"); // Throws NotSupportedException at runtime.
```

This can surprise an API that accepts `ICollection<T>` and assumes `Add` will succeed. The interface exposes possible operations, while `IsReadOnly` reports whether that instance supports mutation through the collection contract.

If a method must grow the supplied collection, document that requirement and consider whether returning results is clearer than mutating caller-owned storage. If it only needs a count, `IReadOnlyCollection<T>` avoids suggesting mutation in the first place.

## Accept the Least-Capable Useful Abstraction

An input parameter should usually require only the capabilities its implementation needs.

```csharp
public static void WriteNames(
    IEnumerable<Customer> customers,
    TextWriter writer)
{
    foreach (var customer in customers)
    {
        writer.WriteLine(customer.Name);
    }
}

public sealed record Customer(string Name);
```

Requiring `List<Customer>` would reject arrays, immutable collections, and generated sequences for no functional reason. Requiring `IList<Customer>` would misleadingly suggest the method uses positions or mutation.

This guideline is not “always accept `IEnumerable<T>`.” If the algorithm requires a stable count and repeated enumeration, `IReadOnlyCollection<T>` communicates that better. If it requires indexed access, accept `IReadOnlyList<T>` or `IList<T>` depending on whether mutation is intended.

| Method need | Suitable parameter |
| --- | --- |
| Iterate once | `IEnumerable<T>` |
| Read stable count | `IReadOnlyCollection<T>` |
| Read by index | `IReadOnlyList<T>` |
| Add or remove | `ICollection<T>` |
| Mutate by position | `IList<T>` |

## Choosing Return Types

Return types communicate ownership and behavior as well as capabilities.

If a method lazily produces results, `IEnumerable<T>` is natural:

```csharp
public static IEnumerable<string> FindWarnings(
    IEnumerable<string> lines)
{
    foreach (var line in lines)
    {
        if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase))
        {
            yield return line;
        }
    }
}
```

If a method completes work and returns a materialized snapshot, a read-only interface can state that callers receive an already formed collection:

```csharp
public static IReadOnlyList<string> NormalizeTags(
    IEnumerable<string> tags) =>
    tags
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order()
        .ToArray();
```

Returning `IReadOnlyList<T>` prevents mutation through that reference but does not guarantee deep immutability or an immutable backing object. If the result must never change, return an immutable collection or a defensive snapshot and keep mutable storage private.

Avoid returning `IList<T>` merely to hide `List<T>`. It still exposes broad mutation. Return a mutable collection only when the caller is intentionally expected to own and modify it.

## Read-Only Alternatives

`IReadOnlyCollection<T>` provides `Count` and enumeration. `IReadOnlyList<T>` adds a read-only indexer and inherits `IReadOnlyCollection<T>`.

```csharp
public sealed class Schedule
{
    private readonly List<DateOnly> _dates = [];

    public IReadOnlyList<DateOnly> Dates => _dates;

    public void Add(DateOnly date)
    {
        if (!_dates.Contains(date))
        {
            _dates.Add(date);
            _dates.Sort();
        }
    }
}
```

The property exposes no mutation members, but a determined caller should not be able to cast this particular value back to `List<T>` if encapsulation is important. Returning `_dates.AsReadOnly()` or an immutable snapshot creates a stronger boundary, with different allocation and update trade-offs.

## Common Misconceptions

### “`IEnumerable<T>` means the data is in memory”

It means the value can be enumerated. The underlying sequence may be a list, a lazy iterator, a database-backed provider exposed through another abstraction, or a stream of generated values.

### “`IEnumerable<T>` has a `Count` property”

It does not. LINQ's `Count()` is an extension method and may enumerate the sequence. Some modern implementations can provide a count efficiently, but an API should not assume that every enumerable can.

### “`ICollection<T>` is always mutable”

The interface exposes mutation members, but an implementation can report `IsReadOnly` and reject them. Use read-only interfaces when mutation should not be part of the consumer contract.

### “`IList<T>` guarantees a `List<T>`”

It guarantees list capabilities, not a concrete implementation or its performance characteristics.

### “A read-only interface makes the objects immutable”

It restricts collection operations through that reference. Elements can still be mutable, and the underlying collection might change elsewhere.

## Interview-Oriented Questions

### How are the three interfaces related?

`IList<T>` inherits `ICollection<T>`, and `ICollection<T>` inherits `IEnumerable<T>`. Each level adds capabilities.

### When should a method accept `IEnumerable<T>`?

When it only needs to enumerate. Be conscious that enumeration may be deferred, expensive, stateful, or non-repeatable.

### Why prefer `IReadOnlyList<T>` over `IList<T>` for some APIs?

It exposes count and indexed reads without promising that the consumer may add, remove, or replace items.

### Does returning `IEnumerable<T>` guarantee deferred execution?

No. A `List<T>` also implements `IEnumerable<T>`. The return type alone does not say whether the sequence is lazy or already materialized; document behavior when it matters.

### Why accept the least-capable abstraction?

It reduces unnecessary coupling, accepts more valid implementations, and communicates what the method actually requires. Do not choose an abstraction weaker than the algorithm's real needs.

## Summary

The generic collection interfaces form a useful capability ladder:

- `IEnumerable<T>` supports enumeration.
- `ICollection<T>` adds count and collection mutation.
- `IList<T>` adds positional access and mutation.

`List<T>` implements all three, but that does not mean every API should request a list. Accept the narrowest contract that expresses the required operations, choose read-only alternatives when mutation is not intended, and make return types honest about laziness, materialization, ownership, and change.
