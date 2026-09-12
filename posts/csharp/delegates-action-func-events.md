---
title: "C# Delegates, Action, Func, and Events: A Practical Guide"
excerpt: "Learn how C# delegates, Action, Func, lambdas, callbacks, and events fit together, with practical examples and common lifetime pitfalls."
category: "C#"

seo:
  focusKeyword: "C# delegates Action Func and events"
  description: "A practical guide to C# delegates, Action, Func, Predicate, method groups, multicast invocation, callbacks, events, and safe subscriptions."
  socialTitle: "C# Delegates, Action, Func, and Events"
  socialDescription: "Understand delegate signatures, callbacks, multicast behavior, and publisher/subscriber events with practical C# examples."
---

# C# Delegates, Action, Func, and Events: A Practical Guide

A method normally runs when its name appears in a call. A delegate lets code hold a reference to a compatible method and decide when to call it. This small change enables callbacks, configurable behavior, and event notifications without making the caller depend on a particular implementation.

> **Quick answer:** A delegate is a type-safe callable value. `Action` and `Func` are reusable delegate types, while lambdas and method groups are common ways to create delegate values. An `event` exposes controlled subscription to a delegate-backed notification.

## What a Delegate Is and Why It Exists

A delegate type specifies a parameter list and return type. A value of that type refers to one or more compatible methods. The compiler checks signatures, so a callback accepting an `int` cannot accidentally receive a method requiring a `Stream`.

