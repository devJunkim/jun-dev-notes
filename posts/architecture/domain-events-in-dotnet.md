---
title: "Domain Events in .NET: Keeping Business Logic Decoupled"
excerpt: "Use domain events to express business facts in .NET while keeping transaction boundaries, handler failures, and durable delivery explicit."
category: "Architecture"

seo:
  focusKeyword: "domain events in .NET"
  description: "Design domain events in .NET with practical aggregate and handler examples, transaction trade-offs, integration events, and outbox reliability."
  socialTitle: "Domain Events in .NET: Business Facts and Reliable Boundaries"
  socialDescription: "Separate domain behavior from reactions without confusing an in-memory event dispatcher with durable message delivery."
---

# Domain Events in .NET: Keeping Business Logic Decoupled

Placing an order may also create a fulfillment request, update a customer summary, and arrange a notification. Putting every reaction inside `Order.Place()` couples the entity to services that have little to do with its own rules.

A domain event lets the entity record a business fact while the application decides how to react. That separation helps only when the transaction and failure behavior remain understandable.

> **Quick answer:** A domain event describes something meaningful that happened in the domain. Keep the fact in the domain model, coordinate handlers in the application layer, and choose explicitly which reactions must commit together and which require durable asynchronous delivery.

## A Business Fact, Not a Generic Callback

`OrderPlaced` communicates more than `OrderUpdated`. It identifies a transition with business meaning and gives consumers a stable reason to react.

An event should contain the facts its handlers need, rather than a live entity reference that can change underneath them. An order ID, event ID, customer ID, and occurrence time are often more useful than a serialized object graph.

This does not mean every property setter needs an event. If changing a display label has no independent business consequence, an ordinary method and save operation may be enough.

### Domain events and integration events

| Concern | Domain event | Integration event |
| --- | --- | --- |
| Audience | The application's domain/application workflow | Another process, service, or external consumer |
| Contract | Can evolve with the application | Needs deliberate versioning and compatibility |
| Delivery | Often an in-process dispatcher | Usually durable infrastructure with retries |
| Transaction question | Which local changes belong together? | How does a committed fact reach another system? |

A domain event can lead to an integration event, but it need not be the public message schema. Mapping `OrderPlaced` to a versioned fulfillment message keeps internal model changes from silently changing another service's contract.

Neither kind of event implies event sourcing. An application can store the current order state in relational tables and still use domain events.

## Let the Entity Record the Transition

The following example models only the placement transition. Pricing, line items, persistence mapping, and authorization belong to a larger application and are intentionally outside this focused example.

```csharp
public sealed record OrderPlaced(
    Guid EventId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAt);

public sealed class Order
{
    private readonly List<OrderPlaced> pendingEvents = [];

    public Guid Id { get; }
    public Guid CustomerId { get; }
    public bool IsPlaced { get; private set; }

    public Order(Guid id, Guid customerId)
    {
        if (id == Guid.Empty || customerId == Guid.Empty)
        {
            throw new ArgumentException("Order and customer IDs are required.");
        }

        Id = id;
        CustomerId = customerId;
    }

    public void Place(Guid eventId, DateTimeOffset occurredAt)
    {
        if (IsPlaced)
        {
            throw new InvalidOperationException("The order is already placed.");
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An event ID is required.", nameof(eventId));
        }

        IsPlaced = true;
        pendingEvents.Add(new OrderPlaced(
            eventId, Id, CustomerId, occurredAt));
    }

    public OrderPlaced[] GetPendingEvents() => pendingEvents.ToArray();

    public void ClearPendingEvents() => pendingEvents.Clear();
}
```

The application supplies identity and time, making the behavior deterministic in tests. The entity knows nothing about email, EF Core, SQS, or a mediator library.

The pending list is transient bookkeeping, not a durable event store. An EF mapping should persist the order's business state and ignore this pending-event mechanism. A production aggregate would also enforce its placement rules, such as requiring valid order lines, before recording the transition.

