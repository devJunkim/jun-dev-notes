---
title: "LINQ Performance in C#: Deferred Execution, Multiple Enumeration, and Common Pitfalls"
excerpt: "Understand when LINQ runs, what repeated enumeration costs, and how to choose operators and materialization boundaries for production C# code."
category: "C#"

seo:
  focusKeyword: "LINQ performance in C#"
  description: "Improve LINQ performance in C# by understanding deferred execution, multiple enumeration, buffering, materialization, and database query boundaries."
  socialTitle: "LINQ Performance in C#: Execution and Common Pitfalls"
  socialDescription: "Trace the work behind a LINQ query and avoid repeated enumeration, unnecessary allocations, and accidental client-side filtering."
---

# LINQ Performance in C#: Deferred Execution, Multiple Enumeration, and Common Pitfalls

A LINQ expression can describe a cheap pass over an array or a query that reads thousands of database rows. The syntax alone does not tell you which one you have.

When a query is slow, start by asking what its source does, when execution begins, and how often the result is consumed. Replacing readable LINQ with a loop before answering those questions can leave the expensive work untouched.

> **Quick answer:** LINQ performance depends on the source, operator behavior, enumeration count, allocations, and provider translation. Materialize when you need a reusable result; keep a pipeline deferred when one streaming pass is enough.

## A Query Variable Is Often a Recipe

For LINQ to Objects, `Where` and `Select` normally construct deferred pipelines. They do not create a collection containing every result. Enumeration pulls elements from the source, evaluates predicates, and projects the elements that pass.

This complete console example makes that work visible:

```csharp
static IEnumerable<int> ReadAmounts()
{
    foreach (var amount in new[] { 10, 25, 40 })
    {
        Console.WriteLine($"Reading {amount}");
        yield return amount;
    }
}

var amounts = ReadAmounts().Where(amount => amount >= 25);
Console.WriteLine("Query constructed");

Console.WriteLine($"Count: {amounts.Count()}");
foreach (var amount in amounts)
{
    Console.WriteLine($"Amount: {amount}");
}
```

The source prints nothing when `amounts` is assigned. `Count()` consumes this filtered iterator once; the `foreach` consumes it again. Each pass reads all three source values.

That second pass is a decision, even if the code does not make it obvious. A custom iterator might read a file or fetch API pages. Enumerating an EF Core query again normally executes another database query. An already materialized array does neither, although its predicates and projections may still be repeated.

