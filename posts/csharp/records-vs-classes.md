---
title: "Records vs Classes in C#: When Should You Use Each?"
excerpt: "Compare records and classes in C# through equality, immutability, DTOs, domain entities, and practical examples that expose common design mistakes."
category: "C#"

seo:
  focusKeyword: "records vs classes in C#"
  description: "Choose between C# records and classes with practical examples of value equality, immutability, with expressions, DTOs, and domain models."
  socialTitle: "Records vs Classes in C#: When Should You Use Each?"
  socialDescription: "Compare records and classes in C# through equality, immutability, DTOs, domain entities, and practical examples that expose common design mistakes."
---

# Records vs Classes in C#: When Should You Use Each?

An API response and an order being edited may expose similar properties, yet they have different meanings. Two response snapshots containing the same data can reasonably be interchangeable. Two orders with identical totals still represent different purchases. Choosing a record or class starts with that distinction, not with the number of lines in the declaration.

> **Quick answer:** Use records when a type represents a value and generated value equality matches its meaning. Use ordinary classes when identity, controlled state changes, or object lifecycle matters. Records support concise immutable models, but they do not automatically make an object graph immutable.

## Records and Classes at a Glance

| Concern | Ordinary class | Record class | Record struct |
| --- | --- | --- | --- |
| Type category | Reference type | Reference type | Value type |
| Default equality | Object identity | Generated member-based equality | Generated member-based equality |
| Assignment | Copies a reference | Copies a reference | Copies the value |
| Positional properties | Not synthesized by a class primary constructor | Usually `init` properties | Usually mutable properties |
| Nondestructive updates | Implement explicitly | `with` expression | `with` expression |
| Typical role | Entity, service, resource owner | DTO, message, value object | Small value without reference identity |

Here, "ordinary class" means a class that has not customized equality. A class can override `Equals`, implement `IEquatable<T>`, and overload operators. A record generates much of that work, so choosing one is also choosing a public semantic contract.

## Reference Equality Versus Value Equality

Consider two customer snapshots:

```csharp
var first = new CustomerSnapshot(42, "Mina");
var second = new CustomerSnapshot(42, "Mina");

Console.WriteLine(first == second);                  // True
Console.WriteLine(first.Equals(second));             // True
Console.WriteLine(ReferenceEquals(first, second));    // False

var entity1 = new CustomerEntity { Id = 42, Name = "Mina" };
var entity2 = new CustomerEntity { Id = 42, Name = "Mina" };

Console.WriteLine(entity1 == entity2);                // False

public sealed record CustomerSnapshot(int Id, string Name);

public sealed class CustomerEntity
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
}
```

The record instances are distinct objects, but their generated equality compares their contents. The class instances use reference equality in this example. Assigning either reference-type object to another variable still copies the reference; a record class does not acquire value-type assignment behavior.

