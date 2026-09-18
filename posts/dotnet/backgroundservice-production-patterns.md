---
title: ".NET BackgroundService: Scopes, Shutdown, and Failure Handling"
excerpt: "Build production-ready .NET hosted workers with scoped dependencies, cooperative shutdown, explicit failure behavior, and observable processing loops."
category: ".NET"

seo:
  focusKeyword: ".NET BackgroundService best practices"
  description: "Use .NET BackgroundService with correct DI scopes, cancellation, graceful shutdown, exception handling, and observable worker loops."
  socialTitle: ".NET BackgroundService: Production Patterns"
  socialDescription: "Design hosted workers that respect dependency lifetimes, stop predictably, and make failures visible."
---

# .NET BackgroundService: Scopes, Shutdown, and Failure Handling

`BackgroundService` makes it easy to put a loop inside a .NET host. The loop is rarely the difficult part. Production failures usually come from mismatched dependency lifetimes, swallowed exceptions, unbounded retries, or work that ignores shutdown.

> **Quick answer:** Keep the hosted service thin, create a dependency-injection scope for each meaningful unit of work, pass the stopping token through every wait, and decide deliberately whether an unhandled failure should stop the host. Graceful shutdown is a time budget, not a guarantee that every item will finish.

## A Hosted Service Is a Singleton

`AddHostedService<T>()` registers the hosted service with singleton lifetime. Injecting a scoped `DbContext` or application service directly into it creates a lifetime mismatch.

The [.NET hosted-services guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) documents the host lifecycle and the required scope pattern for background work.

Create a scope when processing begins and resolve the scoped handler from that scope:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public interface IJobSource
{
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

public interface IJobHandler
{
    Task HandleAsync(string jobId, CancellationToken cancellationToken);
}

public sealed class JobWorker(
    IJobSource source,
    IServiceScopeFactory scopeFactory,
    ILogger<JobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            string? jobId = await source.ReceiveAsync(stoppingToken);

            if (jobId is null)
            {
                continue;
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IJobHandler handler = scope.ServiceProvider
                .GetRequiredService<IJobHandler>();

            try
            {
                await handler.HandleAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker is stopping while processing {JobId}", jobId);
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Job {JobId} failed", jobId);
                throw;
            }
        }
    }
}
```

An asynchronous scope supports services that implement `IAsyncDisposable`. A scope per message also prevents tracked entities and other per-operation state from accumulating across the worker's lifetime.

[Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) explains why a scoped dependency cannot safely become part of a singleton's lifetime.

## Separate the Loop from the Unit of Work

The worker should own host lifecycle and message receipt. A scoped handler should own application behavior, transactions, and domain decisions. That separation makes the handler testable without starting a host and keeps infrastructure polling concerns out of business logic.

Scope size is a design decision. A scope per item gives strong isolation. A scope per batch can reduce setup work but shares a context and failure boundary across the batch. Do not keep one scope forever merely to avoid constructing services.

For a durable queue, success must also control acknowledgement. Delete or complete a message only after its business effect commits. [Reliable Background Processing on AWS with SQS and .NET](https://dev.jun-kim.net/2026/09/15/reliable-background-processing-on-aws-with-sqs-and-net/) covers visibility timeouts, redelivery, and idempotency at that boundary.

## Cancellation Must Reach Every Wait

The token passed to `ExecuteAsync` is signaled when the host starts graceful shutdown. Pass it to queue receives, database calls, HTTP requests, delays, and handlers. A loop that checks the token but then performs an uncancellable 60-second wait is not responsive.

Treat an `OperationCanceledException` caused by the stopping token as expected shutdown. Do not catch all cancellation exceptions indiscriminately: a separate per-operation timeout may indicate a dependency failure that deserves retry or alerting.

`StopAsync` waits for `ExecuteAsync` to complete, subject to the host's shutdown budget. Override it only when additional lifecycle behavior is required, and call `await base.StopAsync(cancellationToken)`. Cleanup that must survive process termination belongs in durable design, not only in `StopAsync`, because crashes and forced termination can skip graceful shutdown entirely.

## Choose an Exception Policy

An exception escaping `ExecuteAsync` is not a per-item retry strategy. In modern .NET hosts, the default background-service exception behavior stops the host. That is often safer than leaving a web process alive after a critical worker has died, but it should be an explicit operational decision.

There are three common levels:

| Failure | Typical response |
| --- | --- |
| Invalid message or permanent business rejection | Record the reason and dead-letter or reject according to the transport contract |
| Transient dependency failure | Retry with a bounded policy that preserves the message's idempotency contract |
| Broken invariant or unexpected worker failure | Let the failure surface, stop or restart the process, and alert |

Catching `Exception`, logging it, and immediately continuing can create a hot loop and a permanently unhealthy service. If the worker continues, it needs a bounded delay, an attempt policy, and a terminal destination for poison work.

Configure `HostOptions.BackgroundServiceExceptionBehavior` only when the alternative behavior is genuinely intended. Ignoring a stopped worker does not restore its loop.

## Startup and Readiness Are Different Questions

Do not put long blocking initialization in `StartAsync`; hosted services start as part of host startup. `ExecuteAsync` should become asynchronous promptly. A worker that depends on a database or broker should expose readiness according to the deployment's policy rather than retrying invisibly forever.

Fail-fast startup is appropriate when the process has no useful behavior without the dependency. Degraded startup may be appropriate when a web API can still serve other operations. Neither choice is universally correct; align probes and alerts with it so the orchestrator does not route traffic to a process that only appears healthy.

## Observability Should Describe Work

Log stable identifiers, attempt outcomes, and durations. Add metrics for receive failures, processing failures, retries, age of oldest work, and current backlog when the transport exposes it. Avoid logging complete payloads that may contain secrets or personal data.

Tracing should connect message receipt to outbound database and HTTP activity. Propagate a trace context only from a trusted format, and start a new activity when the incoming message has none.

Health checks should not execute the work itself. A liveness check answers whether the process should be restarted; readiness answers whether it should currently receive traffic or work. A temporary downstream outage should not automatically become a restart loop.

## Test the Failure Boundaries

Unit-test the scoped handler as an ordinary application service. For the worker boundary, use a fake source and a real service provider to verify that:

- each item receives a fresh scope;
- cancellation ends a blocked receive and an active handler;
- successful work is acknowledged only after completion;
- permanent and transient failures follow different paths;
- an unexpected exception produces the configured host behavior.

Also test shutdown in the deployment environment. Container stop grace periods, host shutdown timeout, message visibility, and handler duration must agree. A locally graceful loop can still be killed mid-transaction when the platform allows less time than the application expects.

`BackgroundService` is lifecycle plumbing. Reliability comes from aligning its scopes, cancellation, failure policy, and transport semantics with the unit of work the application actually performs.
