---
title: "C# const vs readonly vs static readonly: What's the Difference?"
excerpt: "Learn when to use const, readonly, and static readonly in C#, how their initialization rules differ, and which choice produces safer APIs."
category: "C#"

seo:
  focusKeyword: "C# const readonly and static readonly"
  description: "Compare const, readonly, and static readonly in C#, including initialization rules, allowed types, runtime behavior, and API versioning risks."
  socialTitle: "C# const vs readonly vs static readonly"
  socialDescription: "Understand compile-time constants, per-instance readonly fields, and shared static readonly values with practical C# examples."
---

# C# const vs readonly vs static readonly: What's the Difference?

Values that should not change appear everywhere: tax-rate defaults, protocol names, application start times, and configuration-derived limits. C# offers `const`, `readonly`, and `static readonly`, but they represent three different contracts rather than three spellings of “immutable.”

The key questions are **when the value is determined** and **whether it belongs to each object or to the type itself**. A `const` value is embedded at compile time. An instance `readonly` field is assigned at runtime for each object. A `static readonly` field is assigned at runtime once for the type.

> **Quick answer:** Use `const` for genuine, stable compile-time constants; instance `readonly` for constructor-supplied state that differs per object; and `static readonly` for one runtime-created value shared by every instance.

## C# `const`, `readonly`, and `static readonly` at a Glance

| Characteristic | `const` | Instance `readonly` | `static readonly` |
| --- | --- | --- | --- |
| Value determined | Compile time | Runtime | Runtime |
| Belongs to | Type | Object instance | Type |
| Assignment locations | Declaration only | Declaration or instance constructor | Declaration or static constructor |
| Implicitly static | Yes | No | Already declared `static` |
| Can use runtime-created values | No | Yes | Yes |
| Can differ per object | No | Yes | No |
| Typical use | Mathematical or protocol constants | Immutable object state | Shared runtime value |

All three prevent ordinary reassignment after their allowed initialization point. None automatically makes a referenced object deeply immutable.

## What `const` Means in C#

A `const` field or local variable must be assigned a value the compiler can evaluate. The compiler substitutes that value into code that consumes it.

```csharp
public static class RetryPolicy
{
    public const int DefaultAttemptCount = 3;
    public const string HeaderName = "X-Retry-Count";
    public const double BackoffFactor = 1.5;
}

Console.WriteLine(RetryPolicy.DefaultAttemptCount);
```

Constants are implicitly `static`; writing `static const` is invalid. They must have an initializer at the declaration because there is no constructor-time assignment phase.

### Which types can be `const`?

C# constants are limited to built-in numeric types, `bool`, `char`, `string`, enum types, and reference types assigned `null`. A constant expression may combine literals, other constants, casts, and supported operators as long as the compiler can evaluate the result.

```csharp
public enum LogLevel
{
    Info,
    Warning,
    Error
}

public static class Defaults
{
    public const decimal TaxRate = 0.13m;
    public const LogLevel MinimumLevel = LogLevel.Warning;
    public const string ProductName = "Jun Dev Notes";
    public const object? NoValue = null;
}
```

Types such as `DateTime`, `Guid`, arrays, and custom structs cannot be constants because creating their values requires runtime work.

```csharp
// Invalid: DateTime is not a permitted const type.
// public const DateTime StartedAt = DateTime.UtcNow;

// Invalid: the value is produced at runtime.
// public const string Machine = Environment.MachineName;
```

Even `string.Empty` cannot initialize a `const string` because it is a static field, not a string literal or another constant expression. Use `""` for a compile-time empty string.

### Local constants

`const` is also useful inside a method when a fixed value gives meaning to a calculation.

```csharp
static TimeSpan CalculateDelay(int attempt)
{
    const int millisecondsPerStep = 250;
    return TimeSpan.FromMilliseconds(attempt * millisecondsPerStep);
}
```

A named constant is clearer than an unexplained literal, but not every literal deserves a public symbol. Keep constants close to the code whose meaning they clarify.

## What Instance `readonly` Means

