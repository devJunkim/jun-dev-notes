---
title: "Azure Service Bus Sessions in .NET: Ordered Processing Without Global Serialization"
excerpt: "Preserve per-aggregate message order with Azure Service Bus sessions in .NET while handling session locks, state, concurrency, and hot-key limits."
category: ".NET"

seo:
  focusKeyword: "Azure Service Bus sessions .NET"
  description: "Use Azure Service Bus sessions in .NET for per-key FIFO processing with ServiceBusSessionProcessor, session locks, state, and scalable concurrency."
  socialTitle: "Ordered Azure Service Bus Processing with .NET Sessions"
  socialDescription: "Keep events ordered per customer or order without reducing the entire queue to one consumer."
---

# Azure Service Bus Sessions in .NET: Ordered Processing Without Global Serialization

An order is cancelled and then shipped because two workers process related messages concurrently. Adding one global consumer would preserve order, but it would also serialize every customer and destroy throughput.

> **Quick answer:** Enable sessions on the queue or subscription, set `SessionId` to the business key whose messages must be ordered, and consume with `ServiceBusSessionProcessor`. Treat the session lock as exclusive ownership of one ordered stream, keep handlers bounded, and watch for hot session IDs that concentrate work on one processor.

## Define the Ordering Boundary First

Service Bus sessions provide FIFO processing within one session, not across an entire entity. Choose a session key that matches the business invariant:

- `order:{orderId}` when transitions for one order must be ordered;
- `account:{accountId}` when balance mutations must be serialized;
- `workflow:{workflowId}` for a multi-step orchestration.

Using `customerId` for a high-volume tenant can create one hot session. Using a random message ID creates perfect distribution but no useful ordering.

Sessions must be enabled when the queue or subscription is created. Senders and receivers must then use session-aware APIs.

## Send Related Messages with One Session ID

```csharp
public sealed record OrderTransitionV1(
    Guid EventId,
    Guid OrderId,
    string Transition,
    long Version,
    DateTimeOffset OccurredAt);

public static ServiceBusMessage ToMessage(OrderTransitionV1 value)
{
    return new ServiceBusMessage(BinaryData.FromObjectAsJson(value))
    {
        MessageId = value.EventId.ToString(),
        SessionId = $"order:{value.OrderId}",
        Subject = "orders.transition.v1",
        ContentType = "application/json"
    };
}
```

Send messages for one session in the intended order. Sessions cannot reconstruct order if multiple producers race without a shared versioning or sequencing rule.

Include an aggregate version in the contract. The consumer can reject or defer an impossible transition rather than trusting arrival order as the only correctness check.

## Use a Session Processor

```csharp
ServiceBusSessionProcessor processor = client.CreateSessionProcessor(
    "order-transitions",
    new ServiceBusSessionProcessorOptions
    {
        AutoCompleteMessages = false,
        MaxConcurrentSessions = 8,
        MaxConcurrentCallsPerSession = 1,
        MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        SessionIdleTimeout = TimeSpan.FromSeconds(20)
    });

processor.ProcessMessageAsync += async args =>
{
    OrderTransitionV1 transition =
        args.Message.Body.ToObjectFromJson<OrderTransitionV1>()
        ?? throw new InvalidOperationException("Missing transition body.");

    await handler.ApplyAsync(transition, args.CancellationToken);
    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
};

processor.ProcessErrorAsync += args =>
{
    logger.LogError(args.Exception,
        "Session processor failed for {EntityPath} from {ErrorSource}",
        args.EntityPath,
        args.ErrorSource);
    return Task.CompletedTask;
};
```

`MaxConcurrentCallsPerSession = 1` is the clearest ordered configuration. Increasing it allows concurrent handling within a session and can defeat the reason sessions were selected.

`MaxConcurrentSessions` controls parallelism across keys. Tune it against database capacity and handler duration rather than setting it to the largest possible value.

## Understand Session Locks

Accepting a session locks that session to one receiver. Other receivers cannot process its messages until the lock is released or expires. The processor renews locks while work is active, subject to `MaxAutoLockRenewalDuration`.

A lock loss can still lead to redelivery. Database operations remain idempotent, and an aggregate version or unique event ID should prevent repeated state transitions.

Keep one handler invocation shorter than the lock-renewal budget. A session should not become an in-memory workflow that must stay attached to one process for hours.

## Use Session State Sparingly

Session state stores a small opaque value associated with the session. It can hold a cursor or compact workflow marker:

```csharp
BinaryData? state = await args.GetSessionStateAsync(args.CancellationToken);
long lastVersion = state is null ? 0 : state.ToObjectFromJson<long>();

if (transition.Version == lastVersion + 1)
{
    await handler.ApplyAsync(transition, args.CancellationToken);
    await args.SetSessionStateAsync(
        BinaryData.FromObjectAsJson(transition.Version),
        args.CancellationToken);
}
```

Do not treat session state as the authoritative business database. It consumes broker storage, has operational limits, and is coupled to the messaging entity. Persist durable workflow state in a database when it must be queried, audited, or recovered independently.

## Detect Hot and Stuck Sessions

Sessions shift the unit of scalability from message to key. Monitor:

- active sessions and session acceptance latency;
- message age and backlog by business key where observable;
- lock loss and renewal failures;
- handler duration and database contention;
- sessions repeatedly producing poison messages.

One poison message can block later messages in the same ordered stream. Decide whether to dead-letter it and continue, stop the session for investigation, or compensate at the business layer. The correct choice depends on whether skipping a transition would violate the aggregate.

Sessions are useful when ordering is a real invariant. They are costly ceremony when consumers can safely process independent, idempotent events in any order.
