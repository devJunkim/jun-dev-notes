---
title: "C# Required Members: Construction Contracts, Inheritance, and API Evolution"
excerpt: "Use C# required members to express caller-owned initialization without confusing compile-time enforcement with validation or non-null guarantees."
category: "C#"

seo:
  focusKeyword: "C# required members"
  description: "Use C# required members with object initializers, constructors, inheritance, nullability, serialization, and compatible API evolution."
  socialTitle: "C# Required Members: Honest Construction Contracts"
  socialDescription: "Understand what required enforces, what it does not validate, and how it affects constructors, inheritance, and compatibility."
---

# C# Required Members: Construction Contracts, Inheritance, and API Evolution

A DTO has a non-nullable `CustomerId`, but callers can still construct it without supplying a meaningful value. Adding `required` looks like a complete fix. It is useful, but it enforces one narrow contract: certain members must appear in the object’s initialization path.

> **Quick answer:** Use `required` when callers should initialize named fields or properties and constructor parameters would make the type less readable. Keep non-nullability, runtime validation, and domain invariants separate. Treat `[SetsRequiredMembers]` as a promise to the compiler, account for inherited requirements, and review compatibility before adding `required` to a public type.

## What `required` Actually Enforces

The compiler requires an object creator to initialize every required member:

```csharp
public sealed class CreateInvoiceRequest
{
    public required string CustomerId { get; init; }
    public required IReadOnlyList<InvoiceLine> Lines { get; init; }
    public string? PurchaseOrderNumber { get; init; }
}

public sealed record InvoiceLine(string Sku, int Quantity);
```

This compiles:

```csharp
var request = new CreateInvoiceRequest
{
    CustomerId = "customer-42",
    Lines = [new InvoiceLine("desk-lamp", 2)],
};
```

Omitting `CustomerId` or `Lines` produces compiler error CS9035. The [C# property documentation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/properties#required-properties) describes this as an initialization requirement on the caller.

Required members must be settable during initialization. An `init` accessor is usually the right fit because it permits initialization without leaving the property mutable afterward.

## Required Does Not Mean Non-Null or Valid

This code satisfies the required-member contract:

```csharp
var request = new CreateInvoiceRequest
{
    CustomerId = null!,
    Lines = [],
};
```

`required` checks that an assignment exists, not that the assigned value is non-null, non-empty, authorized, or valid for the business operation. Nullable reference type analysis can warn about a plain `null`, but warnings can be suppressed and runtime input can still contain null.

Validate untrusted data at the boundary:

```csharp
public static IReadOnlyDictionary<string, string[]> Validate(
    CreateInvoiceRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.CustomerId))
    {
        errors[nameof(request.CustomerId)] = ["Customer ID is required."];
    }

    if (request.Lines is null || request.Lines.Count == 0)
    {
        errors[nameof(request.Lines)] = ["At least one line is required."];
    }

    return errors;
}
```

The type declaration communicates construction intent to C# callers. Validation protects the running system from invalid objects, deserialization behavior, reflection, and other languages that do not enforce the same compiler rule.

## Choose Required Properties or a Constructor Deliberately

A constructor is clearer when values must be validated together before the object exists:

```csharp
public sealed class DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException("End must not precede start.");
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }
}
```

Required properties are often useful for transport models and configuration-shaped objects where named assignments improve readability. They are less compelling for a domain value whose invariants depend on several inputs.

Do not duplicate the same obligation through both a constructor parameter and a required property unless callers genuinely need both construction styles.

## Understand `[SetsRequiredMembers]`

A constructor can promise that it initializes every required member:

```csharp
using System.Diagnostics.CodeAnalysis;

public sealed class ExportOptions
{
    [SetsRequiredMembers]
    public ExportOptions(string destination)
    {
        Destination = destination;
    }

    public required string Destination { get; init; }
    public required string Format { get; init; } = "csv";
}
```

Callers may now write `new ExportOptions("reports")` without an object initializer. The attribute does not inspect the constructor body and prove every assignment. It tells the compiler to trust the constructor. If the constructor forgets `Destination`, the attribute can hide the missing initialization contract while nullable analysis may still report warnings.

Microsoft’s [`SetsRequiredMembers` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/general#setsrequiredmembers-attribute) explicitly describes that trust boundary. Apply the attribute only after reviewing every required member in the type hierarchy.

## Requirements Flow Through Inheritance

Derived types inherit required members:

```csharp
public abstract class Message
{
    public required string CorrelationId { get; init; }
}

public sealed class PaymentRequested : Message
{
    public required decimal Amount { get; init; }
}

var message = new PaymentRequested
{
    CorrelationId = "corr-123",
    Amount = 42.50m,
};
```

Both members participate in the derived type’s construction contract. An override of a required property must remain required, and hiding an inherited required member is rejected because it would make the required-member list ambiguous.

Constructor chains need equal care. A constructor that delegates to another constructor marked `[SetsRequiredMembers]` must carry the attribute too. These rules are detailed in the [C# required-member specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#required-members).

## Review Serialization and Framework Construction

Serializers can recognize required metadata differently across versions and configuration. A successful C# build does not prove that missing JSON will be rejected, that an ORM can materialize the type, or that a model binder will produce the desired validation response.

Test the actual framework boundary:

- Deserialize a complete payload.
- Omit each required member.
- Send explicit null and empty values.
- Verify the error shape exposed to clients.
- Confirm source-generated serialization metadata if the application uses it.

Do not rely on `required` as the only runtime input defense.

## Adding `required` Can Break Existing Callers

Adding a required member to a public type is source-breaking for callers that construct the type. Existing compiled binaries may continue running, which can make the change appear safer than it is until consumers rebuild.

For a shared contract, consider an additive migration:

1. Add an optional member and define server-owned fallback behavior.
2. Update producers to supply it.
3. Measure adoption where possible.
4. Introduce a new contract version if absence can no longer be supported.

Removing `required` relaxes the source contract, but may still reveal assumptions in consumers and serializers. Treat required-member changes as API evolution, not formatting.

## Keep the Contract Honest

Use `required` to make caller-owned initialization visible at the construction site. Use nullable annotations to describe null expectations, runtime validation to reject untrusted values, and constructors or factories to establish cross-field invariants.

That separation keeps the feature valuable: required members improve object creation without pretending the compiler can validate every way an object enters a production system.
