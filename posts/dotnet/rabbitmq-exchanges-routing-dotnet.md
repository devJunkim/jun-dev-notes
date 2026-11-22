---
title: "RabbitMQ Exchanges and Routing in .NET: Direct, Topic, and Fanout Patterns"
excerpt: "Route .NET messages through RabbitMQ direct, topic, and fanout exchanges with deliberate bindings, mandatory publishing, and controlled topology ownership."
category: ".NET"

seo:
  focusKeyword: "RabbitMQ exchanges routing .NET"
  description: "Use RabbitMQ direct, topic, and fanout exchanges in .NET with explicit routing keys, queue bindings, mandatory publishing, and safe topology management."
  socialTitle: "RabbitMQ Exchanges and Routing with .NET"
  socialDescription: "Build practical command, event, and broadcast routing without coupling publishers to consumer queue names."
---

# RabbitMQ Exchanges and Routing in .NET: Direct, Topic, and Fanout Patterns

A publisher sends directly to `billing-worker-3`. Renaming or scaling the consumer now changes producer code. Exchanges let publishers describe routing intent while queues remain consumer-owned delivery endpoints.

> **Quick answer:** Use a direct exchange for exact command routes, a topic exchange for structured event patterns, and fanout only when every bound queue should receive every publication. Declare durable topology consistently, publish with `mandatory: true` when unroutable messages must be detected, and treat routing keys as versioned contracts.

## Model Exchanges and Queues Separately

An exchange routes a publication. A queue stores deliveries. A binding connects them using exchange-specific rules.

```csharp
const string exchange = "orders.events";
const string billingQueue = "billing.orders";

await channel.ExchangeDeclareAsync(
    exchange,
    ExchangeType.Topic,
    durable: true,
    autoDelete: false,
    arguments: null,
    cancellationToken);

await channel.QueueDeclareAsync(
    billingQueue,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null,
    cancellationToken);

await channel.QueueBindAsync(
    billingQueue,
    exchange,
    routingKey: "orders.accepted.v1",
    arguments: null,
    cancellationToken);
```

Applications may declare expected topology at startup, but production changes should still be reviewed and deployed deliberately. Incompatible redeclarations close the channel.

## Use Direct Exchanges for Exact Routes

A direct exchange matches the routing key exactly. It is suitable for commands such as:

- `payments.capture.v1`;
- `inventory.reserve.v1`;
- `email.send.v2`.

Commands normally have one logical owner, though multiple consumer instances can compete on that owner's queue.

Avoid embedding instance IDs or environment-specific queue names in producer code. The routing key expresses the operation; the binding selects the queue.

## Use Topic Exchanges for Event Families

Topic exchanges match dot-separated patterns:

- `orders.*.v1` matches one segment such as `orders.accepted.v1`;
- `orders.#` matches all order event descendants;
- `*.failed.v1` matches v1 failure events from one top-level domain.

```csharp
await channel.QueueBindAsync(
    queue: "audit.business-events",
    exchange: "domain.events",
    routingKey: "orders.#",
    arguments: null,
    cancellationToken: cancellationToken);
```

Keep routing keys stable and documented. A broad `#` binding is convenient for audit or temporary migration, but it can create unexpected volume and expose events to consumers that were never designed for them.

## Use Fanout Only for True Broadcasts

A fanout exchange ignores routing keys and copies every publication to every bound queue. It works for cache invalidation or broad notifications when all subscribers genuinely need the same stream.

Fanout is not “send to every worker instance.” Multiple instances consuming one queue compete for each message. To give each application its own copy, bind one queue per application.

## Detect Unroutable Publications

Publishing to an exchange can succeed at the protocol level even when no queue matches. Set `mandatory: true` and handle returned messages:

```csharp
channel.BasicReturnAsync += (_, args) =>
{
    logger.LogError(
        "Unroutable message {MessageId} using {Exchange}/{RoutingKey}: {ReplyText}",
        args.BasicProperties.MessageId,
        args.Exchange,
        args.RoutingKey,
        args.ReplyText);

    return Task.CompletedTask;
};

await channel.BasicPublishAsync(
    exchange: "domain.events",
    routingKey: "orders.accepted.v1",
    mandatory: true,
    basicProperties: properties,
    body: body,
    cancellationToken: cancellationToken);
```

A returned message must enter a durable recovery path. Logging and discarding it is still message loss. Publisher confirms and mandatory returns answer different questions: a confirm says the broker accepted the publication; a return says it did not route to a queue.

## Version Routing and Payload Together

A routing key version helps consumers bind deliberately, but it does not replace a compatible payload contract. During migration:

1. add the new binding and compatible consumer;
2. publish the new version according to a documented transition;
3. observe unroutable returns and queue traffic;
4. remove the old binding after consumers have migrated.

Do not use routing patterns as an authorization boundary. RabbitMQ permissions control configure, write, and read access by resource pattern; applications must still validate incoming contracts.

Exchanges decouple publishers from physical queues only when routing keys, bindings, and ownership remain intentional. An undocumented wildcard topology simply moves coupling into the broker.
