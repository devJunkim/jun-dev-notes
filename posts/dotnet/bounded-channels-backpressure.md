---
title: ".NET Channels: Bounded Queues, Backpressure, and Graceful Shutdown"
excerpt: "Build bounded in-process pipelines with System.Threading.Channels, explicit overload behavior, coordinated completion, and observable failure handling."
category: ".NET"

seo:
  focusKeyword: ".NET Channels backpressure"
  description: "Use bounded .NET Channels for in-process pipelines with backpressure, deliberate overload policies, cancellation, and graceful shutdown."
  socialTitle: ".NET Channels: Backpressure and Graceful Shutdown"
  socialDescription: "Design bounded in-process queues that behave predictably during overload, failure, cancellation, and application shutdown."
---

# .NET Channels: Bounded Queues, Backpressure, and Graceful Shutdown

An in-memory queue can decouple request handling from background work, but an unbounded queue merely moves overload into process memory. Production behavior depends on what happens when producers are faster than consumers and how queued work is treated during shutdown.

> **Quick answer:** Use a bounded `Channel<T>` with a capacity derived from an explicit memory and latency budget. Choose whether a full queue waits, rejects, or drops work; complete the writer when no more items will arrive; and let consumers drain with `ReadAllAsync`. Use durable infrastructure instead when work must survive process loss.

## Capacity Is an Operational Policy

`Channel.CreateBounded<T>` limits how many items can wait. With `BoundedChannelFullMode.Wait`, `WriteAsync` waits asynchronously for capacity, applying backpressure to producers.

```csharp
using System.Threading.Channels;

public sealed record ThumbnailJob(Guid ImageId, string ObjectKey);

public sealed class ThumbnailQueue
{
    private readonly Channel<ThumbnailJob> _channel;

    public ThumbnailQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _channel = Channel.CreateBounded<ThumbnailJob>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false,
            });
    }

    public ValueTask EnqueueAsync(
        ThumbnailJob job,
        CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<ThumbnailJob> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public bool TryComplete(Exception? error = null) =>
        _channel.Writer.TryComplete(error);
}
```

Microsoft's [.NET Channels guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) documents the bounded and unbounded creation options and each full-mode behavior. A capacity of 500 jobs is not meaningful by itself: estimate retained bytes per item, acceptable queueing delay, expected burst size, and consumer throughput.

The single-reader and single-writer flags are optimization promises. Set them only when the topology guarantees them; incorrect promises can invalidate concurrency assumptions.

## Decide What Full Means

The full mode changes correctness:

| Mode | Behavior when full | Appropriate only when |
| --- | --- | --- |
| `Wait` | Producer waits for space | Backpressure can safely reach the caller |
| `DropWrite` | Incoming item is discarded | Loss is acceptable and observable |
| `DropOldest` | Oldest queued item is discarded | Freshness matters more than completeness |
| `DropNewest` | Newest queued item is discarded | Replacing the latest queued value is meaningful |

For business commands such as billing or order placement, silent dropping is usually incorrect. Waiting also needs a deadline; otherwise saturated producers can consume request slots indefinitely. A web endpoint can combine `WriteAsync` with its request token and translate queue saturation into a documented response.

If a drop mode is intentional, count dropped items and expose the policy to callers. The `CreateBounded` overload with an item-dropped callback can make loss observable, but metrics do not make lossy processing durable.

## Consume Until Completion

A worker can drain the channel using `ReadAllAsync`:

```csharp
public sealed class ThumbnailWorker(
    ThumbnailQueue queue,
    IThumbnailGenerator generator,
    ILogger<ThumbnailWorker> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (ThumbnailJob job in queue.ReadAllAsync(cancellationToken))
        {
            try
            {
                await generator.GenerateAsync(job, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Thumbnail job {ImageId} failed", job.ImageId);
            }
        }
    }
}
```

This example isolates a failed item and continues. That is a product decision, not a universal default. Some pipelines must stop on corruption, retry transient failures with a bounded policy, or move failed work to durable storage. Avoid an infinite immediate retry loop inside the consumer.

Multiple consumers increase concurrency but may reorder completion. Preserve a single reader when order matters, or partition work by a stable key when each entity needs ordering but the pipeline still needs parallelism.

## Coordinate Completion and Shutdown

Completion and cancellation answer different questions. Completing the writer says no more items will arrive and allows readers to drain buffered work. Cancelling the consumer says stop waiting or processing now.

A graceful owner should generally:

1. Stop accepting new work.
2. Call `TryComplete` on the writer.
3. Allow consumers to drain within the shutdown budget.
4. Cancel remaining work when that budget expires.
5. Record how many items were abandoned or failed.

If a producer faults, pass the exception to `TryComplete(error)`. Readers observe the failure after buffered items are consumed. Ensure one component owns completion; competing producers should not each close a shared channel independently.

The channel is process memory. A deployment, crash, recycle, or machine failure can discard accepted items. If acceptance creates a promise to the user, use a durable broker or transactional outbox rather than acknowledging an in-memory enqueue. [Transactional Outbox Pattern in .NET: Reliable Event Delivery](https://dev.jun-kim.net/2026/09/17/transactional-outbox-pattern-in-net-reliable-event-delivery/) covers coordinating database state with durable publication.

## Instrument the Pipeline Without Guessing

Track enqueue waits, processing duration, failures, drops, and an approximate queue depth. Alert on sustained saturation rather than one short burst. Avoid high-cardinality metric labels such as job IDs or object keys.

Do not expose payloads in logs merely because a job failed. Log a safe identifier and correlation context, and protect any object key or tenant data according to its classification.

Load tests should verify the configured overload policy, not just peak throughput. Exercise producer cancellation, consumer failure, completion with buffered work, shutdown timeout, and multiple-reader ordering. A bounded queue is useful because it forces those choices into the design instead of leaving memory exhaustion as the implicit policy.
