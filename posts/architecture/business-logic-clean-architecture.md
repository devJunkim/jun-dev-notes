---
title: "Where Should Business Logic Live in Clean Architecture?"
excerpt: "Place business rules in the right layer with a practical .NET cancellation workflow spanning the API, application, domain, and infrastructure."
category: "Architecture"

seo:
  focusKeyword: "business logic in Clean Architecture"
  description: "Learn where business logic belongs in Clean Architecture, with practical .NET examples of controllers, use cases, entities, and domain services."
  socialTitle: "Where Should Business Logic Live in Clean Architecture?"
  socialDescription: "Place business rules in the right layer with a practical .NET cancellation workflow spanning the API, application, domain, and infrastructure."
---

# Where Should Business Logic Live in Clean Architecture?

"Put business logic in services" sounds useful until the application has an OrderService with forty methods and entities that are little more than writable database rows. Moving code out of controllers improved one boundary, but it did not establish who owns the rules.

Clean Architecture helps when it clarifies dependencies and responsibilities. It becomes less useful when every decision is reduced to which project name should contain a class.

> **Quick answer:** Put business invariants with the domain behavior they protect, coordinate use cases in the application layer, translate HTTP in the API layer, and implement external access in infrastructure. Not every operation needs a domain service or a rich entity.

## What Counts as Business Logic?

Business logic includes decisions that give the software its business meaning: whether an order can be cancelled, how a discount is calculated, or which account can approve a payment. It also includes application workflows that coordinate those decisions.

These are related but different responsibilities:

| Example | Primary owner |
| --- | --- |
| Parse an order ID from a route | API |
| Determine the authenticated caller | API/security adapter |
| Verify the caller may cancel this order | Application authorization, using relevant policy |
| Load the order and save the result | Application orchestration |
| Reject cancellation after shipment | Domain |
| Calculate a cancellation fee from supplied facts | Domain entity or domain service |
| Translate a database concurrency exception | Infrastructure adapter |
| Translate a use-case conflict into HTTP 409 | API |

A business rule should remain meaningful if the entry point changes from HTTP to a queue consumer. That is a useful test for separating the rule from transport concerns.

## Controllers Should Translate and Delegate

A controller or minimal API endpoint owns the HTTP contract: binding inputs, applying transport-level authentication requirements, invoking an operation, and translating its result. It should not recreate a cancellation policy.

Thin does not mean "one line at any cost." A short result-to-status mapping can be entirely appropriate. Moving it to a generic response wrapper merely to reduce endpoint length may make the contract harder to understand.

Controllers also should not be the only place enforcing access to a use case. Another entry point may call the same handler. Arrange authorization so each invocation receives a trustworthy caller context and cannot bypass required checks.

## The Application Layer Owns the Use Case

An application handler answers: what must happen to complete this operation?