For the broader assignment distinction, see [C# Value Types vs Reference Types](https://dev.jun-kim.net/2026/09/10/c-value-types-vs-reference-types-a-complete-guide/).

Generated record equality also accounts for record type, so different derived record types do not become equal merely because their inherited properties match. Sealing a record is often sensible when a DTO has no intended inheritance contract.

Equality matters beyond an `if` statement. Dictionaries, sets, deduplication, caching, and tests all rely on it. Ask whether two instances with identical data should actually collapse into one logical value.

## Positional Records Are a Public API

A positional declaration supplies constructor parameters, properties, and deconstruction support:

```csharp
public sealed record DeliveryAddress(
    string Street,
    string City,
    string PostalCode);
```

Usage is concise:

```csharp
var address = new DeliveryAddress("10 King St", "Toronto", "M5H 1A1");
var (street, city, postalCode) = address;
```

That convenience exposes the chosen shape to callers. Renaming properties can affect serialized contracts; changing the constructor's parameters can affect construction and deconstruction. Do not treat a public positional record as a private bag of fields that can be rearranged freely.

A non-positional record is available when named initialization is clearer:

```csharp
public sealed record SearchOptions
{
    public required string Query { get; init; }
    public int PageSize { get; init; } = 25;
}
```

The `required` modifier asks normal C# callers to initialize the property. It does not prove that the string is nonempty or that the object satisfies business rules. Boundary validation and domain invariants remain separate responsibilities.

## Immutability Is a Design Choice

The positional properties of a record class are normally init-only. They can be supplied during construction or initialization, but ordinary later assignment is rejected.

```csharp
var original = new DeliveryAddress("10 King St", "Toronto", "M5H 1A1");
// original.City = "Ottawa"; // Does not compile.
```

You can still write mutable properties inside a record, and ordinary classes can be immutable through get-only properties and constructors. The keyword does not decide the entire mutation policy.

More subtly, init-only references do not freeze their targets. This type contains a mutable list:

```csharp
public sealed record TeamSnapshot(string Name, List<string> Members);
```

For a stable snapshot, copy mutable input and avoid exposing a writable collection. An `IReadOnlyList<T>` property restricts methods available through that interface but does not freeze the underlying list.

## What a `with` Expression Copies

A `with` expression creates a new record value while changing selected properties:

```csharp
var previous = new DeliveryAddress("10 King St", "Toronto", "M5H 1A1");
var corrected = previous with { Street = "12 King St" };

Console.WriteLine(previous.Street);  // 10 King St
Console.WriteLine(corrected.Street); // 12 King St
```

This is useful for application state, test fixtures, and transformations where retaining the previous value makes the code easier to reason about.

The default copy is shallow. References stored in members are copied, not recursively cloned:

```csharp
var original = new TeamSnapshot("Platform", new List<string> { "Mina" });
var renamed = original with { Name = "Infrastructure" };

renamed.Members.Add("Alex");

Console.WriteLine(original.Members.Count); // 2
Console.WriteLine(ReferenceEquals(
    original.Members, renamed.Members));   // True
```

Use an explicit copying policy when nested objects can change; `with` does not clone them recursively.

A further trap is calculating a stored property from positional data once, then changing that data through `with`. The stored result can become stale. Prefer a calculated property when it must always reflect the current members:

```csharp
public sealed record Price(decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}
```

## Collection Equality Is Not Automatically Structural

Generated equality delegates to each member's equality behavior. For ordinary arrays and lists, that normally means reference equality:

```csharp
var first = new Tags(new[] { "csharp", "dotnet" });
var second = new Tags(new[] { "csharp", "dotnet" });

Console.WriteLine(first == second); // False

public sealed record Tags(string[] Values);
```

Both records contain the same strings, but the arrays are different objects. Microsoft's [record type documentation](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records) illustrates this distinction between member equality and deep comparison.

Immutability does not change this equality rule: even an immutable collection uses its own equality implementation rather than automatic element-by-element comparison.

If a collection defines the value's identity, consider a dedicated value object with an explicit equality implementation. Decide whether order and duplicates matter, then implement a compatible hash code. Comparing sequences while leaving a reference-based hash code breaks hash-based collection behavior.

Avoid mutable equality-bearing members in dictionary keys. If a key's equality or hash code changes after insertion, lookups can become unreliable. Record syntax does not protect you from that problem.

## `record class` and `record struct`

These declarations represent different storage and copying semantics:

```csharp
public record class CustomerName(string Given, string Family);
public readonly record struct GridPosition(int Row, int Column);
public record struct MutablePosition(int Row, int Column);
```

A plain `record` is shorthand for `record class`. It is a reference type. A `record struct` is a value type, and its positional properties are mutable unless the declaration uses `readonly`.

A small, naturally defaultable pair of coordinates may fit a readonly record struct. A large response containing multiple references usually does not benefit from becoming a struct just because it represents data. Value copies, boxing, default values, and API expectations all matter; neither form is universally faster.

Every struct has a default value. A strongly validated identifier implemented as a record struct must account for `default`, which can bypass the meaningful constructor path and produce an empty underlying value.

## DTOs, Messages, and Value Objects

Records fit DTOs when the payload is a snapshot:

```csharp
public sealed record OrderSummary(
    Guid Id,
    decimal Total,
    string Currency,
    string Status);
```

A query can project into `OrderSummary` without exposing a tracked entity. A later response is another snapshot, not a mutation of the previous response. Records also work well for commands and event payloads whose intended meaning is "these values were supplied."

However, a record is not a substitute for an API contract review. Avoid leaking persistence fields, authentication data, or server-controlled properties into input DTOs. Binding directly to an entity creates risks that changing `class` to `record` does not fix.

Domain value objects can also be records when their equality is correct. They still need validation and normalization. A currency amount should not accept arbitrary currency codes merely because its constructor was easy to generate. For strict invariants, explicit constructors and get-only properties can be preferable to public init properties that let a `with` expression create invalid combinations.

## When a Normal Class Is the Better Choice

An order has identity and a lifecycle. Cancelling it should enforce rules:

```csharp
public sealed class Order
{
    public Guid Id { get; }
    public bool IsShipped { get; private set; }
    public bool IsCancelled { get; private set; }

    public Order(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An order ID is required.", nameof(id));

        Id = id;
    }

    public void Ship()
    {
        if (IsCancelled)
            throw new InvalidOperationException("A cancelled order cannot ship.");

        IsShipped = true;
    }

    public void Cancel()
    {
        if (IsShipped)
            throw new InvalidOperationException("A shipped order cannot be cancelled.");

        IsCancelled = true;
    }
}
```

The important behavior is the permitted transition, not equality across all fields. An ordinary class makes that emphasis clearer. EF Core tracked entities also generally fit classes because tracking depends on object identity.

For the persistence boundary around such entities, see [Repository Pattern in .NET](https://dev.jun-kim.net/2026/09/11/repository-pattern-in-net-when-it-helps-and-when-it-doesnt/).

Services, database contexts, streams, and resource owners are other natural classes. Comparing two mail senders by their configuration does not make them interchangeable objects with the same lifecycle.

## Common Mistakes

- Choosing records only to save constructor boilerplate, without reviewing equality.
- Assuming a record class is a value type.
- Making a mutable record a set member or dictionary key.
- Exposing every domain property as `init`, allowing callers to bypass valid transitions.
- Converting all existing DTOs without checking serialization and caller compatibility.

Changing an established class to a record can alter tests, equality, inheritance, and API behavior. Make that a deliberate migration with relevant contract tests.

## Practical Decision Guide

| Question | Starting choice |
| --- | --- |
| Is this an immutable response, command, or event snapshot? | Record class |
| Should identical member values make instances equal? | Record, after reviewing every member |
| Is this a small value where default and copying semantics are acceptable? | Readonly record struct |
| Does the object have persistent identity and controlled transitions? | Ordinary class |
| Does it own resources or represent a service? | Ordinary class |
| Does it require custom collection equality or strict cross-field invariants? | Explicit value-object design; record is optional |

Choose the meaning first, then the syntax. A record is most useful when its generated behavior expresses the model you intended to build.
