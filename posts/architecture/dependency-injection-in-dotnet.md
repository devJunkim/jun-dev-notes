---
title: "Dependency Injection in .NET: What It Is and Why It Matters"
excerpt: "Learn how dependency injection works in .NET, how it differs from dependency inversion, and how to choose safe service lifetimes in real applications."
category: "Architecture"

seo:
  focusKeyword: "dependency injection in .NET"
  description: "A practical guide to dependency injection in .NET, constructor injection, service registration, lifetimes, testing, and common design mistakes."
  socialTitle: "Dependency Injection in .NET: A Practical Guide"
  socialDescription: "Understand constructor injection, the built-in .NET container, transient, scoped, and singleton lifetimes, and when DI improves a design."
---

# Dependency Injection in .NET: What It Is and Why It Matters

Most useful classes depend on something: a database, an HTTP client, a clock, a cache, or another domain service. The design question is not whether dependencies exist, but who creates them and how clearly those relationships are expressed.

Dependency injection (DI) supplies a class's dependencies from outside the class. In modern .NET applications, those dependencies are commonly declared as constructor parameters and assembled by the built-in service container. This can make code easier to test, configure, and evolve—but only when the abstractions and service lifetimes reflect real needs.

> **Quick answer:** Constructor injection makes required collaborators explicit. Register those collaborators with the correct lifetime, let the container compose the object graph, and avoid using DI as an excuse to create an interface for every class.

## Dependency Injection in .NET at a Glance

Without injection, a class often constructs infrastructure directly:

```csharp
public sealed class OrderService
{
    private readonly SqlOrderRepository _repository =
        new("Server=.;Database=Orders;Trusted_Connection=True;");

    public Task PlaceAsync(Order order, CancellationToken cancellationToken) =>
        _repository.SaveAsync(order, cancellationToken);
}
```

`OrderService` now knows how to construct a SQL repository and where it connects. Testing the service may require a database, and changing storage requires editing business logic.

With constructor injection, the service declares what it needs:

```csharp
public interface IOrderRepository
{
    Task SaveAsync(Order order, CancellationToken cancellationToken);
}

public sealed class OrderService(IOrderRepository repository)
{
    public Task PlaceAsync(Order order, CancellationToken cancellationToken) =>
        repository.SaveAsync(order, cancellationToken);
}

public sealed record Order(Guid Id, decimal Total);
```

Composition code chooses the implementation. `OrderService` focuses on use-case behavior rather than infrastructure construction.

## Dependency Inversion Is Not Dependency Injection

The terms are related, but they are not synonyms.

The **Dependency Inversion Principle** says high-level policy should not depend directly on low-level implementation details; both should depend on suitable abstractions. It is a design principle about the direction of source-code dependencies.

**Dependency injection** is a technique for supplying dependencies to an object rather than having the object construct or locate them. It is one way to assemble a design that follows dependency inversion, but DI can also inject concrete classes, and an interface alone does not guarantee good architecture.

Consider these layers:

```text
Orders.Api       -> Orders.Application
Orders.Sql       -> Orders.Application
```

The application layer can define `IOrderRepository`. The SQL project implements it, while the API composition root connects the interface to that implementation. The high-level use case no longer references the SQL project.

You can use dependency inversion without a container by wiring objects manually:

```csharp
IOrderRepository repository = new InMemoryOrderRepository();
var service = new OrderService(repository);
```

Conversely, registering a tightly coupled concrete object graph in a container uses dependency injection but may not meaningfully improve architectural boundaries.

## Constructor Injection

Constructor injection is the default choice for required dependencies. A valid instance cannot be created without satisfying its constructor contract.

```csharp
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SubscriptionService(
    ISystemClock clock,
    ISubscriptionRepository repository)
{
    public async Task<bool> IsActiveAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await repository.FindAsync(
            subscriptionId,
            cancellationToken);

        return subscription is not null &&
               subscription.ExpiresAt > clock.UtcNow;
    }
}
```

The dependencies are visible in one place. A test can supply controlled implementations, including a fixed clock, without modifying global state.

Constructor injection also exposes design pressure. If a class needs twelve dependencies, hiding them behind a service locator does not solve the problem; it conceals it. The class may have too many responsibilities and deserve decomposition.