This handler can be a direct dependency-injected service; [CQRS in .NET](https://dev.jun-kim.net/2026/09/11/cqrs-in-net-when-command-query-responsibility-segregation-makes-sense/) explains when separating command and query handlers adds value.

It usually coordinates loading data, checking access, invoking domain behavior, saving changes, and arranging side effects. It may depend on interfaces for storage, identity, time, or messaging. Those interfaces express what the use case needs without binding it to an EF Core provider or HTTP client.

This coordination is real logic. There is no requirement that handlers contain only three delegating statements. The important distinction is whether a decision belongs to the workflow or to the object's business meaning.

"Load the order before cancelling it" is workflow. "A shipped order cannot be cancelled" is an invariant. Copying the latter into every handler that updates an order makes the model easier to violate.

## A Cancellation Flow in .NET

The following excerpts form a small design using a persisted order and optimistic concurrency. They assume C# 12 or later and matching EF Core SQL Server packages. In a real solution, place each group in its corresponding project and namespace.

The runtime flow is:

```text
API endpoint
  -> CancelOrderHandler
      -> IOrderRepository -> EF Core implementation
      -> Order.Cancel()
      -> IOrderRepository -> EF Core commit
  -> HTTP result
```

The dependency direction is different from the call sequence. Application references Domain. Infrastructure references the inner contracts. The API's composition root registers concrete implementations. Domain does not reference ASP.NET Core or EF Core.

### Domain: protect the transition

```csharp
public enum OrderStatus { Pending, Shipped, Cancelled }
public enum CancelDecision { Cancelled, AlreadyCancelled, Rejected }

public sealed class Order
{
    private Order() { } // EF Core materialization

    public Order(Guid id, Guid customerId)
    {
        if (id == Guid.Empty || customerId == Guid.Empty)
            throw new ArgumentException("Order and customer IDs are required.");

        Id = id;
        CustomerId = customerId;
        Status = OrderStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }

    public CancelDecision Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            return CancelDecision.AlreadyCancelled;

        if (Status != OrderStatus.Pending)
            return CancelDecision.Rejected;

        Status = OrderStatus.Cancelled;
        return CancelDecision.Cancelled;
    }
}
```

This rule treats repeated cancellation as successful and rejects every state except Pending. That is an explicit business choice. Another domain may require a cancellation reason, a refund policy, or a narrower time window.

The entity does not fetch its own customer or save itself. It decides from state already available to it. The example shows only cancellation; adding shipment should introduce another controlled transition rather than a public status setter.

### Application: coordinate the work

```csharp
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}

public interface ICurrentCustomer
{
    Guid Id { get; }
}

public enum CancelOrderOutcome { Success, NotFound, Conflict }

public sealed class CancelOrderHandler(
    IOrderRepository orders,
    ICurrentCustomer customer)
{
    public async Task<CancelOrderOutcome> HandleAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        Order? order = await orders.FindAsync(orderId, cancellationToken);

        // Deliberately avoid revealing another customer's order.
        if (order is null || order.CustomerId != customer.Id)
            return CancelOrderOutcome.NotFound;

        CancelDecision decision = order.Cancel();

        if (decision == CancelDecision.Rejected)
            return CancelOrderOutcome.Conflict;

        if (decision == CancelDecision.AlreadyCancelled)
            return CancelOrderOutcome.Success;

        bool saved = await orders.TrySaveAsync(cancellationToken);
        return saved
            ? CancelOrderOutcome.Success
            : CancelOrderOutcome.Conflict;
    }
}
```

The authenticated customer comes from an adapter, not from a customer ID supplied in the request body. The application chooses a non-disclosing not-found policy for unauthorized access. More complex authorization can use a dedicated policy abstraction.

Returning a result keeps expected rejection out of exception-based control flow. Unexpected storage errors still propagate to the API's centralized exception handling.

### Infrastructure: implement persistence and concurrency

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class OrdersDb(DbContextOptions<OrdersDb> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(order => order.Id);

        // SQL Server rowversion, kept out of the domain class.
        modelBuilder.Entity<Order>()
            .Property<byte[]>("Version")
            .IsRowVersion();
    }
}