Delegates decouple *when* behavior runs from *what* behavior does. A sorting routine can accept a comparison, a retry operation can accept work to execute, and a publisher can notify subscribers without knowing their classes. [Microsoft's delegate guide](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates) describes the underlying model.

### Declaring, assigning, and invoking a custom delegate

The following complete console example uses a custom type because `PriceRule` names a business role. A method group supplies the value; parentheses invoke it.

```csharp
PriceRule rule = AddTax; // Method group conversion
Console.WriteLine(rule(100m)); // 113.00

rule = subtotal => subtotal * 0.9m; // Lambda conversion
Console.WriteLine(rule.Invoke(100m)); // 90.0

static decimal AddTax(decimal subtotal) => subtotal * 1.13m;

public delegate decimal PriceRule(decimal subtotal);
```

`rule` can be reassigned because a delegate is a value. `rule(100m)` and `rule.Invoke(100m)` are equivalent. A nullable delegate needs a null check, commonly `callback?.Invoke(value)`. A custom delegate declaration defines a new type, even if another delegate type has an identical signature.

## Action, Func, and Predicate

Most callbacks do not need a new named type. The built-in generic delegates cover common signatures.

| Type | Signature | Typical use |
| --- | --- | --- |
| `Action` | No parameters, returns `void` | Run an operation |
| `Action<T>` | One parameter, returns `void` | Report a value |
| `Func<TResult>` | No parameters, returns a result | Supply a value lazily |
| `Func<T, TResult>` | One parameter, returns a result | Transform a value |
| `Predicate<T>` | One parameter, returns `bool` | Test a condition |

`Action<T1, T2>` and `Func<T1, T2, TResult>` extend the same idea to more inputs. In `Func`, the **last** type argument is always the return type.

```csharp
Action announce = () => Console.WriteLine("Starting");
Action<string> log = Console.WriteLine;
Func<DateTimeOffset> now = () => DateTimeOffset.UtcNow;
Func<decimal, decimal> addTax = amount => amount * 1.13m;
Func<decimal, decimal, decimal> add = (left, right) => left + right;
Predicate<int> isEven = number => number % 2 == 0;

announce();
log($"Total: {add(addTax(100m), 5m)}");
Console.WriteLine(isEven(12));
Console.WriteLine(now());
```

A lambda is an expression or statement body that can be converted to a compatible delegate. A **method group** such as `Console.WriteLine` refers to one or more named method overloads; the target delegate signature selects a compatible overload. Lambdas can capture local variables, extending their lifetime while the delegate remains reachable. Capture a stable local value when a deferred callback must see a snapshot rather than a later mutation.

`Predicate<T>` is useful where an API specifically requests it, such as `List<T>.Find`. Many LINQ operators instead accept `Func<T, bool>`. These have similar call shapes but are distinct delegate types.

## Passing Delegates as Parameters and Callbacks

A callback is a delegate passed to another operation so that operation can call back at an appropriate moment. The following example uses a predicate to select items and an action to report each selection.

```csharp
static void Process<T>(
    IEnumerable<T> items,
    Predicate<T> shouldProcess,
    Action<T> onSelected)
{
    foreach (T item in items)
    {
        if (shouldProcess(item))
        {
            onSelected(item);
        }
    }
}

Process(
    new[] { 3, 8, 12 },
    number => number >= 8,
    number => Console.WriteLine($"Selected: {number}"));
```

This is useful when the caller owns a small policy and the callee owns the workflow. A delegate parameter should represent a clear extension point; if several callbacks must coordinate complex state, an interface or dedicated service may communicate the contract better.

For asynchronous callbacks, use a task-returning delegate such as `Func<CancellationToken, Task>`, and **await** the returned task. `async void` callbacks hide completion and complicate exception handling; `async void` is primarily for event-handler signatures that require `void`.

## Multicast Delegates: Order, Results, and Exceptions

Delegates can have an invocation list. Combining them with `+=` appends a handler; `-=` removes a matching occurrence. A normal multicast invocation runs handlers synchronously in invocation-list order.

```csharp
Action<string> notify = message => Console.WriteLine($"A: {message}");
notify += message => Console.WriteLine($"B: {message}");
notify("Ready"); // A, then B
```

If a multicast delegate returns a value, a normal invocation returns **only the result from the last handler that successfully completes the invocation**. Earlier return values are discarded. In practice, using multicast delegates to calculate one result is confusing; use a single callback or explicitly iterate the invocation list if every result matters.

```csharp
Func<int> calculate = () => 1;
calculate += () => 2;
Console.WriteLine(calculate()); // 2
```

An unhandled exception from one handler **stops that invocation**. Later handlers are not called, and the exception propagates to the caller. The same rule applies to ordinary event raising. If every handler must be attempted, the publisher must intentionally enumerate `GetInvocationList()`, catch exceptions per handler, and define how failures are reported. That is a separate policy, not default multicast behavior.

Delegate instances are immutable: `+=` and `-=` produce an updated value rather than altering an existing delegate object. They do not make side effects inside handlers atomic or thread-safe.

## Events and the Publisher/Subscriber Pattern

An event models a notification owned by a **publisher**. Subscribers register handlers, and the publisher raises the event. The `event` keyword restricts outside code to subscribing and unsubscribing; outside code cannot directly invoke or replace a field-like event. [Microsoft's event documentation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/) explains the publisher/subscriber model.

```csharp
var sensor = new TemperatureSensor();
EventHandler<TemperatureChangedEventArgs> handler = (_, e) =>
    Console.WriteLine($"Temperature: {e.Celsius} C");

sensor.TemperatureChanged += handler;
sensor.Record(21.5m);
sensor.TemperatureChanged -= handler;

public sealed class TemperatureChangedEventArgs(decimal celsius) : EventArgs
{
    public decimal Celsius { get; } = celsius;
}

public sealed class TemperatureSensor
{
    public event EventHandler<TemperatureChangedEventArgs>? TemperatureChanged;

    public void Record(decimal celsius)
    {
        TemperatureChanged?.Invoke(
            this,
            new TemperatureChangedEventArgs(celsius));
    }
}
```

`EventHandler<TEventArgs>` follows a familiar .NET convention: sender plus event data. The publisher controls when to raise the event and what data to supply. Subscribers should keep handlers short and handle errors deliberately because an exception can interrupt notification and propagate through `Record`.

### Delegate versus event

| Delegate member | Event member |
| --- | --- |
| Callers with access can invoke it | Only the declaring type can raise a field-like event |
| Callers can assign or replace it if writable | Outside callers can normally only use `+=` and `-=` |
| Suitable for a requested operation or callback | Suitable for a publisher-owned notification |

An event uses a delegate type for its handlers, but it is not merely a public delegate field. Events can also have custom add/remove accessors, so their storage need not be a simple field.

### Subscription lifetime is part of correctness

The publisher normally holds references to subscribed delegates. An instance-method handler refers to its target object; a capturing lambda may refer to captured objects. If a long-lived publisher keeps a short-lived subscriber's handler, that subscriber can remain reachable even after the rest of the application stops using it.

Unsubscribe when ownership ends, especially for static events, singleton services, or application-wide message sources. Store a lambda in a variable if it must later be removed: writing the same-looking lambda expression again creates a different delegate instance. A short-lived publisher that dies with its subscribers usually poses no such retention problem.

For more involved lifetimes, a subscriber can implement `IDisposable` to detach its handlers. Weak-event patterns exist for special cases, but explicit lifetime ownership is easier to reason about when feasible.

## Choosing a Delegate Type and Avoiding Common Mistakes

Use `Action` or `Func` for a local, obvious callback signature. Use a custom delegate when a meaningful name improves a public API, when attributes or parameter names matter to the contract, or when the signature expresses a domain concept. Use `EventHandler<TEventArgs>` for a conventional .NET event unless a different established contract is required.

Common mistakes include calling a nullable delegate without a null check, forgetting to await a task-returning callback, relying on every multicast handler to run after one throws, expecting all multicast return values, and leaving subscriptions attached to a long-lived publisher. Another mistake is exposing a writable delegate field where callers should only subscribe to a notification.

Keep callbacks small and document whether they are synchronous, may throw, and can be called more than once. These details matter more to callers than the choice between a lambda and a named method.

## Interview Questions

**How is `Func<T, bool>` different from `Predicate<T>`?** Both describe a boolean test, but they are different delegate types used by different APIs.

**What does a multicast `Func<int>` return?** The value returned by the last handler when the invocation completes normally; earlier results are discarded.

**Will every event subscriber run if one throws?** No. A normal synchronous invocation stops at the unhandled exception.

**Why can an event subscription retain an object?** The publisher keeps the handler delegate reachable, and the delegate can keep its target or captured objects reachable.

**When is a custom delegate better than `Action`?** When the public contract benefits from a domain-specific name or signature metadata.

## Summary

Delegates make behavior a typed value. `Action`, `Func`, and `Predicate` cover common signatures; lambdas and method groups create values for those types. Events add controlled subscription for publisher-owned notifications. Choose the smallest clear contract, define exception behavior, and match subscription lifetime to object lifetime.
