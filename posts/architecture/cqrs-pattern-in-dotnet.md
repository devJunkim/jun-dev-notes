---
title: "CQRS in .NET: When Command Query Responsibility Segregation Makes Sense"
excerpt: "Learn how CQRS separates write and read responsibilities in .NET, with a small ASP.NET Core example and practical guidance on costs and trade-offs."
category: "Architecture"

seo:
  focusKeyword: "CQRS in .NET"
  description: "A balanced guide to CQRS in .NET: commands, queries, handlers, ASP.NET Core DI, read models, transactions, event sourcing, and when CRUD is simpler."
  socialTitle: "CQRS in .NET: When It Makes Sense"
  socialDescription: "See a practical CQRS example without MediatR, and learn when command/query separation helps or adds needless complexity."
---

# CQRS in .NET: When Command Query Responsibility Segregation Makes Sense

An order screen may need a compact summary of hundreds of orders, while an order submission must validate stock and commit a consistent change. These operations serve different purposes. CQRS makes that difference explicit by separating the code that changes state from the code that reads it.

> **Quick answer:** CQRS separates commands from queries and can give each side its own model and handler. It does not require a mediator library, event sourcing, or separate databases. Apply it where differing read and write needs justify the extra structure; ordinary service methods are often clearer for simple CRUD.

## What CQRS Means

**Command Query Responsibility Segregation** divides application operations into two categories. A **command** requests a state change. A **query** returns information without intentionally changing application state. The distinction is about responsibilities, not necessarily physical deployment or database layout.

| Concern | Command side | Query side |
| --- | --- | --- |
| Intent | Change state | Return data |
| Typical input | A request describing an action | Filters, identifiers, paging |
| Typical output | Result, identifier, or error | Read DTO or collection |
| Rules | Validation, authorization, invariants | Projection and access control |
| Persistence | Writes in a transaction where needed | Often optimized reads |

A query can still cause incidental effects such as telemetry or a database cache hit. The key promise is that asking for data does not intentionally change domain state. A command can return a created ID or status; CQRS does not forbid all return values.

CQRS can be as small as separate methods in one process. It can also evolve into separate services and read stores when scale or data-flow needs demand them. Starting with the smallest useful separation keeps costs visible.

