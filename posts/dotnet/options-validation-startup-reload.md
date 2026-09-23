---
title: ".NET Options Validation: Fail at Startup and Handle Reloads Deliberately"
excerpt: "Bind configuration to focused options, reject invalid settings before serving work, and choose explicit lifetime and reload behavior for production .NET services."
category: ".NET"

seo:
  focusKeyword: ".NET options validation"
  description: "Use .NET options validation with ValidateOnStart, cross-field rules, IOptions lifetimes, and deliberate configuration reload behavior."
  socialTitle: ".NET Options Validation: Startup and Reloads"
  socialDescription: "Catch deployment configuration errors early and prevent settings changes from splitting one operation across inconsistent policies."
---

# .NET Options Validation: Fail at Startup and Handle Reloads Deliberately

A service can deploy successfully and fail on its first real job because a configuration section is missing, a batch size is zero, or two timeout settings contradict each other. Binding configuration to a class makes access convenient, but does not establish that the values describe a usable service.

Configuration deserves a contract: required fields, valid combinations, when validation runs, and what happens if settings change while work is active.

> **Quick answer:** Bind related settings to a focused options type, validate both individual values and relationships, and use `ValidateOnStart()` for configuration required to operate. Choose an options lifetime intentionally, capture one validated policy per operation, and treat hot reload as a separate recovery design.

## Separate Binding from Validity

The configuration binder converts hierarchical keys into properties. Successful conversion is only the first gate. A value of `0` can bind to an integer while remaining an invalid batch size. A missing property can silently leave a default that nobody intended to approve.

Make required settings fail visibly and give defaults only to values for which a missing override is acceptable. Group options by the capability that consumes them; a single application-wide class lets unrelated features depend on each other's settings.

For a dispatcher, this illustrative configuration defines one batch and its timing policy:

```json
{
  "Dispatch": {
    "QueueName": "example-dispatch",
    "BatchSize": 10,
    "ProcessingTimeoutSeconds": 30,
    "VisibilityTimeoutSeconds": 60
  }
}
```

These are example operating limits, not recommended queue defaults. The transport adapter must still honor them, including renewal if work can exceed the visibility window.

## Validate Fields and Their Relationships

The following .NET 10 options class combines required text with bounded numeric values:

```csharp
using System.ComponentModel.DataAnnotations;

public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    [Required]
    public string QueueName { get; set; } = string.Empty;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 10;

    [Range(1, 300)]
    public int ProcessingTimeoutSeconds { get; set; } = 30;

    [Range(1, 3600)]
    public int VisibilityTimeoutSeconds { get; set; } = 60;
}
```

