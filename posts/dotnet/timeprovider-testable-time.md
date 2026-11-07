---
title: ".NET TimeProvider: Deterministic Clocks, Delays, and Timeout Tests"
excerpt: "Use TimeProvider to make time-dependent .NET code explicit and test clocks, delays, timers, and expiry rules without waiting for wall-clock time."
category: ".NET"

seo:
  focusKeyword: ".NET TimeProvider testing"
  description: "Use .NET TimeProvider and FakeTimeProvider for deterministic clock, delay, timer, expiration, and timeout testing."
  socialTitle: ".NET TimeProvider: Test Time Without Sleeping"
  socialDescription: "Replace hidden wall-clock dependencies with explicit clocks and deterministic tests for expiry, delays, and timers."
---

# .NET TimeProvider: Deterministic Clocks, Delays, and Timeout Tests

A test creates a token that expires in five minutes, calls `Task.Delay`, and waits for the real clock. The suite becomes slow and intermittently fails when scheduling, daylight-saving rules, or machine load changes the timing.

> **Quick answer:** Inject `TimeProvider` into code that reads time, measures elapsed duration, or creates time-based waits. Use `TimeProvider.System` in production and `FakeTimeProvider` in tests. Advance fake time explicitly, keep timestamps separate from calendar time, and still test cancellation and concurrency around timer callbacks.

## Make Time an Explicit Dependency

`DateTimeOffset.UtcNow` is convenient but hidden. A service that calls it directly cannot reproduce an expiry boundary without changing the process clock or waiting.

Inject the framework abstraction instead:

```csharp
public sealed class DownloadGrantService(TimeProvider timeProvider)
{
    public DownloadGrant Issue(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        return new DownloadGrant(
            Guid.NewGuid(),
            issuedAt,
            issuedAt.Add(lifetime));
    }

    public bool IsExpired(DownloadGrant grant) =>
        timeProvider.GetUtcNow() >= grant.ExpiresAt;
}

public sealed record DownloadGrant(
    Guid Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
```

Register the production clock once:

```csharp
services.AddSingleton(TimeProvider.System);
```

`TimeProvider.System` supplies UTC and local time, high-frequency timestamps, and timers. Microsoft’s [TimeProvider overview](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview) documents those separate capabilities.

## Test Expiry Without Waiting

`FakeTimeProvider` is provided by the `Microsoft.Extensions.TimeProvider.Testing` package:

```csharp
using Microsoft.Extensions.Time.Testing;

var clock = new FakeTimeProvider(
    new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));
var service = new DownloadGrantService(clock);

DownloadGrant grant = service.Issue(TimeSpan.FromMinutes(5));

Assert.False(service.IsExpired(grant));

clock.Advance(TimeSpan.FromMinutes(5));

Assert.True(service.IsExpired(grant));
```

The test executes immediately and names the boundary precisely. The [FakeTimeProvider testing guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-testing) also supports setting time, automatic advancement, and timer-oriented tests.

Use a fresh fake per test unless shared progression is the behavior under test. A clock advanced by one test can silently change another test’s starting conditions.

## Use Calendar Time and Elapsed Time for Different Questions

`GetUtcNow()` answers a calendar question: what instant is it? It is appropriate for expirations, audit timestamps, and persisted deadlines.

Elapsed duration is different. System time can be corrected while a process runs. For latency and duration measurement, use the timestamp methods:

```csharp
long started = timeProvider.GetTimestamp();

await operation(cancellationToken);

TimeSpan elapsed = timeProvider.GetElapsedTime(started);
```

Do not persist the raw timestamp. It is meaningful only to the provider that produced it and is intended for interval measurement, not interchange.

## Make Delays Use the Same Clock

.NET provides time-provider-aware asynchronous APIs:

```csharp
public sealed class RetryPause(TimeProvider timeProvider)
{
    public Task WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
```

The test controls completion by advancing time:

```csharp
var clock = new FakeTimeProvider();
var pause = new RetryPause(clock);

Task waiting = pause.WaitAsync(
    TimeSpan.FromSeconds(30),
    CancellationToken.None);

Assert.False(waiting.IsCompleted);

clock.Advance(TimeSpan.FromSeconds(30));
await waiting;
```

The same model applies to `Task.WaitAsync` and time-provider-aware `CancellationTokenSource` construction in supported .NET versions. Keep every clock and delay inside one logical operation on the same provider; mixing fake calendar reads with real `Task.Delay` reintroduces nondeterminism.

## Test Cancellation Separately from Time Advancement

Advancing time proves the due-time path. It does not prove cancellation:

```csharp
var clock = new FakeTimeProvider();
using var cancellation = new CancellationTokenSource();

Task waiting = Task.Delay(
    TimeSpan.FromHours(1),
    clock,
    cancellation.Token);

cancellation.Cancel();

await Assert.ThrowsAnyAsync<OperationCanceledException>(
    async () => await waiting);
```

Production code should propagate the caller’s cancellation token. A fake clock is not a reason to omit shutdown behavior or request cancellation from tests.

## Treat Timer Callbacks as Concurrent Work

`TimeProvider.CreateTimer` creates an `ITimer`, but a controllable clock does not make callback code inherently single-threaded or reentrancy-safe.

```csharp
public sealed class LeaseMonitor : IDisposable
{
    private readonly ITimer _timer;

    public LeaseMonitor(TimeProvider timeProvider, Action inspect)
    {
        _timer = timeProvider.CreateTimer(
            _ => inspect(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    public void Dispose() => _timer.Dispose();
}
```

Dispose timers when their owner stops. If a callback may take longer than its period, decide whether overlap is allowed, skipped, or serialized. Tests should advance through several periods, exercise disposal, and verify behavior when the callback throws.

For asynchronous background work, a loop with `Task.Delay(..., timeProvider, token)` often makes cancellation and exception flow clearer than an async-void timer callback.

## Keep Time Zones at the Boundary

Store operational instants as `DateTimeOffset` in UTC. Convert to a user’s time zone when interpreting a local schedule or presenting a value.

`GetLocalNow()` uses the provider’s `LocalTimeZone`, which may be the host machine’s zone rather than the user’s. Do not make business rules depend accidentally on container configuration. Pass the relevant `TimeZoneInfo` when the rule truly concerns local civil time.

Test ambiguous and invalid local times around daylight-saving transitions. Advancing a UTC fake clock alone does not prove that a local scheduling rule handles those transitions correctly.

## Avoid a Homegrown Clock That Covers Only Half the Problem

An interface with only `UtcNow` can be sufficient for a small domain model. Once the application also needs controlled delays, timestamps, timers, or time zones, a narrow custom abstraction often leads to real waits beside fake calendar time.

`TimeProvider` provides one coherent framework boundary. Wrap it only when a domain-specific clock improves the model—for example, a trading calendar that answers whether a market is open—not merely to rename `GetUtcNow()`.

## Verify the Operational Boundary

Time-dependent code deserves tests for:

- the instant immediately before, at, and after expiry;
- cancellation before and during a wait;
- repeated timer periods and disposal;
- backward or forward calendar adjustments where custom providers permit them;
- daylight-saving and time-zone conversion rules;
- overflow and unreasonable duration inputs.

Replacing sleeps makes tests faster, but the larger benefit is design clarity. The code declares whether it needs calendar time, elapsed time, or a scheduler, and production and tests use the same contract.
