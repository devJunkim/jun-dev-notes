---
title: "Nullable Reference Types in C#: Designing Honest API Contracts"
excerpt: "Use nullable annotations, flow analysis, guard clauses, and Try-pattern attributes to make absence explicit without confusing compiler warnings with runtime validation."
category: "C#"

seo:
  focusKeyword: "C# nullable reference types"
  description: "Design C# nullable reference type contracts with runtime guards, NotNullWhen, required members, and a practical migration strategy."
  socialTitle: "C# Nullable Reference Types: Honest API Contracts"
  socialDescription: "Make missing values explicit at API boundaries and remove unsafe null assumptions without spreading suppression operators."
---

# Nullable Reference Types in C#: Designing Honest API Contracts

A method that returns `string` but sometimes produces `null` asks every caller to discover an undocumented rule. Nullable reference types make that rule visible, but only if the annotations describe what the implementation actually guarantees.

The production problem is not eliminating every question mark. It is deciding where absence is legitimate, where invalid input must be rejected, and which guarantees callers can rely on.

> **Quick answer:** Use nullable annotations to describe absence, runtime guards to enforce boundary invariants, and flow-analysis attributes for conditional guarantees such as successful `Try` methods. Treat `!` as an assertion that needs evidence, not a way to repair data.

## An Annotation Is a Contract, Not a Runtime Check

In a nullable-enabled project, `string` expresses an intended non-null value and `string?` permits absence. Both are the same runtime reference type. The compiler follows assignments and control flow to warn about possible misuse; it does not insert a guard at every method call. Microsoft's [nullable reference types guide](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/null-safety/nullable-reference-types) explains this distinction.

Enable the analysis in the project file:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Choose the annotation from the domain meaning. A missing middle name may be valid. A missing routing code needed to deliver a parcel may make the request invalid. Replacing both with `string.Empty` removes a warning while losing the distinction between absent, empty, and valid.

For a lookup, `Customer?` can honestly represent no match. If the caller must distinguish missing, forbidden, and unavailable, a nullable return alone is too small a contract. Use an explicit outcome model or a documented exception policy instead of assigning several meanings to `null`.

## Validate Once at the Boundary That Owns the Invariant

Consider a display label accepted from an import file. The input model allows incomplete data; the application value does not:

```csharp
public sealed class ImportRow
{
    public string? DisplayName { get; init; }
}

public sealed class DisplayLabel
{
    public string Value { get; }

    public DisplayLabel(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalized = value.Trim();
        if (normalized.Length > 80)
        {
            throw new ArgumentException("Display label exceeds 80 characters.", nameof(value));
        }

        Value = normalized;
    }
}

public static class ImportMapping
{
    public static DisplayLabel ToLabel(ImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string name = row.DisplayName
            ?? throw new ArgumentException("Display name is required.", nameof(row));

        return new DisplayLabel(name);
    }
}
```

The mapping decides that missing data is invalid. The constructor also enforces its own public contract, including calls from nullable-oblivious code. Downstream code can then use `Value` without repeatedly guessing whether construction succeeded.

The 80-character limit is an illustrative application rule, measured here in UTF-16 code units. A product requirement based on user-perceived characters needs a different measurement. Null safety cannot answer that domain question.

For bulk imports, throwing for each invalid row may be the wrong operational interface. Translate known validation failures into row results with safe error codes and continue according to the import policy. Keep the invariant while changing how rejection is reported.

## Express Conditional Guarantees with `NotNullWhen`

A `Try` method can return `false` with no result and `true` with a usable result. Its output is nullable overall, but callers should not need a suppression inside the success branch:

```csharp
using System.Diagnostics.CodeAnalysis;

public static class LabelParser
{
    public static bool TryNormalize(
        string? input,
        [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate = input.Trim();
        if (candidate.Length > 80)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
```

The caller can now use the result directly:

```csharp
if (LabelParser.TryNormalize("  Dispatch desk  ", out string? label))
{
    Console.WriteLine(label.Length);
}
```

`NotNullWhen(true)` communicates a relationship between the Boolean return and the output's null-state. It does not make a false implementation safe. Test that every successful branch supplies a result and every rejection follows the documented contract. The [nullable analysis attribute reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis) also covers `NotNull`, `MaybeNull`, and member-initialization contracts.

Use attributes when they explain a reusable API. For a single local branch, a direct check is usually easier to read. [Pattern Matching in C#](https://dev.jun-kim.net/2026/09/22/pattern-matching-in-c-writing-clearer-conditional-logic/) covers expressing local shape and null checks without introducing a helper merely for syntax.

## `required` and `!` Solve Different Problems

A required property forces ordinary C# construction sites to initialize it:

```csharp
public sealed class CreateLabelRequest
{
    public required string DisplayName { get; init; }
}
```

That requirement is about initialization. It does not reject whitespace, enforce the length rule, or make assignments from oblivious code trustworthy. Assigning null to a required non-nullable member produces a nullable warning; it is not a universal runtime barrier. Microsoft's [`required` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/required) separates these responsibilities.

Likewise, `request.DisplayName!` suppresses a warning at that expression. It neither throws early nor replaces a null value. A defensible use might follow a framework lifecycle guarantee that the compiler cannot see, with a test proving the initialization path. A blanket `null!` initializer across request models hides precisely the boundary that deserves inspection.

Serializer and ORM behavior must be checked independently. In particular, enabling nullable annotations on an existing EF Core entity model can affect inferred requiredness and generated migrations, as documented in [EF Core property configuration](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties#required-and-optional-properties). Review the model and schema diff rather than treating that migration as warning cleanup.

## Migrate Around Call Paths, Not Warning Counts

For a large codebase, start with a bounded feature and its public inputs and outputs. Record what missing data means, annotate the interfaces, then follow warnings into implementations and callers. Avoid making every property nullable just to reach a clean build.

Useful review questions include:

- Does a lookup distinguish absence from dependency failure?
- Can a deserializer or external library bypass the construction assumptions?
- Does a collection permit null elements as well as a null collection?
- Are new arrays or default struct values being mistaken for initialized objects?
- Does each suppression have a concrete invariant behind it?

Turn nullable warnings into build failures once the selected scope is ready, and keep runtime tests for malformed external input. A clean compiler result is evidence about annotated control flow; boundary tests establish what happens when actual data violates the intended contract.

The useful result is an API whose caller can tell what absence means and what successful construction promises. Fewer warnings should follow from that design, rather than becoming its substitute.