Register it in a Generic Host's `Program.cs`, with the class in a separate file:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<DispatchOptions>()
    .Bind(builder.Configuration.GetRequiredSection(DispatchOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => (long)options.VisibilityTimeoutSeconds
            >= (long)options.ProcessingTimeoutSeconds + 5,
        "Dispatch visibility must exceed processing timeout by at least five seconds.")
    .ValidateOnStart();

using IHost host = builder.Build();
await host.RunAsync();
```

A plain console project needs the appropriate `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Options.DataAnnotations` packages. ASP.NET Core's shared framework provides the corresponding APIs. Keep package versions aligned with the application's runtime.

`GetRequiredSection` detects a missing section. Data annotations check individual fields. The predicate checks a relationship that neither property's range can express. Widening to `long` before arithmetic prevents an extreme bound integer from overflowing while validation examines it.

Microsoft's [options guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/options) documents the binding and validation pipeline. For a more complex rule set, implement `IValidateOptions<T>` with specific failures rather than building a long Boolean expression.

Validation messages should name the rule and setting, without including secrets or the entire bound object. The configuration above contains no credentials; other option types may do so.

## Startup Validation Requires Host Startup

Without eager validation, invalid options can remain undiscovered until code first asks for their value. `ValidateOnStart()` moves that check to host startup. Constructing a service collection or calling `Build()` alone is not the same test as starting the host.

This distinction matters in deployment checks. A test that only resolves an unrelated service can pass while production startup fails. Start the host with intentionally invalid configuration and assert `OptionsValidationException`, then verify valid configuration starts successfully.

Keep validators deterministic and local. Checking that a queue name is present is configuration validation; contacting the queue service establishes a different fact about credentials, connectivity, and current availability. Put dependency checks in a deliberate startup or readiness policy with its own timeout and failure behavior.

A process that must not dispatch without valid settings should fail startup. An optional feature may instead need an explicitly disabled state. Do not silently replace invalid production values with convenient defaults after an exception.

## Choose When Consumers Observe Changes

The three common interfaces answer different lifetime questions:

| Interface | Value behavior | Typical fit |
| --- | --- | --- |
| `IOptions<T>` | Cached value without automatic configuration reload | Settings that change through restart |
| `IOptionsSnapshot<T>` | Value computed and cached per name within a DI scope | Scoped work that needs a consistent settings view |
| `IOptionsMonitor<T>` | Current values and change notifications | Long-lived consumers designed for reload |

`IOptionsSnapshot<T>` is scoped and should not be injected into a singleton. [Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) explains that lifetime boundary.

Even with a monitor, decide when a logical operation adopts new settings. Reading batch size before a reload and timeout afterward can combine values that never belonged to one validated configuration. Capture once, then copy into an immutable operation policy:

```csharp
using Microsoft.Extensions.Options;

public sealed record DispatchPolicy(
    string QueueName,
    int BatchSize,
    TimeSpan ProcessingTimeout,
    TimeSpan VisibilityTimeout);

public sealed class DispatchPolicyReader(IOptionsMonitor<DispatchOptions> monitor)
{
    public DispatchPolicy Capture()
    {
        DispatchOptions current = monitor.CurrentValue;
        return new DispatchPolicy(
            current.QueueName,
            current.BatchSize,
            TimeSpan.FromSeconds(current.ProcessingTimeoutSeconds),
            TimeSpan.FromSeconds(current.VisibilityTimeoutSeconds));
    }
}
```

Register this reader with DI if the dispatcher uses it. The host example does not start a queue worker. Its purpose is to demonstrate the configuration boundary independently of transport processing.

Treat options instances as read-only after creation even though binding uses settable properties. The copied record reduces the chance that an operation mutates shared configuration accidentally.

## Reload Is Not Automatic Recovery

`IOptionsMonitor` is not a promise to retain the last valid value after a bad update. Validation can throw when a replacement is created, including during change handling. `ValidateOnStart` does not turn subsequent reloads into safe fallback behavior.

Choose a policy before enabling reload. Restart-based configuration is often sufficient. If uninterrupted operation with the last known good configuration is required, implement an explicit candidate-validation and atomic-promotion layer, track its accepted version, and alert when an update is rejected. Do not assume a callback alone supplies those guarantees.

Reload also depends on the provider. JSON file watching can signal changes; editing an environment variable outside a running process does not make that process's environment configuration reload. Deployment-specific precedence and reload support are documented in Microsoft's [configuration provider guide](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers).

Some changes need more than a new options object. Updating a destination does not recreate every existing connection or undo an in-flight operation. Classify settings as restart-only, next-operation, or immediately effective, and keep that contract visible to operators.

## Test Configuration as a Deployment Input

Use in-memory configuration to exercise a missing section, whitespace-only queue name, out-of-range batch size, contradictory timing values, and a valid setup. Separately test that host startup actually runs validation. `Options.Create()` by itself does not run the registered validation pipeline.

For reloadable services, test a valid update and a rejected update while an operation is active. Verify which version that operation keeps and whether the next operation can proceed. Test provider reload behavior in the deployment environment too, because a unit test cannot establish filesystem notification behavior in a container.

Record configuration version and validation outcome in telemetry without dumping configuration values. Operators need to distinguish a bad rollout from an unavailable dependency; making both appear as an arbitrary first-request exception delays that diagnosis.
