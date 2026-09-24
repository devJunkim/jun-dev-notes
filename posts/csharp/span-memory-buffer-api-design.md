---
title: "Span<T> and Memory<T> in C#: Safe Buffer APIs Without Hidden Copies"
excerpt: "Design C# buffer APIs with Span<T>, Memory<T>, explicit ownership, and lifetime rules that remain safe across synchronous and asynchronous work."
category: "C#"

seo:
  focusKeyword: "C# Span and Memory"
  description: "Use C# Span and Memory safely with slicing, parsing, async boundaries, pooling, ownership, and clear buffer lifetime contracts."
  socialTitle: "C# Span and Memory: Safe Buffer API Design"
  socialDescription: "Reduce avoidable copies without introducing use-after-return bugs, unclear ownership, or buffers that outlive their valid data."
---

# Span<T> and Memory<T> in C#: Safe Buffer APIs Without Hidden Copies

A parser receives a byte array, copies three fields into new arrays, converts them to strings, and then discards everything after one request. Replacing every array with a span may remove allocations, but it can also expose lifetime assumptions that the original copies accidentally hid.

Buffer performance starts with an ownership contract: who owns the storage, who may mutate it, and how long a view remains valid.

> **Quick answer:** Use `ReadOnlySpan<T>` for synchronous borrowed input, `Span<T>` for synchronous writable buffers, and `ReadOnlyMemory<T>` or `Memory<T>` when the data must cross an asynchronous boundary or be retained. Slice views instead of copying, but copy deliberately when the caller cannot guarantee the required lifetime.

## A Span Is a View, Not Storage

`Span<T>` and `ReadOnlySpan<T>` describe a contiguous region of memory. Slicing changes the view; it does not allocate a new backing buffer.

This parser reads a simple `key=value` line without creating substrings:

```csharp
public static bool TrySplitSetting(
    ReadOnlySpan<char> line,
    out ReadOnlySpan<char> key,
    out ReadOnlySpan<char> value)
{
    int separator = line.IndexOf('=');
    if (separator <= 0)
    {
        key = default;
        value = default;
        return false;
    }

    key = line[..separator].Trim();
    value = line[(separator + 1)..].Trim();
    return !key.IsEmpty;
}
```

The returned spans still point into `line`'s backing storage. They are valid only as long as that storage is valid and unchanged. The method borrows data; it does not own the parsed fields.

Microsoft's [`Memory<T>` and `Span<T>` usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines) recommend spans for synchronous APIs when possible and read-only variants when mutation is not part of the contract.

## Keep Span Work Synchronous and Local

Spans are ref-like values with compiler-enforced restrictions intended to prevent them from escaping valid storage. They are excellent for parsing, encoding, and transforming data inside one synchronous call.

```csharp
public static bool TryReadHeader(
    ReadOnlySpan<byte> buffer,
    out ushort payloadLength,
    out byte messageType)
{
    if (buffer.Length < 3)
    {
        payloadLength = 0;
        messageType = 0;
        return false;
    }

    payloadLength = (ushort)((buffer[0] << 8) | buffer[1]);
    messageType = buffer[2];
    return true;
}
```

Bounds checks establish that all three bytes exist. The example specifies big-endian length explicitly instead of relying on machine endianness.

Do not return a view over `stackalloc` storage or otherwise let a span outlive its source. The compiler prevents many escapes, but it cannot decide whether an array will be concurrently mutated or returned to a pool while another component still has a view.

## Use Memory Across Async Boundaries

An asynchronous method may suspend and resume later, so its buffer contract needs storage that can be retained safely. Use `ReadOnlyMemory<T>` for read-only input and obtain a span only while executing synchronous code between awaits:

```csharp
public static async Task WriteFrameAsync(
    Stream destination,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken)
{
    if (payload.Length > ushort.MaxValue)
    {
        throw new ArgumentOutOfRangeException(nameof(payload));
    }

    byte[] header =
    [
        (byte)(payload.Length >> 8),
        (byte)payload.Length,
    ];

    await destination.WriteAsync(header, cancellationToken);
    await destination.WriteAsync(payload, cancellationToken);
}
```

The caller must keep `payload`'s backing storage valid and stable until the returned task completes. Document that requirement. If the method queues work and returns before the write finishes, its contract is broken even though the parameter type is `ReadOnlyMemory<byte>`.

For tiny fixed headers, the array allocation may be acceptable. A hot protocol writer can use a pooled buffer or a reusable owner, but only after measurement and with explicit disposal.

## Ownership Matters More Than the Type Name

`ReadOnlyMemory<T>` prevents mutation through that particular view. It does not freeze the backing array. Another reference can still change the data while an async operation reads it.

When an API must retain an independent snapshot, copy at the boundary:

```csharp
public sealed class SigningRequest
{
    private readonly byte[] _payload;

    public SigningRequest(ReadOnlySpan<byte> payload)
    {
        _payload = payload.ToArray();
    }

    public ReadOnlyMemory<byte> Payload => _payload;
}
```

That allocation buys a clear lifetime and stable contents. “Zero-copy” is not automatically better when the alternative is shared mutable state.

For rented memory, express ownership with `IMemoryOwner<T>` or another disposable owner. The component accepting ownership must dispose it exactly once. A component merely borrowing `Memory<T>` must not return the underlying storage to a pool.

## Treat Pools as a Security Boundary

`ArrayPool<T>` reduces repeated large allocations, but a rented array may be larger than requested and may contain old data. Use only the requested slice and initialize bytes that will be observed.

```csharp
byte[] rented = ArrayPool<byte>.Shared.Rent(4096);
try
{
    Memory<byte> working = rented.AsMemory(0, 4096);
    int bytesRead = await source.ReadAsync(working, cancellationToken);
    await ProcessAsync(working[..bytesRead], cancellationToken);
}
finally
{
    CryptographicOperations.ZeroMemory(rented);
    ArrayPool<byte>.Shared.Return(rented);
}
```

Add the relevant `System.Buffers` and `System.Security.Cryptography` namespaces. Clearing is warranted when the buffer may contain secrets; it has a cost and should follow the data classification. Never use the memory after returning the array, and never return it while downstream async work is still active.

Prefer the pool's `clearArray` option or clear only the sensitive region according to the application's policy. The example clears the entire rented array because its actual length may exceed the requested size.

## Optimize With Evidence

Spans can remove substring and temporary-array allocations, but they also make APIs less convenient to store, mock, or use across async work. Start where profiling shows allocation or copying pressure. [Garbage Collection in .NET](https://dev.jun-kim.net/2026/09/11/garbage-collection-in-net-how-memory-management-really-works/) provides the broader allocation and collection model behind that measurement.

Benchmark the complete operation with realistic payloads. A parser that saves one allocation but forces every caller to copy the result may move cost rather than remove it. Track throughput, allocation rate, tail latency, and retained memory.

Tests should cover empty and truncated buffers, maximum lengths, overlapping slices, cancellation, mutation by another owner, and use of pooled memory under failures. The valuable result is not the presence of `Span<T>`; it is a buffer contract whose lifetime and ownership remain correct when the fast path fails.