An instance `readonly` field can be assigned at its declaration or in an instance constructor of the declaring type. After construction, that field cannot be reassigned.

```csharp
public sealed class ApiClientOptions
{
    private readonly Uri _baseAddress;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public ApiClientOptions(string baseAddress)
    {
        _baseAddress = new Uri(baseAddress);
    }

    public Uri BaseAddress => _baseAddress;
    public TimeSpan Timeout => _timeout;
}
```

Each `ApiClientOptions` object can receive a different `_baseAddress`. That makes instance `readonly` appropriate for identity and dependencies established during construction.

```csharp
var production = new ApiClientOptions("https://api.example.com");
var local = new ApiClientOptions("https://localhost:7001");

Console.WriteLine(production.BaseAddress);
Console.WriteLine(local.BaseAddress);
```

Every constructor must leave fields in a valid state, but a `readonly` field does not have C# definite-assignment requirements identical to a non-nullable property. A field that is not explicitly initialized receives its type's default value. Nullable-reference analysis can still warn when a non-nullable field may remain `null`.

### `readonly` does not make an object immutable

For a reference-type field, `readonly` protects the stored reference. It does not freeze the object reached through that reference.

```csharp
public sealed class ShoppingCart
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    public void Add(string item) => _items.Add(item);

    public void Reset()
    {
        _items.Clear();
        // _items = []; // Invalid: the readonly field cannot be reassigned.
    }
}
```

The list can still change. If callers must not mutate a collection, avoid exposing the mutable implementation and consider immutable collections when their semantics fit.

For a mutable struct stored in a `readonly` field, member access can involve defensive copies. Prefer immutable value types—often `readonly struct`—when values are intended to remain unchanged.

## What `static readonly` Means

A `static readonly` field stores one value for the type rather than one value per object. It can be initialized at the declaration or in the declaring type's static constructor.

```csharp
public static class ApplicationIdentity
{
    public static readonly Guid InstanceId = Guid.NewGuid();
    public static readonly DateTimeOffset StartedAt;

    static ApplicationIdentity()
    {
        StartedAt = DateTimeOffset.UtcNow;
    }
}
```

The runtime initializes the type before its static members are first used, according to .NET type-initialization rules. Every caller then sees the same `InstanceId` and `StartedAt` for that process.

Unlike `const`, `static readonly` can hold any type and can call methods or constructors during initialization.

```csharp
public static class KnownMediaTypes
{
    public static readonly MediaType Json =
        new("application", "json");
}

public sealed record MediaType(string Type, string Subtype);
```

Use a static constructor when initialization needs multiple statements, validation, or exception handling. Prefer a field initializer when the expression is simple.

### Be careful with mutable shared values

The `readonly` modifier still protects only the field assignment. A mutable object stored in a public `static readonly` field can be changed by any caller, creating shared global state.

```csharp
public static class BadDefaults
{
    public static readonly List<string> Roles = ["Reader"];
}

BadDefaults.Roles.Add("Administrator"); // Legal and globally visible
```

Prefer immutable objects, read-only abstractions backed by private state, or properties that return safe values.

## Compile-Time Constants Versus Runtime Values

The following declarations look similar but behave differently:

```csharp
public static class BuildInformation
{
    public const string ProtocolVersion = "2";

    public static readonly string RuntimeVersion =
        Environment.Version.ToString();
}
```

`ProtocolVersion` is known while the consuming project compiles. `RuntimeVersion` cannot be known until the application runs. Choosing between them based only on visual style misses their most important semantic difference.

Runtime initialization can also fail. If a static field initializer or static constructor throws, the runtime reports a `TypeInitializationException`, and the type may remain unusable for the application lifetime. Avoid network calls, database access, and other fragile work in static initialization.

## Public `const` Values and Assembly Versioning

Public constants have an important versioning risk: consuming assemblies usually embed the value into their own compiled code.

Suppose a library publishes:

```csharp
public static class Limits
{
    public const int MaximumItems = 100;
}
```

