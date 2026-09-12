---
title: "Repository Pattern in .NET: When It Helps and When It Doesn't"
excerpt: "Learn what the Repository Pattern contributes in .NET, how it relates to EF Core, and when a custom repository clarifies—or complicates—an application."
category: "Architecture"

seo:
  focusKeyword: "Repository Pattern in .NET"
  description: "A balanced guide to the Repository Pattern in .NET, including EF Core, generic repositories, testing, query boundaries, and practical trade-offs."
  socialTitle: "Repository Pattern in .NET: When It Helps"
  socialDescription: "Understand custom repositories, EF Core trade-offs, generic CRUD limitations, testing strategies, and when direct DbContext use is simpler."
---

# Repository Pattern in .NET: When It Helps and When It Doesn't

Few .NET architecture discussions produce advice as polarized as the Repository Pattern. One team creates a repository for every entity because “all data access needs abstraction.” Another rejects repositories because Entity Framework Core already provides `DbContext` and `DbSet<T>`.

Both positions skip the design question: **what boundary does this application need?** A custom repository can isolate complex persistence behavior, express application-specific queries, and provide a useful test seam. It can also become a thin CRUD wrapper that hides EF Core features, duplicates APIs, and adds files without adding meaning.

> **Quick answer:** Use a custom repository when it creates a valuable boundary around persistence or business-oriented queries. Using EF Core directly in a focused application layer is often simpler when the extra abstraction would only mirror `DbSet<T>`.

## What the Repository Pattern Is

A repository presents access to a collection of domain or application objects while hiding some details of storage and retrieval. Callers ask for meaningful objects or results instead of constructing database commands.

```csharp
public interface IOrderRepository
{
    Task<Order?> FindAsync(
        OrderId id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> FindOverdueAsync(
        DateOnly asOf,
        CancellationToken cancellationToken);

    void Add(Order order);
}
```

This contract is not tied to SQL, EF Core, or a table-shaped API. It describes operations the application needs.

Historically, repositories helped separate object-oriented domain code from persistence mechanisms, centralize query behavior, and make a set of persisted objects feel collection-like. The pattern predates EF Core and does not require an object-relational mapper.

## Repository Responsibilities Versus Service Responsibilities

A repository should focus on persistence concerns: retrieving, adding, and sometimes removing objects or returning query results. An application service coordinates a use case, applies policy, calls repositories, and commits a unit of work.

```csharp
public sealed class CancelOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ISystemClock clock)
{
    public async Task HandleAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        order.Cancel(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

The repository finds the aggregate. The domain object enforces cancellation behavior. The handler coordinates the operation. Moving all business rules into `OrderRepository` would mix persistence with use-case policy.

Some applications do not use rich domain entities. A query repository can return projections designed for a screen or API without pretending every read is aggregate retrieval.

```csharp
public interface IOrderQueries
{
    Task<OrderDetails?> GetDetailsAsync(
        Guid orderId,
        CancellationToken cancellationToken);
}

public sealed record OrderDetails(
    Guid Id,
    string CustomerName,
    decimal Total,
    string Status);
