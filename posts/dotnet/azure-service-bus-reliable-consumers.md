---
title: "Reliable Azure Service Bus Consumers in .NET: Locks, Settlement, and Idempotency"
excerpt: "Process Azure Service Bus messages safely with peek locks, explicit settlement, bounded renewal, dead-letter decisions, and durable idempotency."
category: ".NET"

seo:
  focusKeyword: "reliable Azure Service Bus consumer .NET"
  description: "Build reliable Azure Service Bus consumers in .NET with peek locks, message settlement, lock renewal, dead-lettering, and idempotent processing."
  socialTitle: "Reliable Azure Service Bus Consumers in .NET"
  socialDescription: "Handle locks, redelivery, retries, dead letters, and idempotent database effects without claiming impossible exactly-once execution."
---

# Reliable Azure Service Bus Consumers in .NET: Locks, Settlement, and Idempotency

A worker commits an invoice, loses its message lock before completion, and receives the same message again. Nothing is wrong with the broker: the consumer crossed two durable systems without one shared transaction.

> **Quick answer:** Use peek-lock delivery, complete only after the business transaction commits, and expect redelivery whenever settlement is uncertain. Renew locks only for bounded legitimate work, distinguish transient from permanent failures, and enforce idempotency in the database with a stable operation key.

## Peek Lock Trades Loss for Possible Duplication

In peek-lock mode, Service Bus locks a message for one receiver. Completion removes it. If the lock expires or the connection is interrupted before settlement, the message becomes available again.

Receive-and-delete removes the message as it is delivered. A consumer crash can therefore lose work. It is appropriate only when loss is acceptable and processing is trivial.

Microsoft's [message loss and duplicate guidance](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-message-loss-and-duplicates) describes peek lock as at-least-once delivery. Build the handler around that contract.

## Map Outcomes to Settlement

Settlement should describe what happened to the business operation:

| Outcome | Settlement |
| --- | --- |
| Database commit succeeded | Complete |
| Temporary dependency failure | Abandon or let the delivery retry |
| Invalid contract that cannot succeed | Dead-letter |
| Work intentionally postponed with a known sequence number | Defer |

```csharp
private async Task ProcessAsync(ProcessMessageEventArgs args)
{
    try
    {
        InvoiceRequestedV1 request =
            args.Message.Body.ToObjectFromJson<InvoiceRequestedV1>()
            ?? throw new InvalidMessageException("Body is missing.");

        await _handler.HandleAsync(
            args.Message.MessageId,
            request,
            args.CancellationToken);

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }
    catch (InvalidMessageException exception)
    {
        await args.DeadLetterMessageAsync(
            args.Message,
            deadLetterReason: "InvalidContract",
            deadLetterErrorDescription: exception.Message,
            cancellationToken: args.CancellationToken);
    }
    catch (TimeoutException)
    {
        await args.AbandonMessageAsync(args.Message, cancellationToken:
            args.CancellationToken);
    }
}
```

Do not put secrets, personal data, or full payloads in dead-letter descriptions. Those fields are operational metadata and are often broadly visible.

## Make the Database Effect Idempotent

A check followed by an insert is vulnerable to concurrent deliveries. Enforce the message identity with a unique database constraint in the same transaction as the business change.

```csharp
public async Task HandleAsync(
    string messageId,
    InvoiceRequestedV1 request,
    CancellationToken cancellationToken)
{
    await using var transaction = await _db.Database
        .BeginTransactionAsync(cancellationToken);

    bool alreadyHandled = await _db.ProcessedMessages
        .AnyAsync(x => x.MessageId == messageId, cancellationToken);

    if (alreadyHandled)
    {
        await transaction.CommitAsync(cancellationToken);
        return;
    }

    _db.Invoices.Add(Invoice.Create(request.InvoiceId, request.Amount));
    _db.ProcessedMessages.Add(new ProcessedMessage(messageId, DateTimeOffset.UtcNow));

    await _db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
}
```

The `ProcessedMessages.MessageId` column must be unique. Concurrent duplicates then converge on one durable result. [Idempotency in Distributed Systems](https://dev.jun-kim.net/2026/09/22/idempotency-in-distributed-systems-designing-safe-retryable-operations/) covers operation keys and transaction boundaries in more depth.

## Configure Lock Renewal to Match Work

`ServiceBusProcessor` can renew a lock while a handler is active:

```csharp
var options = new ServiceBusProcessorOptions
{
    AutoCompleteMessages = false,
    MaxConcurrentCalls = 8,
    PrefetchCount = 16,
    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
};
```

Renewal is not a substitute for bounded processing. A 45-minute handler is difficult to deploy, retry, and observe. Split long workflows into durable steps or store progress explicitly.

Prefetch also consumes locks before a handler starts. If prefetch is much larger than available concurrency, messages can wait locally until their locks are nearly expired. Tune both values with realistic processing duration.

## Use Delivery Count as Evidence, Not Logic

`DeliveryCount` helps diagnose repeated attempts, but it is not a stable business retry counter. Lock loss, abandon, and restarts can all increase it. Configure `MaxDeliveryCount` for the entity and let exhausted messages move to the dead-letter subqueue.

Read the DLQ explicitly:

```csharp
await using ServiceBusReceiver receiver = client.CreateReceiver(
    "invoice-requests",
    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

IReadOnlyList<ServiceBusReceivedMessage> messages =
    await receiver.ReceiveMessagesAsync(20, TimeSpan.FromSeconds(5));
```

The [Service Bus dead-letter documentation](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues) explains broker-generated reasons. A DLQ needs alerts, diagnosis, retention awareness, and controlled replay; it is not a retry policy by itself.

## Separate Send Deduplication from Consumer Idempotency

Duplicate detection records producer-controlled `MessageId` values for a configured window. It can protect a sender that retries after an uncertain send outcome. It does not protect the consumer from redelivery after processing.

Enable duplicate detection when the entity is created and choose the smallest window that covers plausible producer retries. Larger windows consume more broker resources. The feature is available in Standard and Premium tiers, not Basic.

Reliable processing does not mean a message is physically delivered once. It means every uncertain boundary can be retried without creating an incorrect business result.
