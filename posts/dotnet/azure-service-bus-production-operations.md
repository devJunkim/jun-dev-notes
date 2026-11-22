---
title: "Operating Azure Service Bus with .NET: Concurrency, DLQ Recovery, and Observability"
excerpt: "Run Azure Service Bus .NET workloads in production with measured concurrency, controlled dead-letter recovery, identity boundaries, telemetry, and deployment-safe shutdown."
category: ".NET"

seo:
  focusKeyword: "Azure Service Bus .NET production"
  description: "Operate Azure Service Bus .NET producers and consumers with concurrency tuning, DLQ recovery, managed identity, observability, and safe deployment practices."
  socialTitle: "Azure Service Bus .NET Production Operations"
  socialDescription: "Tune processors, diagnose dead letters, secure identities, and build telemetry that explains queue health before incidents."
---

# Operating Azure Service Bus with .NET: Concurrency, DLQ Recovery, and Observability

A processor works in development, then production backlog grows while CPU remains low. Increasing concurrency clears the queue briefly but overloads the database and causes lock loss. Queue throughput is a system property, not one client setting.

> **Quick answer:** Tune concurrency and prefetch against measured handler time and downstream capacity, use separate identities for senders and receivers, expose messaging telemetry without leaking payloads, and treat dead-letter replay as a controlled operational workflow. Test shutdown and failure recovery under deployment conditions.

## Tune the Whole Processing Pipeline

`MaxConcurrentCalls` limits active handlers. `PrefetchCount` allows the client to lock messages before handlers are ready. Start conservatively:

```csharp
var options = new ServiceBusProcessorOptions
{
    AutoCompleteMessages = false,
    MaxConcurrentCalls = 8,
    PrefetchCount = 16,
    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
};
```

Measure message processing latency, lock loss, queue age, database saturation, and outbound dependency limits. More concurrent calls can reduce backlog only until another dependency becomes the bottleneck.

Large prefetch values are dangerous when processing is slow because the lock starts when the client receives the message, not when the handler begins useful work. Keep the prefetched working set within the lock and memory budget.

## Separate Application and Infrastructure Retries

The Azure SDK retries transient transport operations according to `ServiceBusRetryOptions`. Those retries do not replace business retry policy.

```csharp
var clientOptions = new ServiceBusClientOptions
{
    RetryOptions = new ServiceBusRetryOptions
    {
        Mode = ServiceBusRetryMode.Exponential,
        Delay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(10),
        MaxRetries = 5,
        TryTimeout = TimeSpan.FromSeconds(30)
    }
};
```

An SDK retry handles a transient AMQP operation. A message redelivery retries the business unit of work. An HTTP resilience policy protects one downstream call. Name and observe these layers separately so a single failure does not multiply into an uncontrolled retry storm.

## Make Dead-Letter Recovery a Procedure

Read DLQ messages without immediately completing them. Diagnose `DeadLetterReason`, contract version, delivery count, and the failed dependency. Fix or quarantine the cause before replay.

```csharp
await using ServiceBusReceiver dlq = client.CreateReceiver(
    "orders-to-fulfill",
    new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter,
        ReceiveMode = ServiceBusReceiveMode.PeekLock
    });

ServiceBusReceivedMessage? message =
    await dlq.ReceiveMessageAsync(TimeSpan.FromSeconds(5), cancellationToken);

if (message is not null)
{
    logger.LogWarning(
        "DLQ message {MessageId}: {Reason}",
        message.MessageId,
        message.DeadLetterReason);
}
```

Replay through a controlled tool that validates the message, assigns an audit record, rate-limits sends, and completes the DLQ copy only after the new send succeeds. A bulk replay can overwhelm the same dependency that caused the incident.

## Use Duplicate Detection and Transactions Precisely

Duplicate detection protects repeated sends with the same `MessageId` inside a configured window. It does not provide database exactly-once processing.

Service Bus transactions can group supported broker operations against compatible entities. They cannot atomically combine an arbitrary SQL transaction with a Service Bus send. When a database commit must reliably produce an event, use a transactional outbox and an idempotent relay.

Do not describe either feature as universal exactly-once execution. State the boundary it actually covers.

## Give Each Workload the Minimum Identity

Prefer managed identity with Azure RBAC:

- API: **Azure Service Bus Data Sender** for the required namespace or entity;
- worker: **Azure Service Bus Data Receiver**;
- deployment pipeline: management-plane permissions only where infrastructure changes are required;
- DLQ recovery tool: a separately governed identity.

Avoid namespace-wide owner credentials in application configuration. If a connection string is unavoidable, store it in a secret manager, rotate it, and never log it.

## Instrument the Messaging Boundary

Azure SDK operations emit diagnostic activities that can participate in distributed traces. Add application spans around business processing and use bounded attributes:

```csharp
using Activity? activity = ActivitySource.StartActivity("orders.process");
activity?.SetTag("messaging.system", "servicebus");
activity?.SetTag("messaging.destination.name", "orders-to-fulfill");
activity?.SetTag("messaging.operation", "process");
```

Useful metrics include:

- active and dead-letter message counts;
- age of oldest message;
- completed, abandoned, and failed processing totals;
- handler latency;
- lock-loss count;
- throttled requests and server errors.

Do not use message IDs, customer IDs, or payload fields as metric dimensions. [OpenTelemetry in .NET](https://dev.jun-kim.net/2026/09/26/opentelemetry-in-net-production-traces-metrics-and-correlated-logs/) explains cardinality and sensitive-data constraints.

## Test Deployment and Broker Failure

Automated tests should cover contract serialization and idempotent handlers without requiring the broker. Integration tests should use a dedicated namespace and verify send, settlement, redelivery, and DLQ behavior.

In a deployment environment, test:

- SIGTERM or container stop during an active handler;
- lock expiry during slow work;
- transient network interruption;
- exhausted downstream connection pools;
- DLQ alerts and a throttled replay;
- credential removal and RBAC propagation failure.

Production readiness is demonstrated by recovery behavior, not by a successful send in a console application.
