---
title: "RabbitMQ Retries and Dead-Letter Exchanges in .NET: Avoid Requeue Loops"
excerpt: "Build bounded RabbitMQ retry and dead-letter flows in .NET using negative acknowledgements, TTL queues, retry metadata, poison-message quarantine, and safe replay."
category: ".NET"

seo:
  focusKeyword: "RabbitMQ retry dead letter exchange .NET"
  description: "Design RabbitMQ retries and dead-letter exchanges in .NET with bounded attempts, TTL delay queues, poison-message handling, and controlled replay."
  socialTitle: "RabbitMQ Retries and Dead-Letter Exchanges in .NET"
  socialDescription: "Replace hot requeue loops with bounded delayed retries, explicit poison-message handling, and recoverable dead-letter operations."
---

# RabbitMQ Retries and Dead-Letter Exchanges in .NET: Avoid Requeue Loops

A consumer catches an exception and rejects with `requeue: true`. The same message returns immediately, fails again, and consumes an entire worker pool while the dependency remains unavailable.

> **Quick answer:** Requeue only when an immediate retry has a realistic chance of success. For delayed attempts, dead-letter into bounded retry queues with TTL, track attempts in controlled headers, and send exhausted or invalid messages to a parking-lot queue. Replay only after diagnosis and with rate limits.

## Separate Failure Classes

Classify failures before choosing a transport action:

| Failure | Response |
| --- | --- |
| Malformed or unsupported contract | Reject without requeue; quarantine |
| Temporary database or HTTP failure | Delayed bounded retry |
| Business rejection | Record outcome and acknowledge or quarantine according to the contract |
| Unknown invariant failure | Stop or quarantine and alert |

`BasicNackAsync(..., requeue: true)` returns a message to the same queue and can create a tight loop. It is not a delayed retry mechanism.

## Configure Dead-Letter Routing with Policies

RabbitMQ recommends policies for mutable dead-letter configuration where possible. Hard-coded queue arguments are difficult to change without deleting and recreating the queue.

A simple three-stage topology is:

```text
orders.work
    -> orders.retry.30s
    -> orders.work
    -> orders.parking
```

The work queue dead-letters transient failures to the retry exchange. The retry queue holds the message for 30 seconds, then dead-letters it back to the work exchange. Exhausted messages go to the parking-lot queue instead.

For a local example, queue arguments show the mechanism:

```csharp
await channel.ExchangeDeclareAsync("orders.work", ExchangeType.Direct, durable: true);
await channel.ExchangeDeclareAsync("orders.retry", ExchangeType.Direct, durable: true);
await channel.ExchangeDeclareAsync("orders.parking", ExchangeType.Direct, durable: true);

await channel.QueueDeclareAsync(
    queue: "orders.retry.30s",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: new Dictionary<string, object?>
    {
        ["x-message-ttl"] = 30_000,
        ["x-dead-letter-exchange"] = "orders.work",
        ["x-dead-letter-routing-key"] = "orders.process"
    });

await channel.QueueBindAsync(
    "orders.retry.30s",
    "orders.retry",
    "orders.retry.30s");
```

Use policies in production so operators can adjust dead-letter exchanges and limits without application redeployment.

## Republish with Explicit Attempt Metadata

When a transient attempt fails, publish a new retry message and acknowledge the original only after the retry publication is confirmed.

```csharp
int attempt = ReadAttempt(args.BasicProperties.Headers) + 1;

if (attempt <= 3)
{
    var retryProperties = new BasicProperties
    {
        MessageId = args.BasicProperties.MessageId,
        ContentType = args.BasicProperties.ContentType,
        Type = args.BasicProperties.Type,
        DeliveryMode = DeliveryModes.Persistent,
        Headers = new Dictionary<string, object?>
        {
            ["retry-attempt"] = attempt,
            ["original-routing-key"] = args.RoutingKey
        }
    };

    byte[] body = args.Body.ToArray();

    await publishChannel.BasicPublishAsync(
        "orders.retry",
        "orders.retry.30s",
        mandatory: true,
        retryProperties,
        body,
        cancellationToken);

    await consumeChannel.BasicAckAsync(args.DeliveryTag, false,
        cancellationToken);
}
else
{
    await consumeChannel.BasicRejectAsync(
        args.DeliveryTag,
        requeue: false,
        cancellationToken);
}
```

The publish channel should use publisher confirms. Otherwise acknowledging the original can lose the message if the retry publication never becomes durable.

Do not trust arbitrary incoming headers as unbounded counters or metric labels. Parse defensively and replace application-owned retry metadata.

## Understand Dead-Letter Metadata

RabbitMQ adds `x-death` information when messages are dead-lettered. It contains useful routing and reason history, but its representation is transport-specific and can become complex across multiple queues.

Use broker metadata for diagnosis and loop protection, while keeping the application retry contract explicit. Do not build core business state solely from `x-death` headers.

## Quarantine Poison Messages

A parking-lot queue should retain the original body and essential metadata, with restricted access and alerts. Operators need:

- message ID and contract type;
- original exchange and routing key;
- failure classification and attempt history;
- first and most recent failure times;
- a safe way to inspect without exposing secrets.

Set retention and queue-length limits deliberately. An unbounded parking queue can become a silent data store.

## Replay as a Controlled Deployment

Before replay:

1. fix or mitigate the failure;
2. validate the affected contract versions;
3. make consumers idempotent;
4. replay a small canary batch;
5. monitor dependency saturation and new failures;
6. increase the rate only within verified spare capacity.

Retries are part of failure handling, not a substitute for it. A bounded retry buys time for transient recovery; dead-lettering preserves evidence when automatic recovery is no longer safe.
