---
title: "C# Primary Constructors: State, Validation, and API Design"
excerpt: "Use C# primary constructors without confusing parameters with properties, duplicating state, or weakening validation and dependency boundaries."
category: "C#"

seo:
  focusKeyword: "C# primary constructors"
  description: "Understand C# primary constructor parameters, captured state, validation, inheritance, and when conventional constructors communicate intent better."
  socialTitle: "C# Primary Constructors: State and API Design"
  socialDescription: "Use concise constructor syntax while keeping object state, validation, and public contracts explicit."
---

# C# Primary Constructors: State, Validation, and API Design

Primary constructors reduce ceremony, but their parameters do not automatically become properties. That distinction matters when a type validates input, exposes state, participates in inheritance, or is inspected by serializers and frameworks.

> **Quick answer:** Use a primary constructor when required inputs are obvious and initialization remains readable. Copy parameters into explicitly named properties or fields when they are part of durable state, validate at initialization, and avoid referencing both a stored member and its original parameter because that can create duplicate storage.

## Parameters Are Not Members

C# 12 allows parameters on a class or struct declaration:

```csharp
public sealed class RetryPolicy(int maxAttempts, TimeSpan delay)
{
    public int MaxAttempts { get; } = maxAttempts > 0
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts));

    public TimeSpan Delay { get; } = delay >= TimeSpan.Zero
        ? delay
        : throw new ArgumentOutOfRangeException(nameof(delay));
}
```

Unlike positional record parameters, `maxAttempts` and `delay` do not synthesize public properties. Microsoft's [primary constructor guidance](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors) describes them as constructor parameters whose scope extends across the type body.

Explicit properties make the durable contract visible to callers, serializers, debuggers, and reflection-based tools. They also keep later methods from depending on hidden captured state.

## Avoid Accidental Double Storage

When a parameter is referenced by an instance method, the compiler may capture it in generated storage:

```csharp
public sealed class EndpointClient(Uri endpoint)
{
    public Uri Endpoint { get; } = endpoint;

    public string Describe() => endpoint.AbsoluteUri;
}
```

Here `endpoint` initializes `Endpoint` and is also used later, so the object may retain two references. Use the property consistently instead:

```csharp
public string Describe() => Endpoint.AbsoluteUri;
```

Compiler warnings such as CS9124 help identify this pattern. Treat them as design feedback rather than suppressing them automatically.

## Validate During Initialization

Property initializers can validate primary-constructor inputs. Keep validation expressions readable and free of side effects:

```csharp
public sealed class ExportJob(string destination, int batchSize)
{
    public string Destination { get; } =
        string.IsNullOrWhiteSpace(destination)
            ? throw new ArgumentException("Destination is required.", nameof(destination))
            : destination.Trim();

    public int BatchSize { get; } = batchSize is > 0 and <= 10_000
        ? batchSize
        : throw new ArgumentOutOfRangeException(nameof(batchSize));
}
```

When initialization requires branching, resource acquisition, or several dependent checks, a conventional constructor is often clearer. Concision is not worth hiding the order in which invariants are established.

## Dependency Injection Is a Good Fit—with Limits

Primary constructors work naturally for injected services:

```csharp
public sealed class InvoiceService(
    IInvoiceRepository repository,
    ILogger<InvoiceService> logger)
{
    public async Task<Invoice?> FindAsync(Guid id, CancellationToken token)
    {
        logger.LogDebug("Loading invoice {InvoiceId}", id);
        return await repository.FindAsync(id, token);
    }
}
```

The dependencies remain implementation details. If null guards, decoration, or multiple construction modes are necessary, explicit fields and a conventional constructor may communicate ownership better. Do not expose injected services as public properties merely to make the syntax resemble a record.

## Inheritance Can Capture the Same Value Twice

A derived primary constructor can pass a parameter to its base type. If the derived type also references that parameter later, both levels may store it. Prefer using a protected base member when the derived type genuinely needs the same state.

Every additional constructor must ultimately delegate to the primary constructor with `this(...)`. This ensures the primary parameters are supplied, but it can make complex construction paths harder to follow.

## Choose Syntax by the Contract

Use primary constructors when they make required inputs immediately visible and the type has straightforward initialization. Prefer conventional constructors when you need complex validation, several overloads, defensive copying, or a clear breakpoint for construction logic.

Before merging, inspect compiler warnings, verify serialized shape explicitly, and test invalid inputs. Primary constructors change syntax and possible storage; they do not change the responsibility to establish a valid object.