### Why not resolve services inside the class?

Injecting `IServiceProvider` and calling `GetRequiredService<T>()` throughout business code is the service-locator pattern. Dependencies become invisible until runtime, tests need container setup, and missing registrations fail far from object construction.

```csharp
// Avoid in ordinary application code.
public sealed class ReportService(IServiceProvider services)
{
    public Task GenerateAsync(CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<IReportRepository>();
        return repository.GenerateAsync(cancellationToken);
    }
}
```

Framework integration and dynamic factories sometimes need `IServiceProvider`, but constructor parameters should describe normal required dependencies.

## Registering Services in ASP.NET Core

ASP.NET Core creates a service collection during startup. Registrations describe how the container should construct each service and how long its instances should live.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrderService service,
    CancellationToken cancellationToken) =>
{
    var order = new Order(Guid.NewGuid(), request.Total);
    await service.PlaceAsync(order, cancellationToken);

    return Results.Created($"/orders/{order.Id}", order);
});

app.Run();

public sealed record CreateOrderRequest(decimal Total);
public sealed record Order(Guid Id, decimal Total);
```

Minimal API handlers can receive registered services as parameters. Controllers use constructor injection:

```csharp
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = new Order(Guid.NewGuid(), request.Total);
        await orderService.PlaceAsync(order, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = order.Id }, order);
    }
}
```

The container examines the selected constructor, resolves its parameters recursively, and creates the object graph. Registrations usually belong near the application's composition root, often grouped by feature through extension methods.

```csharp
public static class OrderServiceCollectionExtensions
{
    public static IServiceCollection AddOrders(
        this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, SqlOrderRepository>();
        services.AddScoped<OrderService>();
        return services;
    }
}
```

## Transient, Scoped, and Singleton Lifetimes

Lifetime selection is part of correctness, not just performance tuning.

| Lifetime | Instance behavior | Common fit | Main risk |
| --- | --- | --- | --- |
| Transient | New instance each time it is resolved | Lightweight, stateless operations | Excess allocation or captured disposable services |
| Scoped | One instance per scope; usually one HTTP request | Request-oriented work, EF Core `DbContext` | Resolving outside a scope or capturing in singleton |
| Singleton | One instance for the application's service provider | Thread-safe shared services and caches | Shared mutable state, thread-safety, captive dependencies |

Microsoft's [.NET service lifetime documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes) provides the authoritative container behavior and disposal guidance.

### Transient

`AddTransient` creates a new instance each time the service is requested.

```csharp
builder.Services.AddTransient<IInvoiceFormatter, InvoiceFormatter>();
```

Transient works well for lightweight, stateless services. “Transient” does not mean the object is immediately disposed after one method call. The container that resolves a disposable transient tracks and disposes it according to that container or scope's lifetime.

### Scoped

`AddScoped` creates one instance per dependency-injection scope. In ASP.NET Core, each HTTP request normally has its own scope.

```csharp
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Orders")));

builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

Entity Framework Core registers `DbContext` as scoped by default. Repositories that use that context are commonly scoped too, allowing one unit of work inside a request without sharing a context across concurrent requests.

A scope is not inherently an HTTP request. Worker services and console applications may need to create scopes explicitly.

### Singleton

`AddSingleton` creates one instance and reuses it for the lifetime of the service provider.

```csharp
builder.Services.AddSingleton<ISystemClock, SystemClock>();
```

Singleton services must be safe for concurrent use. The container's thread-safe resolution does not make the service's own mutable state thread-safe. Singletons can also retain large object graphs until shutdown.

Configuration objects and stateless services can be good singleton candidates. A service is not automatically better as a singleton merely because construction is inexpensive or it appears stateless today.

## The Captive Dependency Problem

A longer-lived service must not capture a shorter-lived dependency in a way that extends its effective lifetime. The classic mistake is constructor-injecting a scoped service into a singleton.

```csharp
builder.Services.AddScoped<OrdersDbContext>();
builder.Services.AddSingleton<OrderCleanupWorker>();

public sealed class OrderCleanupWorker(OrdersDbContext dbContext)
{
    // Incorrect: the singleton captures one scoped DbContext.
}
```

This can reuse a `DbContext` across requests or operations, introduce concurrency errors, and delay disposal. Development scope validation can detect this configuration.

