---
title: "Azure Service Bus Topics and Subscriptions with .NET: Practical Event Routing"
excerpt: "Route one .NET integration event to independent Azure Service Bus subscribers using explicit contracts, subscription filters, and safely managed rules."
category: ".NET"

seo:
  focusKeyword: "Azure Service Bus topics subscriptions .NET"
  description: "Use Azure Service Bus topics, subscriptions, and filters from .NET to build durable fan-out with versioned events and independently operated consumers."
  socialTitle: "Azure Service Bus Topics and Subscriptions with .NET"
  socialDescription: "Build practical pub-sub routing for billing, notifications, and audit consumers without coupling one event to one worker."
---

# Azure Service Bus Topics and Subscriptions with .NET: Practical Event Routing

An order service sends the same event separately to billing, notifications, and audit queues. Adding a fourth consumer now requires changing the producer. A topic moves that fan-out decision into messaging topology while each subscription keeps its own durable backlog.

> **Quick answer:** Publish a stable integration event once to a topic and give each independently operated consumer its own subscription. Use application properties for deliberate filtering, remove the default rule when a subscription should not receive everything, and deploy rules as versioned infrastructure rather than mutating them from every worker.

## A Subscription Is a Durable Virtual Queue

A Service Bus topic accepts messages. Each matching subscription receives its own copy and settles it independently. A slow notification consumer does not stop billing, though its subscription backlog can grow.

Use a queue when one pool of competing consumers owns the work. Use a topic when several business capabilities need independent delivery. The [queues, topics, and subscriptions documentation](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-queues-topics-subscriptions) describes this distinction.

For an `orders` topic, create subscriptions such as:

- `billing` for accepted orders;
- `notifications` for accepted and cancelled orders;
- `audit` for every event.

## Publish a Stable Integration Event

Do not publish an internal domain object. Define a contract that downstream services can evolve against.

```csharp
public sealed record OrderAcceptedV1(
    Guid EventId,
    Guid OrderId,
    string CustomerId,
    decimal Total,
    DateTimeOffset OccurredAt);

public sealed class OrderEvents(ServiceBusClient client)
{
    private readonly ServiceBusSender _sender = client.CreateSender("orders");

    public Task PublishAsync(
        OrderAcceptedV1 integrationEvent,
        CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(integrationEvent))
        {
            MessageId = integrationEvent.EventId.ToString(),
            Subject = "orders.accepted.v1",
            ContentType = "application/json",
            CorrelationId = integrationEvent.OrderId.ToString()
        };

        message.ApplicationProperties["eventType"] = "orders.accepted";
        message.ApplicationProperties["schemaVersion"] = 1;
        message.ApplicationProperties["region"] = "ca";

        return _sender.SendMessageAsync(message, cancellationToken);
    }
}
```

Use application properties for small routing facts. Do not copy a large body into properties merely to make filtering possible.

## Create Narrow Subscription Rules

`ServiceBusAdministrationClient` manages topology. Run this from deployment automation or a controlled migration, not from every worker replica.

```csharp
using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;

var admin = new ServiceBusAdministrationClient(
    fullyQualifiedNamespace,
    new DefaultAzureCredential());

var options = new CreateRuleOptions(
    "accepted-orders",
    new SqlRuleFilter(
        "eventType = 'orders.accepted' AND schemaVersion = 1"));

await admin.CreateRuleAsync(
    "orders",
    "billing",
    options,
    cancellationToken);
```

New subscriptions normally begin with a `$Default` rule that matches every message. Delete it after creating the intended rules when broad delivery is not desired:

```csharp
await admin.DeleteRuleAsync(
    "orders",
    "billing",
    RuleProperties.DefaultRuleName,
    cancellationToken);
```

Order rule changes carefully. Deleting the default rule before a replacement exists can create a window in which events match nothing.

## Process Each Subscription Independently

Each service creates a processor for its own subscription:

```csharp
ServiceBusProcessor processor = client.CreateProcessor(
    topicName: "orders",
    subscriptionName: "billing",
    new ServiceBusProcessorOptions
    {
        AutoCompleteMessages = false,
        MaxConcurrentCalls = 8
    });
```

Billing completes only after its transaction commits. Notifications can retry on an email-provider outage without forcing billing to repeat. Each subscription needs its own dead-letter monitoring and capacity plan.

## Evolve Contracts Additively

An event is a promise to consumers. Prefer additive optional fields and new event versions over silently changing meaning. Keep old rules and consumers during migration:

1. publish `orders.accepted.v2` alongside or in place of v1 according to a documented transition;
2. add v2 subscription rules;
3. deploy compatible consumers;
4. measure v1 traffic and consumer adoption;
5. remove v1 only after the compatibility window closes.

Avoid filters tied to volatile implementation details. A rule such as `total > 1000` embeds a business threshold in infrastructure. That can be appropriate for routing, but ownership and change control must be explicit.

## Plan for Unmatched and Failed Events

Service Bus does not create a global “no subscription matched” destination. Ensure every event type has an intended subscription, or add a catch-all audit subscription during migrations.

Filter evaluation failures and repeated consumer failures can dead-letter messages at the subscription level. Alert on each DLQ separately and include topic, subscription, event type, and message ID in diagnostics.

Topics reduce producer coupling; they do not remove contract ownership. Durable fan-out works when topology, schemas, filters, and per-subscription operations evolve together.
