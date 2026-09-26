---
title: "Generic Math in C#: Designing Algorithms with Static Abstract Interfaces"
excerpt: "Use C# generic math and static abstract interface members to express numeric capabilities without duplicating algorithms or accepting invalid operations."
category: "C#"

seo:
  focusKeyword: "C# generic math"
  description: "Design C# generic math APIs with INumber, focused operator constraints, checked conversions, overflow rules, and domain-specific numeric types."
  socialTitle: "C# Generic Math and Static Abstract Interfaces"
  socialDescription: "Write reusable numeric algorithms while keeping conversion, overflow, precision, and supported operations explicit."
---

# Generic Math in C#: Designing Algorithms with Static Abstract Interfaces

A library exposes the same averaging algorithm for `int`, `decimal`, and `double`. Three overloads drift apart, while a fourth custom numeric type cannot participate without another copy.

Generic math lets an algorithm state which operators and static members it needs. The hard part is not syntax; it is choosing a constraint that matches the algorithm's real mathematical assumptions.

> **Quick answer:** Constrain generic numeric code to the smallest capability set it requires. Use `INumber<T>` for broadly number-like behavior, narrower operator interfaces for focused algorithms, and explicit checked or saturating conversions. Document division, overflow, precision, NaN, and empty-input semantics because a shared operator syntax does not make numeric types behave identically.

## Static Abstract Members Enable Compile-Time Operations

Traditional interfaces describe instance members. Numeric algorithms also need operators, identities, parsing, and conversions that are static. C# static abstract interface members make those capabilities available through a constrained type parameter.

```csharp
using System.Numerics;

public static T Sum<T>(ReadOnlySpan<T> values)
    where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
{
    T total = T.AdditiveIdentity;
    foreach (T value in values)
    {
        total += value;
    }

    return total;
}
```

The algorithm needs addition and an additive identity, not every feature of a number. That makes it usable by types that can be added but do not meaningfully support division or ordering.

Microsoft's [generic math documentation](https://learn.microsoft.com/en-us/dotnet/standard/generics/math) describes the hierarchy from focused operator interfaces through `INumber<TSelf>`. Static interface members are invoked through `T`, the constrained type parameter—not as ordinary static methods on the interface type.

## Use Broad Constraints Only When the Algorithm Is Broad

An average requires more than addition:

```csharp
using System.Numerics;

public static T Average<T>(ReadOnlySpan<T> values)
    where T : INumber<T>
{
    if (values.IsEmpty)
    {
        throw new ArgumentException("At least one value is required.", nameof(values));
    }

    T total = T.Zero;
    foreach (T value in values)
    {
        total = checked(total + value);
    }

    return total / T.CreateChecked(values.Length);
}
```

This compiles for integers and floating-point types, but the result semantics differ. Integer division truncates. Floating-point addition can lose precision and can produce non-finite values. Decimal arithmetic has a different range and representation.

If truncation is not acceptable, do not advertise the method as a universal average. Constrain to the numeric family the domain accepts, return a different result type, or require a caller-provided division policy.

The `checked` block makes integral and decimal overflow visible where their operators support checked behavior. It does not cause floating-point overflow to throw; infinity remains possible. Tests must reflect each supported type's semantics.

## Make Conversion Policy Visible

Generic math exposes three useful conversion styles:

- `CreateChecked` throws when the value cannot be represented;
- `CreateSaturating` clamps to the destination range;
- `CreateTruncating` discards information according to the conversion rules.

Choose from domain meaning, not convenience. Saturating an image channel may be reasonable. Saturating a financial amount can silently turn bad input into a valid-looking maximum. Truncating an identifier is almost certainly wrong.

```csharp
using System.Numerics;

public static TDestination ConvertReading<TSource, TDestination>(TSource value)
    where TSource : INumberBase<TSource>
    where TDestination : INumberBase<TDestination>
{
    return TDestination.CreateChecked(value);
}
```

The method makes rejection the default. A separately named `ClampReading` can use saturating conversion when that is an intentional product rule.

## Domain Types Need Domain Invariants

A custom type can implement operator interfaces, but satisfying the compiler is not enough. Consider a percentage value:

```csharp
using System.Numerics;

public readonly record struct Percentage(decimal Value)
    : IAdditionOperators<Percentage, Percentage, Percentage>,
      IAdditiveIdentity<Percentage, Percentage>
{
    public static Percentage AdditiveIdentity => new(0m);

    public static Percentage operator +(Percentage left, Percentage right) =>
        new(checked(left.Value + right.Value));
}
```

Should the result be permitted above 100? That depends on whether the type represents a bounded rate, a percentage-point change, or an aggregate. The operator cannot answer without a precise domain definition.

Do not implement `INumber<T>` merely to make a domain type fit every generic algorithm. That interface promises a large numeric surface. Implement only meaningful capabilities, or keep domain operations as named methods when operator syntax would obscure rules.

## Avoid Accidental Type Semantics

Generic code can still encode assumptions that are false for some accepted types:

- `T.Zero` may be a valid identity but not a valid business measurement;
- comparison involving floating-point NaN does not behave like total ordering;
- adding values in a different order can change a floating-point result;
- unsigned subtraction can underflow;
- `Complex` supports arithmetic but not ordinary ordering;
- custom implementations can allocate or perform expensive validation.

Prefer a narrow constraint and state supported types when the algorithm has additional assumptions the type system cannot express. Benchmarks should include the actual numeric types and data sizes; generic math removes duplicated source code, not the cost of the underlying operations.

## Test Laws and Boundary Values

Test zero, one item, negative values, extrema, overflow, conversion failure, integer truncation, floating-point infinity and NaN where supported, and custom numeric implementations.

Property-based tests can validate identities such as `Sum(empty) == AdditiveIdentity`, but only apply algebraic laws the type promises. Floating-point addition is not associative, so a test that assumes exact regrouping is invalid.

Generic math is most useful when it expresses a real reusable algorithm. The constraint should read like a capability contract, while tests and documentation capture the numeric behavior that interfaces alone cannot guarantee.
