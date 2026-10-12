---
title: "C# Collection Expressions: Target Typing, Spreads, and Performance"
excerpt: "Use C# collection expressions deliberately by understanding target typing, spread evaluation, overload ambiguity, and allocation behavior."
category: "C#"

seo:
  focusKeyword: "C# collection expressions"
  description: "Learn how C# collection expressions use target typing, evaluate spread elements, resolve overloads, and affect allocation and API design."
  socialTitle: "C# Collection Expressions Without Allocation Surprises"
  socialDescription: "Use concise collection syntax while keeping target types, enumeration, mutability, and performance behavior explicit."
---

# C# Collection Expressions: Target Typing, Spreads, and Performance

C# collection expressions make arrays, lists, spans, and other collections look pleasantly uniform. The same brackets can nevertheless produce different concrete storage, mutability, and lifetime behavior depending on the receiving type.

> **Quick answer:** Treat a collection expression as target-typed construction syntax, not as a collection type of its own. Choose the target type deliberately, remember that spreads enumerate and copy their inputs, and measure hot paths instead of assuming that shorter syntax means fewer allocations.

## The Target Determines the Result

A collection expression has no standalone type. The assignment, argument, or return context supplies one:

```csharp
int[] array = [1, 2, 3];
List<int> list = [1, 2, 3];
ReadOnlySpan<int> span = [1, 2, 3];
IReadOnlyList<int> view = [1, 2, 3];
```

Microsoft's [collection expression reference](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/collection-expressions) documents the supported conversions and compiler optimizations. The important design question is not whether the syntax compiles; it is which contract callers should observe.

Use an array when fixed contiguous storage is part of the contract, a `List<T>` when mutation is intended, and a read-only interface when callers should not depend on the concrete type. A read-only interface prevents mutation through that reference, but it does not promise immutable backing data.

For buffer-oriented APIs, [Span<T> and Memory<T> in C#: Safe Buffer APIs Without Hidden Copies](https://dev.jun-kim.net/2026/09/24/span-and-memory-in-c-safe-buffer-apis-without-hidden-copies/) explains the lifetime boundary between synchronous span use and heap-safe memory.

## Spreads Enumerate into a New Collection

The spread element inserts every item from an enumerable source:

```csharp
int[] defaults = [10, 20];
int[] selected = [30, 40];
int[] combined = [.. defaults, .. selected, 50];
```

`..` is not a lazy concatenation operator. The resulting collection contains all produced elements, so the spread source is enumerated and its values are copied into new storage. This differs from a deferred LINQ pipeline such as `defaults.Concat(selected)`.

That distinction matters when a source performs I/O, changes between enumerations, throws partway through, or is expensive to enumerate. Materialize intentionally and avoid hiding a side-effecting sequence behind compact syntax.

When the compiler knows source lengths, it can allocate efficiently. For an arbitrary `IEnumerable<T>`, it may need a growable intermediate representation. Benchmark the actual target and source shapes if the operation is frequent or handles large collections.

## Empty Does Not Mean Typeless

An empty expression still needs a target:

```csharp
int[] noIds = [];
List<string> noNames = [];

static IReadOnlyList<Guid> FindIds(bool enabled) =>
    enabled ? [Guid.NewGuid()] : [];
```

This makes empty return paths concise. It does not guarantee a new allocation: the compiler can reuse an empty representation when the target contract allows it. Do not write code that depends on reference identity for empty collections.

## Overloads Can Become Ambiguous

Because one expression can convert to several collection types, overload sets need care:

```csharp
static void Process(int[] values) { }
static void Process(List<int> values) { }

Process((int[])[1, 2, 3]);
```

The cast states the intended target. A better API usually avoids overloads distinguished only by closely related collection shapes. Prefer `ReadOnlySpan<T>` for synchronous, non-retained read access or `IEnumerable<T>` when deferred enumeration is genuinely part of the contract.

Do not retain a span beyond its valid lifetime, and do not accept `IEnumerable<T>` merely to appear general if the implementation immediately needs a count and random access.

## Custom Collections Need Honest Semantics

Custom collection types can support collection expressions through normal collection-initializer behavior or a collection builder. A builder can construct an optimized representation from `ReadOnlySpan<T>`, but it becomes part of the type's correctness boundary.

The collection should be well behaved: its count should match enumeration, and bulk addition should produce the same logical result as adding elements individually. Surprising enumeration side effects can make compiler optimizations observably inconsistent.

Only add builder support when the type has a stable collection contract and construction is common enough to justify the extra API surface.

## Review the Produced Contract

During review, inspect the target type, spread sources, mutability expectations, and whether the value escapes the current method. Test order, duplicate handling, empty inputs, and exceptions from spread enumeration.

Collection expressions improve readability when the target contract is already clear. They become risky when concise syntax hides a choice about ownership, allocation, or evaluation that the surrounding API should make explicit.
