---
title: "Reliable RabbitMQ Messaging in .NET: Publisher Confirms and Consumer Acknowledgements"
excerpt: "Prevent silent RabbitMQ message loss with publisher confirms, mandatory routing, manual acknowledgements, durable queues, and idempotent .NET consumers."
category: ".NET"

seo:
  focusKeyword: "RabbitMQ publisher confirms .NET"
  description: "Build reliable RabbitMQ .NET publishers and consumers with publisher confirms, manual acknowledgements, durable queues, mandatory routing, and idempotency."
  socialTitle: "Reliable RabbitMQ Messaging with .NET"
  socialDescription: "Understand the separate producer and consumer acknowledgement boundaries that protect RabbitMQ workloads from silent loss."
---

# Reliable RabbitMQ Messaging in .NET: Publisher Confirms and Consumer Acknowledgements

A producer writes bytes to a socket and assumes the message is safe. A consumer acknowledges before its database commit. Both paths can lose work even when the queue is durable.

> **Quick answer:** Use durable or quorum queues with persistent messages, enable publisher confirms, detect unroutable mandatory publications, and acknowledge a delivery only after its required durable effect succeeds. Expect uncertain outcomes and redelivery, then make consumers idempotent.

## Producer and Consumer Acknowledgements Are Independent

Publisher confirms cover the path from publisher to broker. Consumer acknowledgements cover the path from broker to consumer. One does not imply the other.

The RabbitMQ [acknowledgements and confirms guide](https://www.rabbitmq.com/docs/confirms) treats them as separate data-safety mechanisms. A system needs both when it cannot afford silent loss at either boundary.

## Combine Queue Durability with Persistent Delivery

```csharp
await channel.QueueDeclareAsync(
    queue: "payments.capture",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: new Dictionary<string, object?>
    {
        ["x-queue-type"] = "quorum"
    },
    cancellationToken: cancellationToken);

var properties = new BasicProperties
{
    DeliveryMode = DeliveryModes.Persistent,
    MessageId = operationId,
    ContentType = "application/json",
    Type = "payments.capture.v1"
};
```

A persistent message sent to a durable queue is eligible for disk-backed survival. It is not proof that the broker accepted responsibility before the producer connection failed. Quorum queues add replicated safety, but applications should still wait for confirms.

## Enable Publisher Confirm Tracking

With RabbitMQ.Client 7, create a channel with publisher confirmations and tracking enabled:

```csharp
var channelOptions = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true);

await using IChannel publishChannel =
    await connection.CreateChannelAsync(channelOptions, cancellationToken);

await publishChannel.BasicPublishAsync(
    exchange: "payments.commands",
    routingKey: "payments.capture.v1",
    mandatory: true,
    basicProperties: properties,
    body: body,
    cancellationToken: cancellationToken);
```

When tracking is enabled, the async publish operation participates in confirm handling. A failure or negative acknowledgement must enter the application's retry or outbox path. Do not recursively republish forever inside an exception handler.

For sustained throughput, use bounded in-flight publications rather than waiting synchronously after every message or allowing unlimited outstanding work.

## Detect Routes That Match Nothing

Publisher confirms can still acknowledge a publication that routed nowhere. Use `mandatory: true` and handle `BasicReturnAsync`. Persist the failed operation or stop the publishing workflow; a log entry alone cannot recover the message.

Confirm tracking, mandatory returns, and an outbox solve different failure cases:

| Mechanism | Boundary |
| --- | --- |
| Publisher confirm | Broker accepted or rejected responsibility |
| Mandatory return | Publication did not route to a queue |
| Transactional outbox | Database commit reliably leads to eventual publication |

## Acknowledge Only After Business Success

```csharp
consumer.ReceivedAsync += async (_, args) =>
{
    PaymentCaptureV1 command =
        JsonSerializer.Deserialize<PaymentCaptureV1>(args.Body.Span)
        ?? throw new InvalidOperationException("Missing command body.");

    try
    {
        await handler.CaptureAsync(
            args.BasicProperties.MessageId,
            command,
            cancellationToken);

        await channel.BasicAckAsync(
            args.DeliveryTag,
            multiple: false,
            cancellationToken);
    }
    catch (PermanentPaymentException)
    {
        await channel.BasicRejectAsync(
            args.DeliveryTag,
            requeue: false,
            cancellationToken);
    }
    catch (Exception)
    {
        await channel.BasicNackAsync(
            args.DeliveryTag,
            multiple: false,
            requeue: true,
            cancellationToken);
    }
};
```

An unacknowledged delivery is requeued when its channel or connection closes. The next attempt may reach another consumer. A redelivery flag is useful diagnostic context, but it cannot prove whether the previous business transaction committed.

## Preserve Channel Ownership

Acknowledgements must use the same channel that delivered the message. Double acknowledgement or using an unknown delivery tag causes a channel error. Do not hand delivery tags to unrelated concurrent components.

Consumer callbacks may run concurrently depending on channel configuration. Keep mutable state isolated, and do not issue overlapping operations on a channel unless the client API explicitly supports that pattern.

## Idempotency Completes the Reliability Story

The worst boundary is commit-then-ack: the database commits, the acknowledgement is lost, and the command is delivered again. Store a stable message or business operation ID under a unique constraint in the same database transaction as the effect.

At-least-once delivery is not a defect to hide. It is the explicit contract that lets the system recover without silently losing work.