An application compiles against `MaximumItems` and receives the literal `100` in its output. If the library later changes the constant to `200`, replacing only the library assembly may not update the already compiled application. The consumer generally must be recompiled.

By contrast, a `public static readonly` field is read at runtime:

```csharp
public static class Limits
{
    public static readonly int MaximumItems = 100;
}
```

Replacing the library can expose the updated field value without recompiling the consumer, assuming the API remains binary compatible.

This does not mean every public constant is wrong. Stable values such as mathematical constants, bit flags, and values fixed by a permanent specification can be appropriate. Avoid public `const` for values likely to change between library releases, including defaults, limits, branding, and feature settings. The official [C# constants documentation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/constants) also calls out this versioning behavior.

## Practical Selection Guide

### Use `const` when

- The value is available at compile time.
- The type is permitted for a constant.
- The value is conceptually permanent, not configuration.
- Embedding the value into consuming assemblies is acceptable.

Examples include mathematical conversion factors, fixed protocol tokens, and private literals used to clarify an algorithm.

### Use instance `readonly` when

- Each object can have a different value.
- The value is supplied or calculated during construction.
- Reassignment after construction would violate the object's state model.

Constructor-injected dependencies and identifiers are common examples. A get-only auto-property may be an equally good or better public-facing choice.

### Use `static readonly` when

- One value should be shared by the type.
- Its creation requires runtime work.
- The type is not allowed for `const`.
- A public value might evolve across library versions.

Examples include runtime-generated identifiers, `Uri` instances, immutable lookup data, and reusable value objects.

### Use configuration instead when

A value is not a constant merely because developers rarely change it. Timeouts, connection endpoints, feature switches, and deployment-specific limits usually belong in configuration. Baking an environment-dependent value into any field makes operation and testing harder.

## Common Mistakes and Misconceptions

### Assuming `readonly` means deeply immutable

It prevents field reassignment after initialization. It does not prevent mutations inside a referenced object. Design the referenced type and exposed API for the level of immutability you need.

### Using `const` for a runtime value

Method calls, constructor calls, environment variables, and configuration values are unavailable in constant expressions. Use `readonly`, `static readonly`, or configuration binding.

### Marking changing defaults as public constants

This creates an assembly-versioning trap because clients embed the old value. Prefer `static readonly` or a property when consumers should observe updates at runtime.

### Making mutable collections public and `static readonly`

The field cannot point to another list, but anyone can mutate the shared list. Keep mutable state private or expose an immutable/read-only representation.

### Choosing `static readonly` when values differ per object

Static state is shared. If a value is derived from constructor input or tenant-specific settings, it should usually be instance state.

## Interview-Oriented Questions

### When is a `const` value evaluated?

At compile time. Uses of the constant are generally replaced with its value in the consuming compiled code.

### Can a constructor assign a `readonly` field?

An instance constructor of the declaring type may assign an instance `readonly` field. A static constructor may assign a `static readonly` field. Arbitrary methods and constructors of derived types cannot perform those assignments.

### Why can `static readonly` hold a `DateTimeOffset` but `const` cannot?

`static readonly` is initialized at runtime and can execute a constructor or method. Constants are restricted to supported constant types and compile-time expressions.

### Is `static readonly` equivalent to a singleton object?

No. It is one field associated with a type. The referenced object can still be mutable, and no broader singleton lifecycle or encapsulation pattern is implied.

### Why is changing a public `const` risky?

Already compiled consumers can retain the old embedded value until they are rebuilt. A runtime-read member such as `static readonly` avoids that particular inlining behavior.

## Summary

`const`, instance `readonly`, and `static readonly` express different initialization and ownership rules:

- `const` is a compile-time value, implicitly static, and limited to constant-compatible types.
- Instance `readonly` is runtime-initialized per object at the declaration or in an instance constructor.
- `static readonly` is runtime-initialized once per type at the declaration or in a static constructor.

Choose based on semantics, not brevity. Reserve `const` for values that are truly constant, use instance `readonly` for stable object state, and use `static readonly` for shared runtime values. For deployment-specific or regularly changing values, use configuration instead.
