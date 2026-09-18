---
title: "Transactional Outbox Pattern in .NET: Reliable Event Delivery"
excerpt: "Use the transactional outbox pattern in .NET to close the database-and-message-broker failure gap without pretending delivery is exactly once."
category: "Architecture"

seo:
  focusKeyword: "transactional outbox pattern in .NET"
  description: "Implement the transactional outbox pattern in .NET with atomic writes, a relay worker, idempotent consumers, ordering, and cleanup trade-offs."
  socialTitle: "Transactional Outbox Pattern in .NET"
  socialDescription: "Close the dual-write failure gap while keeping duplicate delivery, ordering, and operations explicit."
---

# Transactional Outbox Pattern in .NET: Reliable Event Delivery

An order can commit successfully and still fail to publish its `OrderPlaced` message. Publishing first only reverses the problem: consumers may react to an order that later rolls back.

The transactional outbox closes that dual-write gap by storing the business change and an outgoing message in the same local database transaction. A separate relay publishes stored messages later.

The [transactional outbox pattern](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos) is not specific to one broker or database product; the storage and relay mechanism can vary while the atomic local-write principle remains the same.

> **Quick answer:** Write an outbox record in the same transaction as the aggregate change, then let a background relay publish it. This guarantees that committed business state has a durable message to send; it does not guarantee exactly-once delivery, global ordering, or that consumers process the message only once.

## The Failure Window Is the Design Problem

This apparently simple sequence crosses two independent systems:

```text
1. Save order to database
2. Publish OrderPlaced to broker
```

If the process crashes between steps, the order exists without its message. Reversing the steps can publish a message for data that never commits. A distributed transaction might coordinate both systems, but many brokers and cloud services do not participate in the same transaction protocol, and operational support can be undesirable even when available.

The outbox changes the sequence:

```text
Database transaction:
  1. Save order
  2. Insert outbox row
  3. Commit

Later:
  4. Relay reads outbox row
  5. Relay publishes message
  6. Relay marks row as dispatched
```

Steps one through three are atomic because they use one transactional store. Publishing becomes recoverable work.

## Store an Integration Contract, Not an Entity

A practical outbox row needs an immutable identifier, message type, schema version, occurrence time, payload, and processing state. The payload should represent the integration contract rather than a serialized EF Core entity with navigation properties and persistence details.

```csharp
using System.Text.Json;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string Type { get; init; }
    public int Version { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public static class OutboxFactory
{
    public static OutboxMessage Create<T>(
        T message,
        string type,
        int version,
        TimeProvider timeProvider) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAtUtc = timeProvider.GetUtcNow(),
        Type = type,
        Version = version,
        Payload = JsonSerializer.Serialize(message),
    };
}
```

Do not put secrets into the payload merely because the outbox table is internal. It will be copied to logs, backups, broker storage, and consumer systems unless each boundary prevents that.

## Commit State and Message Together

The application use case changes the aggregate and adds the integration event before a single `SaveChangesAsync` call:

```csharp
public async Task PlaceAsync(
    PlaceOrder command,
    CancellationToken cancellationToken)
{
    var order = Order.Place(command.CustomerId, command.Lines);
    db.Orders.Add(order);

    var integrationEvent = new OrderPlacedV1(
        order.Id,
        order.CustomerId,
        order.Total);

    db.OutboxMessages.Add(OutboxFactory.Create(
        integrationEvent,
        "orders.order-placed",
        version: 1,
        timeProvider));

    await db.SaveChangesAsync(cancellationToken);
}
```

EF Core wraps a normal `SaveChanges` call in a transaction when the provider supports transactions. If the workflow uses several saves or additional database work, define the transaction boundary explicitly. The important property is that the aggregate and outbox row cannot commit independently.

This often appears beside [CQRS in .NET](https://dev.jun-kim.net/2026/09/11/cqrs-in-net-when-command-query-responsibility-segregation-makes-sense/), but neither pattern requires the other. A domain event records a business fact inside the model; an integration event is a stable contract for other processes. They may be related, but publishing every internal domain event directly exposes implementation details.

## The Relay Is At-Least-Once

The relay claims a small batch, publishes each message, and marks successful rows as dispatched. The unavoidable failure window is after the broker accepts a message but before the database records success. A restart publishes that row again.

Therefore the honest delivery contract is at-least-once. Give every message a stable ID and require consumers to make effects idempotent. A consumer can record processed message IDs in the same local transaction as its business effect, use a naturally idempotent operation, or enforce a unique business key.

Exactly-once wording is usually hiding a boundary. A broker may deduplicate within a window, but it cannot generally make an arbitrary consumer's database update and its acknowledgement one atomic action.

## Claim Rows Without Double-Processing

Multiple relay instances are desirable for availability, but they must coordinate claims. Common strategies include database-specific row locking with skip-locked semantics, an atomic status update with a lease owner and expiry, or change-data capture that streams inserts.

Each choice has costs:

| Strategy | Advantage | Cost |
| --- | --- | --- |
| Poll and lock rows | Simple and stays in the application database | Database-specific locking and polling load |
| Lease columns | Recoverable claims with visible ownership | More state and careful lease renewal |
| Change-data capture | Low-latency stream without application polling | Additional platform and operational complexity |

Do not hold a database transaction open while waiting on the broker if that creates long locks. Claim work briefly, publish outside the claim transaction, and use an expiring lease or equivalent recovery mechanism.

## Ordering Is Scoped, Not Global

Timestamps do not provide a trustworthy global order across concurrent transactions. An auto-increment key describes insertion order in one database, not necessarily aggregate commit order or broker consumption order.

If messages for one order must remain ordered, include an aggregate ID and monotonically increasing aggregate version, then use a broker partition or session keyed by that ID. Consumers should detect gaps or stale versions according to business needs. Global ordering usually sacrifices throughput and still does not solve duplicate effects.

## Operate the Outbox as a Queue

An outbox needs backlog and age metrics, attempt counts, alert thresholds, retention, and a way to inspect or quarantine poison messages. Delete or archive dispatched rows in bounded batches so cleanup does not create long locks or uncontrolled table growth.

Schema evolution also needs a policy. Include a message version, keep old consumers compatible during rollout, and avoid rewriting undispatched payloads casually. A serializer or type name tied directly to a CLR assembly makes deployments brittle.

The relay can be a `.NET` hosted worker, a separate process, or platform-specific change-data-capture infrastructure. The right choice depends on throughput and operational ownership, not on pattern purity.

## When the Pattern Is Worth It

Use an outbox when a committed local change must reliably cause work in another process and the systems cannot share one appropriate transaction. Skip it when the action is purely local, occasional loss is acceptable, or a platform service already provides the required atomic boundary.

The pattern buys recoverability at the cost of delayed delivery, duplicate handling, storage, cleanup, and another component to observe. It is valuable precisely when those explicit costs are better than an invisible dual-write failure window.
