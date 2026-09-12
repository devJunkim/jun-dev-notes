---
title: "IEnumerable vs IQueryable in .NET: What's the Difference?"
excerpt: "Learn how IEnumerable and IQueryable behave in LINQ and EF Core, where filters execute, when queries materialize, and where architectural boundaries belong."
category: ".NET"

seo:
  focusKeyword: "IEnumerable vs IQueryable in .NET"
  description: "Compare IEnumerable and IQueryable in .NET, including LINQ to Objects, expression trees, EF Core SQL translation, materialization, and API design."
  socialTitle: "IEnumerable vs IQueryable in .NET"
  socialDescription: "Understand deferred LINQ execution, database versus in-memory filtering, AsEnumerable, AsQueryable, and practical EF Core query design."
---

# IEnumerable vs IQueryable in .NET: What's the Difference?

Two LINQ queries can look almost identical while doing radically different work. One evaluates a delegate against objects in the current process. Another builds an expression tree that Entity Framework Core translates into SQL and sends to a database.

`IEnumerable<T>` and `IQueryable<T>` both support familiar operators such as `Where`, `Select`, and `OrderBy`, but they represent different composition models. The important question is not simply “is the data in memory?” It is which LINQ implementation receives each operation and what the underlying source does when enumerated.

> **Quick answer:** `IEnumerable<T>` is an enumeration contract and normally uses LINQ to Objects for added operators. `IQueryable<T>` carries an expression tree and query provider that may translate operations to another system. Neither interface alone proves that results have already been loaded.

## `IEnumerable<T>` vs `IQueryable<T>` at a Glance

| Characteristic | `IEnumerable<T>` | `IQueryable<T>` |
| --- | --- | --- |
| Primary model | Sequence enumeration | Provider-backed query composition |
| LINQ predicate form | `Func<T, bool>` | `Expression<Func<T, bool>>` |
| Typical execution | .NET code over produced elements | Provider translates expression, often to SQL |
| Deferred execution | Common | Common |
| Requires data already materialized | No | No |
| Supports provider translation | Not for newly added `Enumerable` operators | Yes, when the provider supports the expression |
| Common examples | Arrays, lists, iterators | EF Core `DbSet<T>` queries |

`IQueryable<T>` inherits `IEnumerable<T>`, so every queryable can be enumerated. Its extra members—especially `Expression` and `Provider`—enable another system to interpret the query.

## `IEnumerable<T>` and LINQ to Objects

`IEnumerable<T>` represents a sequence that can produce elements. When normal LINQ operators are applied to an enumerable, the compiler selects methods from `System.Linq.Enumerable`. Those methods accept delegates that run as .NET code.

```csharp
var products = new[]
{
    new Product(1, "Keyboard", 129m, true),
    new Product(2, "Mouse", 49m, true),
    new Product(3, "Dock", 179m, false)
};

IEnumerable<Product> available = products
    .Where(product => product.InStock)
    .OrderBy(product => product.Price);

foreach (var product in available)
{
    Console.WriteLine(product.Name);
}

public sealed record Product(
    int Id,
    string Name,
    decimal Price,
    bool InStock);
```

The filter runs in the process against objects produced by `products`. The query is deferred: `Where` and `OrderBy` create an enumerable pipeline, while enumeration performs the work.

### `IEnumerable<T>` does not mean “already in memory”

An enumerable may lazily read a file, generate values, call an API page by page, or adapt another source. It describes how consumers obtain elements, not where those elements originate.

```csharp
static IEnumerable<string> ReadNonEmptyLines(string path)
{
    foreach (var line in File.ReadLines(path))
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            yield return line;
        }
    }
}
```

`File.ReadLines` does not need to load the entire file before iteration. Conversely, a `List<T>` is materialized but still exposes `IEnumerable<T>`. The interface alone does not settle materialization.

## `IQueryable<T>` and Query Providers

`IQueryable<T>` represents a query as data. LINQ methods from `System.Linq.Queryable` receive expression trees rather than compiled delegates. A provider inspects that tree and decides how to execute it.

With EF Core, a `DbSet<T>` implements `IQueryable<T>`:

```csharp
IQueryable<Product> query = dbContext.Products
    .Where(product => product.InStock)
    .OrderBy(product => product.Price)
    .Select(product => new ProductSummary(
        product.Id,
        product.Name,
        product.Price));

public sealed record ProductSummary(int Id, string Name, decimal Price);
```

No products are loaded merely because the query variable is assigned. EF Core records method calls in an expression tree. When the query executes, the relational provider translates supported operations into SQL similar to:

```sql
SELECT p.Id, p.Name, p.Price
FROM Products AS p
WHERE p.InStock = 1
ORDER BY p.Price;
```

The exact SQL depends on the provider and EF Core version. An expression that works in LINQ to Objects is not necessarily translatable by a database provider. Modern EF Core generally throws when a non-translatable expression appears outside the final projection rather than silently loading an entire table for client filtering.

The official EF Core guidance on [client and server evaluation](https://learn.microsoft.com/en-us/ef/core/querying/client-eval) explains the translation boundary and explicit client evaluation.

## Deferred Execution in Both Models

Both interfaces commonly use deferred execution. Building a query does not usually enumerate it.

```csharp
var query = dbContext.Products
    .Where(product => product.Price >= 100m);

// The database query normally executes here.
var products = await query.ToListAsync(cancellationToken);
```

Execution operators include:

- `ToList()` and `ToListAsync()`
- `ToArray()` and `ToArrayAsync()`
- `First()`, `Single()`, and their asynchronous EF Core variants
- `Any()`, `Count()`, `Sum()`, and related terminal operations
- `foreach` and `await foreach`, depending on the source

Enumerating a deferred query more than once can execute it more than once. With EF Core, that can mean another database round trip.

```csharp
var query = dbContext.Products.Where(product => product.InStock);

var count = await query.CountAsync(cancellationToken); // SQL query 1
var items = await query.ToListAsync(cancellationToken); // SQL query 2
```

Sometimes two targeted queries are correct. Sometimes one materialization followed by an in-memory count is better. Decide from data volume, consistency needs, SQL shape, and latency rather than assuming either is universally optimal.

## Where Filtering Actually Executes

Consider a database query that remains `IQueryable<T>` until materialization:

```csharp
var summaries = await dbContext.Products
    .Where(product => product.InStock)
    .Where(product => product.Price < 200m)
    .Select(product => new ProductSummary(
        product.Id,
        product.Name,
        product.Price))
    .ToListAsync(cancellationToken);
```

Both filters and the projection are available to the EF Core provider for database translation. The database can return only matching columns and rows.

Now materialize too early:

```csharp
var allProducts = await dbContext.Products
    .ToListAsync(cancellationToken);

var summaries = allProducts
    .Where(product => product.InStock)
    .Where(product => product.Price < 200m)
    .Select(product => new ProductSummary(
        product.Id,
        product.Name,
        product.Price))
    .ToList();
```

The first `ToListAsync` requests all product columns and rows. Later operators are LINQ to Objects. This may waste database work, network bandwidth, application memory, and object-tracking overhead.

Premature materialization is not always wrong. It can be intentional when the result set is already bounded, the next operation cannot be translated, one snapshot will be reused, or the application needs to leave the `DbContext` lifetime. The mistake is materializing without recognizing that subsequent filters moved to the client.

## What `AsEnumerable()` Does

`AsEnumerable()` exposes a source as `IEnumerable<T>` without materializing it. It changes which LINQ extension methods are selected for subsequent operators.

```csharp
static string NormalizeName(string value) =>
    value.Trim().ToUpperInvariant();

var names = dbContext.Products
    .Where(product => product.InStock) // Translated to SQL
    .Select(product => product.Name)   // Translated to SQL
    .AsEnumerable()
    .Where(name => NormalizeName(name).StartsWith("A")); // .NET code

foreach (var name in names)
{
    Console.WriteLine(name);
}
```

Enumeration still triggers the EF Core database query for the operations before `AsEnumerable`. The final filter executes in the application as rows arrive. `AsEnumerable()` therefore does **not** mean “the query has already run.” It marks an explicit transition from provider composition to LINQ to Objects.

Use that transition only after reducing data on the server as much as practical, and only when client evaluation is acceptable. For asynchronous streaming, EF Core exposes `AsAsyncEnumerable()`.

## What `AsQueryable()` Does

`AsQueryable()` is often misunderstood as a way to send an in-memory query to a database. It is not.

If the source already implements `IQueryable<T>`, `AsQueryable()` returns it. Otherwise, it wraps the enumerable with an in-memory queryable provider, and query operations still execute over the local sequence.

```csharp
var values = new List<int> { 1, 2, 3, 4 };
IQueryable<int> query = values.AsQueryable();

var even = query.Where(value => value % 2 == 0).ToList();

Console.WriteLine(string.Join(", ", even)); // 2, 4
```

The expression-tree shape changes, but no database appears and no SQL is generated. Remote execution requires a provider designed for that remote system.

## Materialization and Result Shapes

Materializing establishes a boundary. Before it, provider-backed operations can be translated. After it, the application owns concrete results.

For read-only EF Core queries, combine server filtering, projection, and optional no-tracking behavior before materialization:

```csharp
var products = await dbContext.Products
    .AsNoTracking()
    .Where(product => product.InStock)
    .OrderBy(product => product.Name)
    .Select(product => new ProductSummary(
        product.Id,
        product.Name,
        product.Price))
    .Take(100)
    .ToListAsync(cancellationToken);
```

Projection avoids loading unused entity columns. `AsNoTracking` avoids change-tracking work when entities will not be updated. `Take` bounds the result; real APIs often use deterministic ordering and pagination.

Use `ToArray` when an array is the desired result representation, `ToList` for a mutable list, and read-only or immutable types when the contract should not expose mutation. The terminal operator should reflect how consumers use the result, not a habit.

## Should APIs Expose `IQueryable<T>`?

Returning `IQueryable<T>` gives callers powerful composition, but it also leaks provider behavior across a boundary. The caller can build expressions that fail translation, trigger expensive SQL, depend on a live `DbContext`, or expose data that the repository intended to constrain.

```csharp
public interface IProductQueries
{
    Task<IReadOnlyList<ProductSummary>> FindAvailableAsync(
        decimal maximumPrice,
        CancellationToken cancellationToken);
}
```

A query-specific method owns translation, authorization, includes, projections, and performance decisions. It is easier to test as an application boundary and returns a completed result.

Exposing `IQueryable<T>` can still be reasonable inside a cohesive data-access layer, for reusable query specifications, or in carefully controlled infrastructure code. Avoid sending it through controllers, remote service contracts, or layers that should not know which provider executes the query.

## Common Mistakes and Misconceptions

### “`IEnumerable<T>` means in-memory data”

It does not. It means the sequence is enumerable. What happens during enumeration depends on the source and on which operations were already composed.

### Calling `ToList` before filtering

With EF Core, this can pull unnecessary rows and columns into the process. Compose translatable filters and projections before materializing.

### Assuming every C# expression translates to SQL

An expression tree is input to a provider, not arbitrary executable C#. Translation support varies by provider. Test important queries against the real database provider.

### Using `AsEnumerable` as a performance fix

It opts subsequent operators into client-side LINQ; it does not make a query faster by itself. It may be appropriate for a small, already-filtered result when a required operation cannot translate.

### Believing `AsQueryable` makes a list remote

It wraps a local enumerable with a queryable interface. Only a remote provider can translate work to a remote system.

### Enumerating a query accidentally more than once

Logging, counting, and then looping can cause repeated work. Materialize once when reuse is intended, but keep database-side reduction before that point.

## Interview-Oriented Questions

### What is the main difference between `IEnumerable<T>` and `IQueryable<T>`?

`IEnumerable<T>` exposes enumeration and uses delegates for LINQ to Objects operators. `IQueryable<T>` also exposes an expression tree and provider so operations can be interpreted or translated.

### Does assigning an EF Core query to `IEnumerable<T>` execute it?

No. Assignment does not enumerate it. Subsequent `Enumerable` operators run in .NET when enumeration occurs, while the provider-backed expression already built before the boundary still executes through EF Core.

### What does `AsEnumerable()` do to an EF Core query?

It does not materialize. It changes subsequent composition to `Enumerable` operators, making that later work client-side when the sequence is enumerated.

### Why can early `ToListAsync()` hurt performance?

It fixes the database query at that point. Filters and projections added afterward operate on downloaded objects, potentially transferring and allocating much more data.

### Why might returning `IQueryable<T>` be undesirable?

It leaks query-provider and lifetime concerns, lets callers create unreviewed queries, and makes translation and performance behavior part of a wider architectural contract.

## Summary

`IEnumerable<T>` and `IQueryable<T>` describe different forms of composition, not simply “memory” versus “database”:

- `IEnumerable<T>` is an enumeration contract; added LINQ to Objects operators execute as .NET delegates.
- `IQueryable<T>` carries expressions and a provider that may translate them to SQL or another query language.
- Both commonly defer work until enumeration or a terminal operator.
- `AsEnumerable()` changes subsequent operators without materializing.
- `AsQueryable()` does not turn local data into a remote query.

With EF Core, compose filters, projections, ordering, and limits before materialization when they belong in the database. Keep `IQueryable<T>` within boundaries that understand the provider, and return intentional result shapes from higher-level APIs.