The [Azure Architecture Center's CQRS guidance](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs) likewise distinguishes a basic shared-store implementation from more elaborate architectures. That distinction matters during design reviews: a team can adopt the useful command/query boundary without accepting every infrastructure cost associated with a distributed example.

## Commands, Queries, Handlers, and DTOs

A command names an action in the business vocabulary, such as `PlaceOrder`, rather than merely mirroring a database table's `Insert`. A command handler coordinates validation, persistence, and side effects for that action. A query handler retrieves data and maps it to a read-focused **data transfer object** (DTO).

```csharp
public sealed record PlaceOrder(Guid CustomerId, decimal Total);
public sealed record OrderSummary(Guid Id, decimal Total, string Status);

public interface IPlaceOrderHandler
{
    Task<Guid> HandleAsync(PlaceOrder command, CancellationToken cancellationToken);
}

public interface IGetOrderHandler
{
    Task<OrderSummary?> HandleAsync(Guid id, CancellationToken cancellationToken);
}
```

The DTO is shaped for a caller, not required to be an entity mirror. A list page might need `Id`, `Total`, and `Status`; a detail page might need line items. Separate DTOs prevent read needs from forcing write-domain objects to expose irrelevant data.

## A Small In-Process ASP.NET Core Example

The following is a single-file minimal API example targeting modern ASP.NET Core with EF Core packages and a configured provider. It uses one database and direct dependency injection; no mediator package is involved. The entity has a private setter for status so a command handler controls the initial state.

```csharp
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<OrdersDb>(options =>
    options.UseSqlite("Data Source=orders.db"));
builder.Services.AddScoped<PlaceOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();

var app = builder.Build();

app.MapPost("/orders", async (
    PlaceOrder request,
    PlaceOrderHandler handler,
    CancellationToken cancellationToken) =>
{
    Guid id = await handler.HandleAsync(request, cancellationToken);
    return Results.Created($"/orders/{id}", new { id });
});

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    GetOrderHandler handler,
    CancellationToken cancellationToken) =>
{
    OrderSummary? order = await handler.HandleAsync(id, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.Run();

public sealed record PlaceOrder(Guid CustomerId, decimal Total);
public sealed record OrderSummary(Guid Id, decimal Total, string Status);

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; private set; } = "Placed";
}

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class PlaceOrderHandler(OrdersDb db)
{
    public async Task<Guid> HandleAsync(
        PlaceOrder command,
        CancellationToken cancellationToken)
    {
        if (command.CustomerId == Guid.Empty || command.Total <= 0)
            throw new ArgumentException("A customer and positive total are required.");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = command.CustomerId,
            Total = command.Total
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}

public sealed class GetOrderHandler(OrdersDb db)
{
    public Task<OrderSummary?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking()
            .Where(order => order.Id == id)
            .Select(order => new OrderSummary(
                order.Id, order.Total, order.Status))
            .SingleOrDefaultAsync(cancellationToken);
}
```

To run this as a real project, register `Microsoft.EntityFrameworkCore.Sqlite` and create the schema with a migration before sending requests. The example deliberately keeps HTTP details outside the handlers. Production endpoints should map validation failures to client responses and apply authentication and authorization appropriate to the use case.

This is CQRS because the write operation and read operation have separate models and paths. Both use the same `OrdersDb`; separate databases are **not** a requirement.

## Validation, Transactions, and Consistency

Validation belongs at more than one level. An endpoint can reject malformed input. A command handler can check use-case conditions. The domain model or database constraints should enforce invariants that must hold regardless of entry point. Avoid copying the same rule into every layer without deciding which layer owns the authoritative check.

For a command that changes several rows, define a transaction boundary around the complete state change. [EF Core's transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions) explains that a single `SaveChangesAsync` applies its changes transactionally by default when the provider supports transactions. Multiple saves, external services, and message publication need additional design: a database transaction cannot automatically make an HTTP call or broker publish atomic. An outbox can coordinate durable message publication when that requirement exists.

Concurrent commands need thought beyond a transaction. Two requests may read the same order and both try to update it. A database concurrency token or carefully chosen conditional update can detect a conflict; the handler can then return a conflict response, retry safely, or ask the user to reload. Validation performed before a write cannot by itself prevent a race. The domain rule and persistence operation must agree on what happens under concurrency.

Queries reading the same database can generally see committed writes according to the database's isolation behavior. If the read side uses an asynchronously updated projection, it may be **eventually consistent**. That trade-off must be visible in user-facing workflows: a newly placed order may take time to appear in a reporting view. CQRS alone does not imply eventual consistency; the chosen data flow does.

## Read Models and Separate Stores

A read model is data shaped for a query. In a modest app it can simply be an EF Core projection into `OrderSummary`, as in the example. At larger scale it might be a materialized table or a search index built from changes on the write side.

Separate read and write databases are useful when independent scaling, specialized indexing, or different consistency requirements justify synchronization. They introduce replication lag, monitoring, failure recovery, and data-rebuild questions. Do not add them just to satisfy a diagram labeled CQRS.

## Mediators, Clean Architecture, and Event Sourcing

**MediatR is optional.** Directly injecting a handler or service is already enough to separate commands and queries. A mediator-style dispatcher can route typed requests to handlers and apply cross-cutting behaviors such as logging or validation. Its benefit is consistency in a sufficiently large operation catalog; its cost is indirection and another abstraction to trace during debugging.

**Clean Architecture** is about dependency direction and boundaries. CQRS is about separating read and write responsibilities. They can be combined: application-layer handlers may depend on interfaces while infrastructure provides implementations. Neither pattern requires the other, and a Clean Architecture application can use straightforward service methods.

**Event sourcing** stores a sequence of events as the source of truth. CQRS does **not** require event sourcing. A conventional relational table can back both sides. Conversely, event-sourced systems often use CQRS-style projections because reconstructing every read directly from an event stream is expensive. These patterns solve related but distinct problems.

## When CQRS Helps and When It Adds Work

CQRS is a good fit when reads and writes have substantially different models, command workflows contain meaningful business rules, read throughput needs specialized projections, or separate teams own well-defined operational paths. It can also make a complex domain easier to navigate by giving each use case a focused handler.

Ordinary application or service methods are often simpler for a small CRUD application where reads and writes map closely to the same entity and there are few rules. Adding one command class, handler interface, handler implementation, validator, dispatcher, and DTO for every trivial property update can obscure rather than clarify the behavior.

Avoid generic `Create<T>` and `Update<T>` commands that merely reproduce repository CRUD, handlers that do nothing but pass through to a repository, and separate databases before consistency requirements are understood. If every handler has almost identical plumbing, reconsider the boundary. The measure is clarity and needed flexibility, not file count.

Test the behavior at the level where it can fail. Unit tests can exercise a handler's rule decisions, but an EF Core projection, transaction, or concurrency check needs integration tests against a representative provider. For an asynchronous read model, test what a caller sees before and after projection updates, including retries and duplicate events. Mocking a dispatcher and asserting that it dispatched a command rarely proves the business outcome.

## Interview Questions

**Does CQRS require two databases?** No. Read and write code can share one database; separate stores are an optional scaling or modeling choice.

**Does CQRS require event sourcing?** No. Commands can update ordinary tables. Event sourcing is a separate persistence model.

**Is a mediator library necessary?** No. Direct DI and focused handlers implement in-process CQRS. A dispatcher is optional.

**Can a command return data?** Yes, often an ID or operation result. The command's purpose remains a state change.

**When is CRUD preferable?** When operations are simple, models closely align, and CQRS layers add more navigation than value.

## Summary

CQRS gives read and write operations separate responsibilities and models. Start with in-process handlers and one database if that is enough. Add dispatch, projections, or independent stores only when concrete workflow, scale, or consistency needs justify their complexity.
