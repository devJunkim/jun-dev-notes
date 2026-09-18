---
title: "C# Async Streams: Cancellation, Disposal, and API Design"
excerpt: "Design and consume IAsyncEnumerable streams in C# with correct cancellation, disposal, paging, and failure semantics."
category: "C#"

seo:
  focusKeyword: "C# async streams"
  description: "Use C# async streams with IAsyncEnumerable, await foreach, cancellation, disposal, paging, and deliberate buffering boundaries."
  socialTitle: "C# Async Streams: Cancellation and API Design"
  socialDescription: "Build async streams that cancel promptly, release resources, and expose honest paging and failure behavior."
---

# C# Async Streams: Cancellation, Disposal, and API Design

Returning `Task<List<T>>` makes a caller wait for the entire result. Returning `IAsyncEnumerable<T>` lets the producer deliver values over time while keeping asynchronous I/O out of a synchronous iterator.

That convenience also creates a longer-lived operation. Cancellation, resource ownership, partial results, and repeated enumeration become part of the API contract.

> **Quick answer:** Use `IAsyncEnumerable<T>` when values naturally arrive across asynchronous boundaries and incremental processing is useful. Accept consumer cancellation with `[EnumeratorCancellation]`, acquire resources inside the iterator, and document whether a failure can occur after some values have already been yielded.

## An Async Stream Is a Pull-Based Conversation

`await foreach` repeatedly asks an async enumerator for its next value. The producer can await between values, and the consumer applies natural backpressure by requesting the next value only after finishing the current loop iteration.

Microsoft's [async streams guidance](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream) shows the compiler protocol behind `await foreach`, including cancellation and asynchronous disposal.

This is not parallel processing. A normal `await foreach` handles one element at a time. If each element needs concurrent work, add bounded concurrency explicitly rather than assuming the stream provides it.

Async streams fit paged APIs, database cursors, and event sources that end. They are less useful when the caller needs an all-or-nothing result, a total count before processing, or repeated random access. In those cases, a materialized collection may communicate the contract better.

The distinction complements [LINQ Performance in C#: Deferred Execution, Multiple Enumeration, and Common Pitfalls](https://dev.jun-kim.net/2026/09/15/linq-performance-in-c-deferred-execution-multiple-enumeration-and-common-pitfalls/): both APIs can be deferred, but an async stream allows each move to wait without blocking a thread.

## Forward Consumer Cancellation Correctly

The iterator below reads a paged source and yields each item as soon as its page arrives:

```csharp
using System.Runtime.CompilerServices;

public sealed record Product(int Id, string Name);
public sealed record ProductPage(
    IReadOnlyList<Product> Items,
    string? NextCursor);

public interface IProductPageSource
{
    Task<ProductPage> ReadAsync(
        string? cursor,
        CancellationToken cancellationToken);
}

public static class ProductStreams
{
    public static async IAsyncEnumerable<Product> ReadAllAsync(
        IProductPageSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            ProductPage page = await source.ReadAsync(cursor, cancellationToken);

            foreach (Product product in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return product;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }
}
```

`[EnumeratorCancellation]` tells the compiler to combine the method argument with the token supplied when enumeration begins. It matters for this common call:

```csharp
await foreach (Product product in ProductStreams
    .ReadAllAsync(source)
    .WithCancellation(cancellationToken))
{
    await index.WriteAsync(product, cancellationToken);
}
```

Without the attribute, a token passed only through `WithCancellation` may not reach the iterator body. Alternatively, callers can pass the token directly to `ReadAllAsync(source, cancellationToken)`. Supporting both styles makes a reusable stream behave like other cancellable async APIs.

Cancellation remains cooperative. The iterator must pass the token to I/O and avoid long CPU-bound work that never checks it.

## Resource Lifetime Follows Enumeration

Code before the first `yield return` does not run when the stream object is created. It runs when enumeration starts. A `using` inside the iterator therefore remains active across yields and is disposed when enumeration completes, fails, or the consumer leaves the loop early.

```csharp
public static async IAsyncEnumerable<string> ReadLinesAsync(
    Stream stream,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    using var reader = new StreamReader(stream, leaveOpen: true);

    while (await reader.ReadLineAsync(cancellationToken) is { } line)
    {
        yield return line;
    }
}
```

The compiler-generated `await foreach` cleanup disposes the async enumerator. That cleanup is why an early `break` still exits the iterator's `using` scope. Here the caller retains ownership of the stream because `leaveOpen` is true; changing that option changes the public ownership contract.

Do not create a database context outside an iterator and return a stream that depends on it after the context's scope has ended. Acquire the context during enumeration, or keep the operation inside an application method whose scope clearly outlives consumption.

## Partial Results Change Failure Semantics

A `Task<IReadOnlyList<Product>>` either returns the list or faults before exposing it. An async stream can yield 80 products and then fail on page five. The consumer has already observed side effects if it wrote those products elsewhere.

That is valuable for streaming, but it rules out pretending the operation is atomic. Consumers should make per-item effects idempotent, checkpoint progress, or buffer until a commit boundary when partial processing is unacceptable.

Avoid catching every exception inside the iterator and silently ending the sequence. End-of-stream and failed-stream mean different things. Translate only failures the abstraction can interpret, and preserve cancellation as cancellation rather than converting it into an empty result.

## Enumeration May Repeat the Work

Every enumeration invokes the iterator again. For a paged HTTP source, two loops usually mean two sets of remote requests:

```csharp
IAsyncEnumerable<Product> products = ProductStreams.ReadAllAsync(source);

await foreach (Product product in products) { /* first traversal */ }
await foreach (Product product in products) { /* remote work repeats */ }
```

If the result must be reused, materialize it once at an intentional boundary. If the source is single-use, say so and consider exposing an operation rather than a casually reusable property.

Streaming also does not guarantee constant memory. Operators or consumers that sort, group, cache, or collect the stream still buffer data. The API controls when values become available, not what every downstream operation does with them.

## Keep Concurrency Bounded and Separate

Calling an async operation in the loop is sequential and often correct. Starting a task per item can overwhelm a database or API and retain every task until completion. When concurrency is required, use a bounded channel, a small worker pool, or another mechanism with an explicit limit and failure policy.

The stream should normally remain responsible for producing values. Retry, ordering, concurrency, and checkpoint decisions belong at a layer that understands the side effect. Combining all of them inside one iterator makes cancellation and partial failure much harder to reason about.

## Practical Review Checklist

- Does incremental delivery improve the caller's behavior, or would a collection be simpler?
- Can a consumer-provided token reach every asynchronous wait?
- Who owns streams, readers, contexts, and responses during enumeration?
- What happens after some values are yielded and a later page fails?
- Is repeated enumeration safe, expensive, or unsupported?
- Does any downstream operation quietly buffer the entire sequence?

Async streams are most useful when their lifetime semantics are explicit. The syntax is small; the production design lies in cancellation, cleanup, and what partial progress means.