A hosted singleton that needs scoped work should create a scope for each operation:

```csharp
public sealed class OrderCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider
                .GetRequiredService<OrderCleanupService>();

            await cleanup.RunAsync(stoppingToken);
            logger.LogInformation("Order cleanup completed");

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
```

Here service location is contained at a scope boundary where dynamic resolution is necessary, rather than spread through business logic.

## Testing Code That Uses DI

DI does not automatically make code testable. Clear contracts and isolated side effects do. Constructor injection makes substitutions straightforward when a boundary genuinely benefits from one.

```csharp
public sealed class FixedClock(DateTimeOffset value) : ISystemClock
{
    public DateTimeOffset UtcNow => value;
}

var clock = new FixedClock(
    new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
var repository = new InMemorySubscriptionRepository();
var service = new SubscriptionService(clock, repository);
```

Not every dependency needs a mocking framework or interface. Concrete classes can be injected, and small deterministic value objects can often be created directly. Abstract volatile boundaries—time, external services, storage—not every line of code.

## When Dependency Injection Helps

DI is useful when:

- A component has required collaborators with meaningful lifecycles.
- Infrastructure implementations vary between environments.
- Tests need controlled replacements for external boundaries.
- Cross-cutting services such as logging, options, and HTTP clients are centrally configured.
- The application benefits from a clear composition root.

Manual construction may be clearer when:

- A small object has no external collaborators.
- A short utility is pure and deterministic.
- An implementation is an internal detail with no realistic variation.
- Container registration would add ceremony without reducing coupling.

DI should organize dependencies, not inflate the type count. An interface named after every implementation, a repository around every `DbSet`, or layers that only forward calls can make navigation and debugging worse.

## Common Mistakes and Misconceptions

### Confusing DI with dependency inversion

DI supplies objects. Dependency inversion shapes source-code dependencies around policy and abstractions. One can support the other, but neither guarantees good boundaries by itself.

### Selecting lifetimes by habit

Registering everything as transient or scoped avoids making an explicit decision. Consider state, thread safety, disposal, cost, and the lifetimes of dependencies.

### Injecting scoped services into singletons

This creates a captive dependency. Redesign the lifetime relationship or create an explicit scope for each unit of work.

### Calling `BuildServiceProvider` during registration

Building a second container can create duplicate singleton instances and disconnected lifetimes. Prefer registration overloads that receive `IServiceProvider` when a factory needs another service.

### Injecting data as services

A shopping cart, current entity, or arbitrary DTO usually should not be registered in the container. Pass method data as method arguments. Use options patterns for configuration.

### Adding abstractions without a boundary

An interface is useful when it represents a stable capability, architectural boundary, or meaningful substitution. It is not a mandatory companion to every class.

## Interview-Oriented Questions

### What problem does constructor injection solve?

It makes required collaborators explicit and separates object use from object construction. That supports centralized configuration, substitution at boundaries, and valid object creation.

### What is the difference between dependency inversion and dependency injection?

Dependency inversion is a design principle about depending on abstractions rather than low-level details. Dependency injection is a construction technique that supplies dependencies from outside an object.

### What does scoped mean in ASP.NET Core?

A scoped registration produces one instance per DI scope. ASP.NET Core normally creates a scope per HTTP request, but other application models can define scopes differently.

### Why is a scoped dependency inside a singleton dangerous?

The singleton retains the scoped instance beyond its intended boundary, potentially sharing non-thread-safe state and delaying disposal. This is called a captive dependency.

### Should every service have an interface?

No. Introduce interfaces where they establish a useful contract or boundary. Injecting a concrete class is appropriate when substitution and dependency direction do not require an abstraction.

## Summary

Dependency injection moves dependency construction out of application classes and into a composition boundary. Constructor injection is the clearest default because it exposes required collaborators and creates valid objects from the start.

Use the .NET container deliberately:

- Transient services are created per resolution.
- Scoped services are shared within a scope, commonly an HTTP request.
- Singleton services live for the service provider and must be thread-safe.

Keep dependency injection distinct from the Dependency Inversion Principle, prevent longer-lived services from capturing shorter-lived ones, and resist abstraction that adds no real boundary. DI is most valuable when it makes a system's relationships easier—not merely more indirect.
