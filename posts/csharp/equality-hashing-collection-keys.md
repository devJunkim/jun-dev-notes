---
title: "Equality and Hashing in C#: Designing Reliable Collection Keys"
excerpt: "Implement C# equality and hash codes as one stable contract so records, value objects, dictionaries, and sets behave predictably in production."
category: "C#"

seo:
  focusKeyword: "C# equality and hashing"
  description: "Design reliable C# equality and hashing contracts for records, value objects, dictionaries, sets, inheritance, and mutable data."
  socialTitle: "C# Equality and Hashing for Reliable Collection Keys"
  socialDescription: "Avoid disappearing dictionary keys and inconsistent value objects by treating equality, hash codes, and mutation as one contract."
---

# Equality and Hashing in C#: Designing Reliable Collection Keys

An object is added to a `HashSet<T>`, one of its properties changes, and the set can no longer find it. Nothing corrupted the collection. The object's identity changed while the collection was still using the hash code from its earlier state.

Equality in C# is more than an `Equals` implementation. It is a contract shared by equality operators, hash-based collections, records, inheritance, and every property that participates in identity.

> **Quick answer:** Define equality from immutable, domain-significant values. Equal objects must return the same hash code for the duration of their use as keys. Implement `IEquatable<T>`, `Equals(object?)`, `GetHashCode`, and operators consistently—or use a record when its generated value semantics match the domain.

## Decide What Makes Two Values the Same

Reference equality asks whether two variables point to the same object. Value equality asks whether two objects represent the same domain value. Neither is universally correct.

An entity such as a customer normally keeps its identity when its display name changes. A value object such as a currency amount is defined by its amount and currency. Mixing those models causes subtle bugs: comparing all mutable entity fields makes identity unstable, while comparing only a convenient label can merge unrelated entities.

Write the rule in domain language before writing methods:

- Two `Sku` values are equal when their normalized codes are equal.
- Two customer entities are equal when their durable identifiers are equal.
- Two requests with identical fields are not necessarily the same operation.

The rule must also define comparison details. Product codes may be case-insensitive, while cryptographic tokens should not be. Culture-sensitive comparison is rarely appropriate for technical identifiers.

## Implement One Consistent Contract

This immutable value object normalizes its input once and uses ordinal comparison everywhere:

```csharp
public sealed class Sku : IEquatable<Sku>
{
    public string Value { get; }

    public Sku(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToUpperInvariant();
    }

    public bool Equals(Sku? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) =>
        obj is Sku other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(Sku? left, Sku? right) =>
        EqualityComparer<Sku>.Default.Equals(left, right);

    public static bool operator !=(Sku? left, Sku? right) => !(left == right);

    public override string ToString() => Value;
}
```

`IEquatable<Sku>` gives generic collections a strongly typed path. `Equals(object?)` keeps non-generic callers consistent. `GetHashCode` uses the same comparer as equality. The operators delegate to the same contract and handle nulls without duplicating the comparison.

The invariant is one-way: equal objects must have equal hash codes. Unequal objects may collide. A hash code is a bucket hint, not a unique identifier, persistence key, or security boundary.

## Never Mutate Equality State While It Is a Key

The dangerous design is easy to write:

```csharp
public sealed class MutableSku
{
    public string Code { get; set; } = string.Empty;

    public override bool Equals(object? obj) =>
        obj is MutableSku other && Code == other.Code;

    public override int GetHashCode() => Code.GetHashCode();
}
```

If `Code` changes after insertion into a dictionary, lookup computes a new hash and searches a different bucket. Even enumerating the dictionary may still show the key, which makes the failure look intermittent.

Prefer immutable equality components. If a mutable domain object must be indexed, key the collection by a separate immutable identifier:

```csharp
var ordersById = new Dictionary<Guid, Order>();
ordersById.Add(order.Id, order);
```

Removing a key, mutating it, and reinserting it can be correct under strict ownership, but it is harder to reason about under concurrency and exceptions. An immutable key is usually the cleaner boundary.

## Records Generate Semantics, Not Domain Decisions

A record generates value equality from its equality components:

```csharp
public sealed record Money(decimal Amount, string Currency);
```

That is useful when every component belongs in the value. It is wrong when a property is incidental, mutable, or requires a special comparer. A record containing a `List<T>` compares the list reference by default; it does not automatically perform sequence equality.

Record classes also include runtime type in their equality contract, which helps keep derived record values symmetric. Custom equality across an inheritance hierarchy is much harder. A base instance considering a derived instance equal while the derived instance rejects the base violates symmetry.

For domain values, sealing the type often makes the rule explicit. For entities, use a durable identifier and be careful with transient instances whose database-generated ID has not been assigned. Two unsaved objects with the same default ID should not accidentally compare equal.

## Comparers Belong at the Collection Boundary

Sometimes equality is contextual rather than intrinsic. A username index may be case-insensitive even though the original spelling is preserved:

```csharp
var users = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
{
    ["Jun.Kim"] = Guid.NewGuid(),
};

bool exists = users.ContainsKey("jun.kim");
```

Supplying an `IEqualityComparer<T>` keeps that policy local instead of changing the type's global equality. Use the same comparer when data crosses collection boundaries. A database unique index using different collation rules can still accept or reject values differently from the application.

Do not use randomized process hash codes as durable partitions. `HashCode.Combine` and framework string hashing are designed for in-process hash tables, not stable storage or cross-service routing. Choose an explicitly specified stable algorithm when persisted compatibility is required.

## Test Laws, Not Just Examples

An equality test suite should verify:

- reflexivity: `x.Equals(x)` is true;
- symmetry: `x.Equals(y)` matches `y.Equals(x)`;
- transitivity across three equivalent values;
- consistent null behavior;
- equal values produce equal hash codes;
- dictionary and set lookup works through an independently constructed equal value;
- non-identity properties do not unexpectedly change equality.

Property-based tests are useful when normalization has many edge cases. Include Unicode, casing, whitespace, default identifiers, derived types, and values coming from serializers or ORMs.

The best equality implementation is boring: one documented identity rule, immutable components, one comparison policy, and tests that exercise the collections that depend on it.