For the broader responsibility split, see [Where Should Business Logic Live in Clean Architecture?](https://dev.jun-kim.net/2026/09/13/where-should-business-logic-live-in-clean-architecture/).

## Dispatch in the Application Layer

An application handler can react to the event through an explicit interface. There is no requirement to begin with reflection, a generic event bus, or a mediator package.

```csharp
public interface IOrderPlacedHandler
{
    Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken);
}

public sealed class OrderEventDispatcher(
    IEnumerable<IOrderPlacedHandler> handlers)
{
    public async Task DispatchAsync(
        IEnumerable<OrderPlaced> events,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in events)
        {
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
        }
    }
}
```

Dispatch is sequential. That makes failure behavior straightforward and avoids concurrent access to a shared scoped `DbContext`, which is not thread-safe. It does not establish a business guarantee about handler registration order. If handler B requires handler A's output, represent that dependency as an explicit workflow rather than a hidden ordering convention.

An exception stops the remaining handlers. There is no retry queue inside this dispatcher. Those properties are part of its contract, not incidental implementation details.

## Choose What Must Commit Together

Consider an order that must never be committed without a durable intent to request fulfillment. One approach is to dispatch before commit and have a handler stage an outbox record in the same database unit of work.

```csharp
public sealed record FulfillmentRequestedV1(
    Guid MessageId, Guid OrderId, Guid CustomerId);

public interface IOutboxWriter
{
    // Stage a record in the current unit of work; do not send it now.
    void Add(FulfillmentRequestedV1 message);
}

public sealed class StageFulfillment(IOutboxWriter outbox)
    : IOrderPlacedHandler
{
    public Task HandleAsync(
        OrderPlaced domainEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        outbox.Add(new FulfillmentRequestedV1(
            domainEvent.EventId, domainEvent.OrderId, domainEvent.CustomerId));
        return Task.CompletedTask;
    }
}

public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    OrderEventDispatcher dispatcher,
    TimeProvider timeProvider)
{
    public async Task<bool> HandleAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        order.Place(Guid.NewGuid(), timeProvider.GetUtcNow());
        await dispatcher.DispatchAsync(
            order.GetPendingEvents(), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        order.ClearPendingEvents();
        return true;
    }
}
```

These persistence interfaces describe an implementation contract: the repository loads a tracked order, the outbox writer stages a row, and the unit of work saves both through the same relational `DbContext` and transaction. The interfaces alone do not make separate stores atomic. An implementation must provide that guarantee and test it against its database provider.

This example uses a separate unit of work because the commit covers changes staged through more than one abstraction. A repository-owned commit can also be valid when it owns the whole boundary; [Repository Pattern in .NET](https://dev.jun-kim.net/2026/09/11/repository-pattern-in-net-when-it-helps-and-when-it-doesnt/) discusses that trade-off.

Register the participating services with compatible scoped lifetimes so they share the intended context. [Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) covers how lifetime choices affect that arrangement.

Authorization must run before this use case is allowed to place the order. A real persistence implementation also needs concurrency protection so two requests cannot both commit the same transition. The in-memory `IsPlaced` check alone cannot protect concurrent database transactions.

### What a failure means here

If a handler fails before `SaveChangesAsync`, the application must abandon this unit of work. Earlier handlers may already have staged changes in memory. Do not catch the exception and save that partly processed context anyway.

Likewise, do not blindly redispatch the pending list in the same context: that can stage duplicate outbox rows. Dispose the failed scope and apply an explicit retry policy around a fresh operation. A unique message ID constraint helps detect duplicate staging, but request-level idempotency is a separate concern.

The sample dispatcher processes the supplied event snapshot. If handlers raise additional domain events, a larger implementation needs a documented strategy for draining them, detecting cycles, and bounding dispatch. Adding that machinery before the workflow needs it makes the simple case harder to follow.

## Before Commit and After Commit Solve Different Problems

| Dispatch timing | Useful property | Cost or failure risk |
| --- | --- | --- |
| Before commit, same unit of work | Local state and handler changes can succeed together | Handlers lengthen the operation; failure aborts it |
| After commit, in memory | The order is already durable when handlers run | A crash can lose the pending reactions |
| Durable outbox, delivered later | Committed work survives a process restart | Delivery is asynchronous and may be repeated |

“Before commit” only helps transactional consistency if the handler's writes actually participate in that transaction. Sending an email before saving an order cannot be rolled back if the save fails.

“After commit” does not undo a successful save when a handler throws. Returning a generic failure to the caller can then encourage a retry of an operation that already succeeded. Decide whether the reaction is best-effort or whether it needs persistent tracking and a separate delivery lifecycle.

For durable delivery, an outbox worker reads committed messages, publishes them, and records progress. A crash after publishing but before recording completion can cause another delivery. Consumers still need idempotency. The outbox closes the database-to-message reliability gap; it does not create exactly-once effects across independent systems.

## Domain Events Can Complement CQRS

A command changes state; an event describes a change that occurred. A query reads state. They serve different purposes, even when all three use classes named “handlers.”

An order event might update a read model. If that update happens asynchronously, the query side can lag behind the command side. The UI and API contract need to tolerate that delay rather than assuming a successful command makes every projection immediately current.

Separate read storage is optional. [CQRS in .NET](https://dev.jun-kim.net/2026/09/11/cqrs-in-net-when-command-query-responsibility-segregation-makes-sense/) explains the simpler same-database form. Domain events are useful independently of whether a project adopts CQRS.

## Test Facts, Reactions, and Failure Boundaries

Entity tests should verify that placement changes state, produces one event with the supplied identifiers and time, and rejects a second placement without recording another event.

Handler tests should verify the mapped fulfillment contract. Dispatcher tests should verify that a failure stops later handlers. These tests are fast and do not need a broker.

Database integration tests answer different questions: do the order and outbox commit together, does a concurrency conflict prevent duplicate placement, and does rollback leave neither change committed? Delivery tests should cover a worker restart and a duplicate message, including the crash window after an external send.

Mocking `SaveChangesAsync` can test orchestration, but it cannot prove transaction atomicity.

## Use Events Where the Indirection Earns Its Place

Use a domain event when a business transition has independently meaningful reactions, when those reactions change at different rates, or when the event makes a durable handoff easier to express.

Prefer direct calls when the next step is a required calculation in the same small workflow, when execution order is essential, or when there is only one straightforward dependency. Events can reduce coupling between classes while increasing the effort needed to trace execution.

Before introducing one, be able to answer three questions: what fact occurred, what must commit with it, and how will a failed reaction recover? Those answers matter more than the dispatcher library.
