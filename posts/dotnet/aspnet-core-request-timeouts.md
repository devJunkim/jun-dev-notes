---
title: "ASP.NET Core Request Timeouts: Policies, Cancellation, and Deadline Budgets"
excerpt: "Configure ASP.NET Core request timeout policies, propagate cancellation, and coordinate proxy and dependency deadlines without pretending timed-out work was undone."
category: ".NET"

seo:
  focusKeyword: "ASP.NET Core request timeouts"
  description: "Configure ASP.NET Core request timeout policies and propagate deadline cancellation safely across endpoints and dependencies."
  socialTitle: "ASP.NET Core Request Timeouts and Deadline Budgets"
  socialDescription: "Bound request duration while preserving cancellation, response, and downstream-operation semantics."
---

# ASP.NET Core Request Timeouts: Policies, Cancellation, and Deadline Budgets

A request can exceed the time a client, proxy, or user is willing to wait while the server continues consuming connections and dependency capacity. One global number rarely fits interactive endpoints, uploads, streaming responses, and batch operations.

> **Quick answer:** Configure named timeout policies by workload, place the middleware correctly, and propagate `HttpContext.RequestAborted` into every cancellable dependency. Treat timeout as a deadline signal—not proof that database, queue, or remote side effects were rolled back.

## Configure Policies Deliberately

ASP.NET Core provides request-timeout middleware with global and per-endpoint policies:

```csharp
using Microsoft.AspNetCore.Http.Timeouts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("interactive", TimeSpan.FromSeconds(3));
    options.AddPolicy("report", TimeSpan.FromSeconds(30));
});

var app = builder.Build();
app.UseRequestTimeouts();

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    IOrderReader orders,
    CancellationToken cancellationToken) =>
{
    Order? order = await orders.FindAsync(id, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
}).WithRequestTimeout("interactive");
```

Microsoft's [request-timeout middleware documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts) notes that registering middleware does not activate a limit until a policy is configured. When routing is explicit, place `UseRequestTimeouts` after `UseRouting`.

## Propagate Cancellation Through the Call Graph

The timeout signals `HttpContext.RequestAborted`; it does not forcibly terminate managed code. Database, HTTP, stream, and queue calls must receive the token.

Avoid replacing the request token with an unrelated fixed timeout deep in the stack. If a dependency needs a shorter budget, link tokens and choose a deadline that leaves time to format a response and release resources.

CPU-bound work must check cancellation at safe intervals. Blocking calls that ignore tokens can outlive the request and continue consuming a worker thread.

## Coordinate All Deadline Layers

A production request may have deadlines in the browser, CDN, load balancer, reverse proxy, ASP.NET Core, outbound client, and database. The outer layer should usually allow slightly more time than the application budget; otherwise the connection disappears before the application can return its intended timeout response.

Do not automatically retry a timed-out write. The remote system may have committed it before the response was lost. Use idempotency keys, status lookup, or reconciliation for consequential operations.

## Long-Lived Endpoints Need Different Treatment

Streaming downloads, server-sent events, and WebSockets should not inherit a short interactive policy blindly. Disable or replace the policy only after defining idle timeouts, connection limits, heartbeat behavior, and shutdown handling elsewhere.

Uploads need separate limits for body size, minimum data rate, and total processing time. A timeout alone does not protect memory or disk from oversized bodies.

## Make Timeout Outcomes Observable

Track timeout counts by route template and policy, not raw URL. Record total duration and which dependency was active without logging sensitive request data. A rise in timeouts can indicate saturation, a slow dependency, a budget that is too small, or clients abandoning work early.

Test without an attached debugger because timeout behavior can be disabled during debugging. Verify cooperative cancellation, late dependency completion, response status, client disconnects, and shutdown. A timeout policy is effective only when the entire operation respects the deadline and remains recoverable after uncertainty.