An `IEnumerable<T>` contract does not promise cheap, repeatable enumeration. Some sources are stateful or usable only once. The existing [IEnumerable vs ICollection vs IList article](https://dev.jun-kim.net/2026/09/11/c-ienumerable-vs-icollection-vs-ilist-whats-the-difference/) explains what those collection contracts actually provide.

## Deferred Does Not Always Mean Streaming

Execution timing and memory behavior are different questions.

| Operation | Typical LINQ to Objects behavior |
| --- | --- |
| `Where`, `Select` | Deferred; can produce results incrementally |
| `Take` | Deferred; limits how much downstream code requests |
| `OrderBy` | Deferred, but ordered enumeration buffers the source |
| `GroupBy` | Deferred, but builds groups before yielding them |
| `ToList`, `ToArray` | Execute now and retain the results |
| `Any`, `Count`, `First` | Execute now to produce a scalar answer |

An ordered enumeration must inspect the source before it knows which element belongs first. Sorting a large sequence therefore changes both the latency of the first result and the memory requirement.

Runtime implementations can optimize particular operator combinations. For example, a terminal operation on an ordered sequence need not use the same algorithm as a full ordered `foreach`. Treat the table as a guide to consumption, not a promise about every internal allocation or comparison.

Also avoid relying on side effects inside `Select`. An operation such as `Count()` can sometimes determine a result without evaluating a projection. Use an explicit loop when the purpose is to perform work for each item.

## Materialize at a Deliberate Boundary

If a bounded report needs both a count and the same rows, capturing the result once is reasonable. Using the `ReadAmounts` method above:

```csharp
var snapshot = ReadAmounts()
    .Where(amount => amount >= 25)
    .ToArray();

Console.WriteLine($"Count: {snapshot.Length}");
foreach (var amount in snapshot)
{
    Console.WriteLine($"Amount: {amount}");
}
```

Now the iterator runs once. Choose an array for a fixed-size result or a list when the consumer needs list operations; neither choice makes the contained objects deeply immutable. For reference-type elements, materialization copies references, as discussed in [C# Value Types vs Reference Types](https://dev.jun-kim.net/2026/09/10/c-value-types-vs-reference-types-a-complete-guide/).

Materialization is less attractive when the source is large and the caller needs only one pass. It retains results that could otherwise be processed incrementally. It also cannot finish for an unbounded sequence.

Avoid chains such as `source.ToList().Where(...).ToList()` unless the intermediate snapshot has a real purpose. Apply the relevant filter before collecting the result. For a database source, bound the query with appropriate filtering and pagination rather than treating available memory as the limit.

A snapshot stabilizes the sequence you captured. It does not create a database transaction or guarantee that separately executed queries observed the same database state.

## Ask for the Answer You Need

Use `Any(predicate)` when the business question is whether a match exists. Use `Count(predicate)` when the actual number matters.

On a general enumerable, existence can stop at the first match, while counting matches requires reaching the end. But `Count()` without a predicate can use a collection's stored count, and modern LINQ has additional fast paths. “`Any` is always faster” is too broad.

When the variable is already a `List<T>`, `list.Count > 0` is clear. With an array, use `array.Length > 0`. Do not materialize an unknown sequence just to inspect its count.

For `IQueryable<T>`, these operators are interpreted by a provider. EF Core commonly translates existence into an `EXISTS`-style query and counting into an aggregate. Inspect generated SQL and the query plan when the distinction matters to a hot path.

### Cardinality is part of correctness

| Operator | No match | Multiple matches | Intended question |
| --- | --- | --- | --- |
| `First` | Throws | Returns first | Which matching item comes first? |
| `FirstOrDefault` | Returns default | Returns first | Is there a first item? |
| `Single` | Throws | Throws | Is there exactly one item? |
| `SingleOrDefault` | Returns default | Throws | Is there at most one item? |

For an arbitrary enumerable, `Single` must establish that another match does not exist. `First` can stop sooner. Replacing `Single` with `First` to save work also removes a cardinality check; that can hide a data defect.

Neither operator replaces a database uniqueness constraint. And “first” requires a meaningful ordering when the choice matters; a relational query without `OrderBy` has no guaranteed row order.

Remember that default is not always an unambiguous absence marker. `FirstOrDefault()` on integers returns zero when empty, even if zero is a valid result. Select a nullable value or use a result type when absence must be represented separately.

## Filter Before Expensive Transformations

If eligibility depends only on the original value, reject ineligible records before doing expensive work:

```csharp
public sealed record Invoice(int Id, decimal Balance, bool IsOpen);
public sealed record InvoicePreview(int Id, string Document);

public static class InvoiceQueries
{
    public static InvoicePreview[] BuildPreviews(
        IEnumerable<Invoice> invoices,
        Func<Invoice, string> renderDocument)
    {
        return invoices
            .Where(invoice => invoice.IsOpen && invoice.Balance > 0)
            .Select(invoice => new InvoicePreview(
                invoice.Id, renderDocument(invoice)))
            .ToArray();
    }
}
```

Here document rendering runs only for open invoices with a positive balance. The final array is appropriate if the caller needs all previews now; return a deferred sequence instead if ownership and lifetime allow streaming.

Reordering operators is valid only when it preserves meaning. A filter that depends on the rendered document cannot simply move ahead of rendering. Moving `Take` before `OrderBy` selects from a different population. Side effects make these transformations harder to reason about, which is another reason to keep query delegates focused on computation.

Repeated searches inside a projection deserve attention too. If each invoice scans the entire customer list, the nested work can dominate the LINQ overhead. Building a dictionary or lookup once may better match the access pattern. Choose between them based on whether duplicate keys are valid, and account for the memory cost.

## Keep Database Work on the Intended Side

`IQueryable<T>` composition is not ordinary delegate execution over objects. A provider translates supported expressions. For a fuller explanation, see [IEnumerable vs IQueryable in .NET](https://dev.jun-kim.net/2026/09/11/ienumerable-vs-iqueryable-in-net-whats-the-difference/).

This focused EF Core method assumes the caller supplies a provider-backed query:

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class InvoiceRow
{
    public int Id { get; set; }
    public decimal Balance { get; set; }
    public bool IsOpen { get; set; }
}

public sealed record InvoiceSummary(int Id, decimal Balance);

public static class InvoiceReadModel
{
    public static Task<List<InvoiceSummary>> ReadPageAsync(
        IQueryable<InvoiceRow> invoices,
        CancellationToken cancellationToken)
    {
        return invoices
            .Where(invoice => invoice.IsOpen)
            .OrderBy(invoice => invoice.Id)
            .Select(invoice => new InvoiceSummary(
                invoice.Id, invoice.Balance))
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}
```

Filtering, ordering, projection, and the row limit remain in the provider expression until execution. The fixed page size is illustrative; a real endpoint needs its own pagination contract and authorization filter.

Calling `AsEnumerable()` before `Where` would make that subsequent filter a LINQ to Objects operation. `AsEnumerable()` itself does not fetch the data; the boundary changes which implementation receives later operators. Calling `ToList()` before the filter both executes and retains the unfiltered result.

Modern EF Core does not silently run every unsupported expression on the client. Unsupported expressions outside the top-level projection generally fail translation. Explicitly move to client processing only after reducing the dataset, and verify behavior against the actual provider. Compiling this method cannot prove its SQL plan or performance.

## A Production Review Checklist

- Identify the source and every enumeration point, including logging, counting, and serialization.
- Decide whether consumers need one pass, a reusable snapshot, or separate fresh reads.
- Check buffering, result size, and whether filtering occurs before expensive work.
- Preserve existence, uniqueness, ordering, and absence semantics when changing operators.
- For database queries, inspect round trips, selected columns, generated SQL, and indexes.
- Measure the real workload before replacing readable code with a more complex implementation.

The useful optimization is usually the one that removes unnecessary work: a repeated query, an oversized result, a nested scan, or an expensive projection applied too early. Start there before treating LINQ syntax itself as the problem.