public sealed class EfOrderRepository(OrdersDb db) : IOrderRepository
{
    public Task<Order?> FindAsync(
        Guid id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(
            order => order.Id == id, cancellationToken);

    public async Task<bool> TrySaveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
```

The database token matters because two requests can load the same Pending order. A domain check alone cannot stop one request from cancelling while another ships. The token makes the stale write fail; this use case reports a conflict and ends its request scope.

This example puts `SaveChangesAsync` behind `IOrderRepository` as a deliberate commit, or unit-of-work, boundary for this single-aggregate use case. The handler decides *when* to commit; the EF Core adapter performs the provider-specific write and translates its concurrency failure. For workflows spanning several repositories, a separate unit-of-work abstraction can make the shared commit point clearer. [Repository Pattern in .NET](https://dev.jun-kim.net/2026/09/11/repository-pattern-in-net-when-it-helps-and-when-it-doesnt/) discusses that trade-off and when direct EF Core access is simpler.

This repository is intentionally narrow. It catches only the known concurrency condition. It does not swallow connectivity errors or claim every failed save is a business conflict. The request should end after a failed save; continuing to reuse the same tracked state would need an explicit recovery policy.

Create and apply a migration before using this model. SQL Server rowversion is provider-specific; another database needs its own concurrency-token configuration.

### API: expose the use case

```csharp
using System.Security.Claims;

public sealed class HttpCurrentCustomer(IHttpContextAccessor accessor)
    : ICurrentCustomer
{
    public Guid Id
    {
        get
        {
            string? value = accessor.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(value, out Guid id) && id != Guid.Empty
                ? id
                : throw new InvalidOperationException(
                    "The authenticated customer claim is invalid.");
        }
    }
}
```

At the API composition root, register the handler, repository, current-customer adapter, `IHttpContextAccessor`, and SQL Server `OrdersDb` with scoped lifetimes where appropriate. [Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) covers registration and lifetime choices. Configure authentication and authorization for the identity provider used by the application; the following endpoint assumes that setup already exists.

```csharp
app.MapPost("/orders/{id:guid}/cancel", async (
    Guid id,
    CancelOrderHandler handler,
    CancellationToken cancellationToken) =>
{
    CancelOrderOutcome outcome =
        await handler.HandleAsync(id, cancellationToken);

    return outcome switch
    {
        CancelOrderOutcome.Success => Results.NoContent(),
        CancelOrderOutcome.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { code = "order_cancellation_conflict" })
    };
}).RequireAuthorization();
```

The endpoint knows HTTP status codes. The handler knows the workflow. The entity knows when cancellation is valid. Infrastructure knows how to persist it.

## When a Domain Service Belongs in the Design

Some rules do not naturally belong to one entity. A cancellation fee may depend on a booking, a policy, and the current date. A domain service can express that calculation using supplied facts:

```csharp
public sealed class CancellationFeePolicy
{
    public decimal Calculate(
        decimal paidAmount,
        DateOnly departure,
        DateOnly today)
    {
        if (paidAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(paidAmount));

        return departure.DayNumber - today.DayNumber >= 7
            ? 0m
            : paidAmount * 0.25m;
    }
}
```

The numbers here are illustrative business rules, not a universal travel policy. The application supplies the date and loads the required data. That keeps time and external access visible and makes the calculation straightforward to test.

A domain service is not a dumping ground for methods that were inconvenient to place elsewhere. Name it after a business concept, and keep it focused on a rule. A service whose main job is sending HTTP requests belongs in infrastructure even if the remote system performs business work.

## Avoiding an Anemic Design Without Overengineering CRUD

An anemic design appears when entities expose arbitrary setters while services repeat all meaningful constraints. Every caller must remember the right sequence of assignments, and new entry points can bypass the rules.

Encapsulating transitions helps when the domain has state and invariants. It does not mean that every lookup table needs elaborate behavior. A simple administrative screen may be well served by validation, an application operation, and persistence.

Read models can remain plain DTOs. They describe information, not valid transitions. Forcing behavior into a reporting projection confuses read needs with domain responsibilities.

Likewise, do not introduce an interface for every class by reflex. Use boundaries where they isolate an external dependency, support meaningful substitution, or protect a dependency rule.

## Transactions, Side Effects, and Tests

If cancellation must produce a notification reliably, changing the order and calling an email provider are not one atomic operation. Store an outbox message with the state change, then deliver it separately when reliability warrants that design. Infrastructure implements delivery; application and domain determine the event's meaning.

Tests should follow the responsibility:

- Domain tests cover Pending, Shipped, and repeated cancellation.
- Handler tests cover ownership checks, missing orders, and save conflicts.
- Database integration tests verify the actual concurrency-token behavior.
- API tests verify authorization and result-to-HTTP mapping.

Mocks cannot prove that SQL Server rejects a stale update. Conversely, every fee calculation does not need a database fixture.

## Practical Placement Guide

Ask three questions when a rule's home is unclear: would it still exist without HTTP, does it describe valid business state, and does it require coordinating external work?

Keep valid-state decisions in the domain. Keep coordination in the application. Keep serialization and HTTP translation at the boundary. Keep database and provider mechanics in infrastructure.

The best boundary lets a future developer change a cancellation policy in one obvious place and verify it without starting the entire system.
