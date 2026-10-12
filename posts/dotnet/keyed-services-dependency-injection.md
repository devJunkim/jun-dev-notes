---
title: ".NET Keyed Services: Multiple Implementations Without Service Locator Sprawl"
excerpt: "Use keyed dependency injection in .NET while keeping keys centralized, lifetimes valid, selection logic testable, and missing registrations visible."
category: ".NET"

seo:
  focusKeyword: ".NET keyed services"
  description: "Use .NET keyed services for multiple implementations with explicit keys, safe lifetimes, testable selection, and predictable failure behavior."
  socialTitle: ".NET Keyed Services Without Service Locator Sprawl"
  socialDescription: "Resolve multiple implementations deliberately while preserving dependency visibility, lifetime safety, and maintainable selection rules."
---

# .NET Keyed Services: Multiple Implementations Without Service Locator Sprawl

An application needs two implementations of the same interface: perhaps standard and expedited pricing, regional storage, or sandbox and production transports. Registering both is easy; selecting one without scattering conditionals and magic strings is the real design problem.

> **Quick answer:** Use keyed services when the set of implementations is known at composition time and the key is part of application policy. Centralize keys, preserve normal dependency-injection lifetimes, inject a fixed keyed dependency where possible, and isolate runtime selection behind a small typed resolver.

## Register Keys at the Composition Root

The built-in .NET container supports keyed singleton, scoped, and transient registrations:

```csharp
using Microsoft.Extensions.DependencyInjection;

public interface IReceiptFormatter
{
    string Format(decimal total);
}

public sealed class TextReceiptFormatter : IReceiptFormatter
{
    public string Format(decimal total) => $"Total: {total:C}";
}

public sealed class JsonReceiptFormatter : IReceiptFormatter
{
    public string Format(decimal total) => $"{{\"total\":{total}}}";
}

public static class ReceiptFormats
{
    public const string Text = "text";
    public const string Json = "json";
}

ServiceCollection services = new();
services.AddKeyedSingleton<IReceiptFormatter, TextReceiptFormatter>(ReceiptFormats.Text);
services.AddKeyedSingleton<IReceiptFormatter, JsonReceiptFormatter>(ReceiptFormats.Json);
```

The [official dependency-injection documentation](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/) notes that keys can be objects, not only strings. Stable constants or a small enum reduce typing errors. Avoid using arbitrary request text directly as a DI key; translate external values into an approved internal choice first.

## Prefer Fixed Injection When the Choice Is Static

When a consumer always needs one implementation, make that dependency visible:

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class AuditReceiptWriter(
    [FromKeyedServices(ReceiptFormats.Json)] IReceiptFormatter formatter)
{
    public string Write(decimal total) => formatter.Format(total);
}
```

This keeps construction declarative and prevents business code from reaching into `IServiceProvider`. A missing key fails during resolution rather than producing an unexplained branch later.

Validate the container during startup where possible. Also exercise representative resolutions in tests because validation cannot prove that every runtime key from configuration or persisted data is valid.

## Isolate Runtime Selection

Sometimes the key depends on a trusted business value. Keep the lookup at one boundary:

```csharp
public enum ReceiptFormat
{
    Text,
    Json
}

public sealed class ReceiptFormatterResolver(IServiceProvider services)
{
    public IReceiptFormatter Resolve(ReceiptFormat format) => format switch
    {
        ReceiptFormat.Text => services.GetRequiredKeyedService<IReceiptFormatter>(ReceiptFormats.Text),
        ReceiptFormat.Json => services.GetRequiredKeyedService<IReceiptFormatter>(ReceiptFormats.Json),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
```

The resolver is intentionally small. Domain services depend on it rather than on the general-purpose container, and its mapping can be unit tested. If selection needs complex eligibility, fallback, or tenant policy, model that policy explicitly instead of turning keys into a hidden rules engine.

## Lifetimes Still Apply

Keys do not weaken lifetime rules. A singleton cannot safely capture a scoped keyed service. A keyed singleton with mutable state must still be thread-safe. Transient disposable services resolved from the root provider can still live until the root container is disposed.

Create a scope for scoped work and resolve inside it:

```csharp
await using AsyncServiceScope scope = services.BuildServiceProvider().CreateAsyncScope();
IReceiptFormatter formatter = scope.ServiceProvider
    .GetRequiredKeyedService<IReceiptFormatter>(ReceiptFormats.Json);
```

The standalone snippet demonstrates scope ownership; production code should build the root provider once through the host and should not call `BuildServiceProvider` during service registration.

## Decide Missing-Key Behavior Explicitly

`GetRequiredKeyedService` throws when registration is missing. That is appropriate for a configuration error. `GetKeyedService` returns `null`, which can support an optional capability if absence is expected and handled visibly.

Do not silently fall back from an unknown payment, storage, or authorization strategy. A default that is safe for formatting may be unsafe for security-sensitive behavior. Record the chosen strategy in telemetry without logging secrets or tenant-sensitive configuration.

.NET also supports `KeyedService.AnyKey` for specific fallback and enumeration scenarios. Its behavior is specialized, and .NET 10 rejects using `AnyKey` with singular `GetKeyedService`. Prefer explicit registrations unless a wildcard policy is genuinely required.

## Know When a Dictionary Is Simpler

Keyed DI is useful when implementations have dependencies and container-managed lifetimes. A dictionary of pure strategies may be simpler when objects are already constructed, selection is data driven, or callers need to enumerate capabilities.

Use separate interfaces when implementations represent materially different responsibilities. A shared interface plus keys should express interchangeable behavior, not erase important domain distinctions.

Keyed services remove registration boilerplate, but maintainability still comes from visible choices, centralized policy, and tests that prove every supported key resolves to the intended implementation.