```

Separating command-oriented repositories from query services is an option, not a mandatory architecture.

## Implementing a Repository with EF Core

An EF Core implementation can keep query and mapping details behind the contract.

```csharp
public sealed class EfOrderRepository(OrdersDbContext dbContext)
    : IOrderRepository
{
    public Task<Order?> FindAsync(
        OrderId id,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(
                order => order.Id == id,
                cancellationToken);

    public async Task<IReadOnlyList<Order>> FindOverdueAsync(
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Where(order =>
                order.Status == OrderStatus.Open &&
                order.DueDate < asOf)
            .OrderBy(order => order.DueDate)
            .ToListAsync(cancellationToken);
    }

    public void Add(Order order) => dbContext.Orders.Add(order);
}
```

Repository abstractions should not force unnatural implementation code. Design signatures around clear caller needs while respecting the actual data-access technology.

Register the implementation through dependency injection:

```csharp
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Orders")));

builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IUnitOfWork>(provider =>
    provider.GetRequiredService<OrdersDbContext>());
```

`DbContext` is normally scoped per web request. Repositories using it should usually share that scope rather than create contexts independently for each method.

## EF Core Already Resembles Repository and Unit of Work

`DbSet<TEntity>` presents query and change operations for an entity set, resembling a repository. `DbContext` tracks changes across sets and saves them as a unit, resembling Unit of Work.

```csharp
var order = await dbContext.Orders
    .Include(order => order.Lines)
    .SingleAsync(order => order.Id == orderId, cancellationToken);

order.Cancel(DateTimeOffset.UtcNow);

await dbContext.SaveChangesAsync(cancellationToken);
```

This code is direct, expressive, and uses EF Core's native query capabilities. Wrapping it with methods named `GetById`, `Update`, and `Save` may not improve the design.

Saying “EF Core is a repository” can also be too broad. It is a persistence framework with repository-like APIs, not necessarily the application-specific boundary your domain needs. Whether to add a custom layer depends on coupling, query ownership, testing strategy, and the application's expected evolution.

Microsoft's [EF Core testing strategy guidance](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy) discusses repositories as one possible test-double boundary while also emphasizing tests against the real database system.

## The Generic Repository Question

A generic repository often starts like this:

```csharp
public interface IRepository<TEntity>
    where TEntity : class
{
    Task<TEntity?> GetByIdAsync(
        object id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TEntity>> GetAllAsync(
        CancellationToken cancellationToken);

    void Add(TEntity entity);
    void Remove(TEntity entity);
}
```

It appears reusable, but business queries quickly need includes, filtering, sorting, projections, pagination, concurrency, and provider-specific features. The abstraction then grows toward a second, less capable ORM:

```csharp
Task<IReadOnlyList<TEntity>> FindAsync(
    Expression<Func<TEntity, bool>> predicate,
    Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy,
    string includeProperties,
    CancellationToken cancellationToken);
```

Now application code knows about expression trees, `IQueryable<T>`, and include strings anyway. The generic wrapper has not isolated EF Core; it has reproduced part of its API with weaker typing.

Generic repositories can still help in constrained systems—for example, uniform storage with simple entity operations, a framework-level library, or a carefully bounded base implementation. Do not assume generic CRUD is valuable merely because many entities have tables.

## When a Custom Repository Adds Value

### Protecting aggregate boundaries

In domain-oriented systems, a repository can load and persist aggregate roots while preventing application code from modifying internal entities independently.

```csharp
public interface IShoppingCartRepository
{
    Task<ShoppingCart?> FindForCustomerAsync(
        CustomerId customerId,
        CancellationToken cancellationToken);

    void Add(ShoppingCart cart);
}
```

The interface speaks in domain terms and avoids exposing unrestricted `IQueryable<ShoppingCart>`.

### Owning complex or repeated queries

A repository or query service can centralize authorization filters, tenant boundaries, projections, compiled queries, or provider-specific optimizations. Callers receive a named operation instead of rebuilding subtle LINQ in several handlers.

### Isolating multiple persistence mechanisms

If one capability might use EF Core today and a service API or document database elsewhere, a stable application contract can isolate that difference. Build this boundary for a credible requirement, not a hypothetical future database swap.

### Creating a test seam above EF Core

A repository that returns completed results can be stubbed without pretending an in-memory LINQ provider behaves like the production database.

```csharp
public sealed class StubOrderRepository(Order? order) : IOrderRepository
{
    public Task<Order?> FindAsync(
        OrderId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(order);

    public void Add(Order newOrder) =>
        throw new NotSupportedException();
}
```

The stub should implement the contract honestly; a focused test interface is often easier than a wide repository whose unused methods throw.

## When Direct EF Core Is Simpler

Direct `DbContext` use can be a strong choice when:

- The application layer is already the intended persistence boundary.
- Queries are local, straightforward, and not duplicated.
- The team benefits from EF Core projections, tracking choices, and provider APIs.
- Integration tests use the real database provider.
- A wrapper would only rename `Add`, `Find`, and `SaveChanges`.

```csharp
public sealed class ListAvailableProductsHandler(ShopDbContext dbContext)
{
    public async Task<IReadOnlyList<ProductListItem>> HandleAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsAvailable)
            .OrderBy(product => product.Name)
            .Select(product => new ProductListItem(
                product.Id,
                product.Name,
                product.Price))
            .ToListAsync(cancellationToken);
    }
}
```

The handler owns one use-case query. Introducing `IProductRepository.GetAvailableProductListItemsAsync` may simply move those lines into another file without changing coupling that matters.

Direct access still needs discipline. Avoid giant handlers, scattered duplicated filters, and leaking tracked entities through unrelated layers.

## Testing Considerations

Repository debates often focus too narrowly on mocking. Database behavior includes translation, collation, constraints, transactions, indexes, and concurrency. A mocked repository cannot verify any of those.

Use a layered testing strategy:

- Unit-test domain and application logic that does not need a database.
- Use repository stubs when the repository is a deliberate application boundary.
- Integration-test EF Core queries against the real production database engine where practical.
- Avoid assuming EF Core's in-memory provider accurately reproduces relational behavior.

If a repository returns `IQueryable<T>`, tests still need a query provider, and application code remains coupled to query composition. Returning query results or purpose-built projections creates a stronger seam, at the cost of less caller flexibility.

## Query-Specific Abstractions

An alternative to one repository per entity is an interface shaped around a feature:

```csharp
public interface IRevenueReportQueries
{
    Task<IReadOnlyList<MonthlyRevenue>> GetMonthlyRevenueAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
```

The implementation can use optimized EF Core LINQ, raw SQL, or a read replica. The application depends on the report it needs, not generic persistence operations.

Command handlers can use `DbContext` directly while query handlers use specialized query services, or vice versa. Consistency matters, but uniformity should not erase meaningful differences between use cases.

## Common Mistakes and Over-Engineering

### Creating one generic repository for every table

Tables are storage structures, not automatically domain aggregates or application boundaries. This approach often exposes generic CRUD where the business needs specific operations.

### Returning `IQueryable<T>` from every repository

It leaks provider behavior, context lifetime, translation failures, and unrestricted query composition. Keep it inside trusted data-access boundaries when it is genuinely useful.

### Putting business workflows in repositories

Repositories should not send emails, decide authorization policy, or coordinate unrelated aggregates. Application services and domain objects own those responsibilities.

### Calling `SaveChanges` inside every repository method

Immediate saves make multi-step transactions difficult and blur Unit of Work ownership. Decide explicitly which layer controls the commit boundary.

### Mocking `DbSet<T>` to prove database queries work

LINQ to Objects behavior does not prove that a relational provider translates or executes the same query. Use real-provider integration tests for query correctness.

### Assuming database independence is free

Providers differ in SQL, transactions, types, collations, and capabilities. A generic interface cannot erase those differences without limiting features or moving complexity elsewhere.

## Practical Decision Guide

Ask these questions before adding a repository:

1. What meaningful boundary will it create?
2. Are queries duplicated or complex enough to need an owner?
3. Does the domain have aggregate persistence rules?
4. Is a test double above EF Core genuinely valuable?
5. Will callers receive focused results, or will EF Core leak through anyway?
6. Does the abstraction simplify change, or only add forwarding methods?

Use a repository when the answers identify concrete value. Use `DbContext` directly when it is already at an appropriate boundary and the extra layer would be ceremonial. Revisit the choice as complexity changes.

## Interview-Oriented Questions

### What problem does the Repository Pattern solve?

It provides an application-facing abstraction over retrieval and persistence, centralizing data-access behavior and potentially separating domain or use-case code from storage details.

### Is `DbContext` already a repository?

`DbSet<T>` and `DbContext` provide repository-like and Unit of Work-like behavior. That may be sufficient, but a custom repository can still add a meaningful domain, query, or testing boundary.

### Why can a generic repository be harmful with EF Core?

It may duplicate EF Core's API, hide useful features, grow complicated query parameters, and add indirection without reducing important coupling.

### Should repositories expose `IQueryable<T>`?

Sometimes within a controlled data layer, but it weakens encapsulation and leaks translation and lifetime concerns. Purpose-built methods returning completed results are safer across architectural boundaries.

### How should repositories be tested?

Unit tests can stub a repository contract to test application behavior. Repository implementations and their queries should be tested against the real database provider where practical.

## Summary

The Repository Pattern is a tool, not an architectural requirement. It helps when it protects aggregate boundaries, owns important queries, isolates a real persistence variation, or supplies a useful seam above EF Core.

It hurts when it becomes generic CRUD over `DbSet<T>`, leaks `IQueryable<T>` everywhere, or adds layers that merely forward calls. Choose custom repositories or direct EF Core access based on the boundary your application actually needs, and test database behavior with the real provider regardless of abstraction style.
