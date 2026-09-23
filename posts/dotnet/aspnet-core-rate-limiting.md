---
title: "Rate Limiting in ASP.NET Core: Policies, Partitions, and Production Trade-Offs"
excerpt: "Apply ASP.NET Core rate limiting with bounded queues, trustworthy partition keys, useful 429 responses, and deployment-aware capacity controls."
category: ".NET"

seo:
  focusKeyword: "ASP.NET Core rate limiting"
  description: "Configure ASP.NET Core rate limiting policies, partitions, queues, 429 responses, metrics, and multi-instance production behavior."
  socialTitle: "ASP.NET Core Rate Limiting in Production"
  socialDescription: "Design fair rate limits with stable identities, bounded queues, clear rejection behavior, and controls that match your deployment topology."
---

# Rate Limiting in ASP.NET Core: Policies, Partitions, and Production Trade-Offs

A global limit of 100 requests per minute sounds protective until one busy customer consumes it for everyone. A per-IP limit sounds fair until thousands of users share a corporate proxy—or an attacker sends unbounded fake forwarding headers.

Rate limiting is a resource-allocation policy. The algorithm matters, but the partition key, deployment topology, queue behavior, and response contract usually matter more.

> **Quick answer:** Apply named policies to expensive endpoints, partition by a bounded and authenticated identity when possible, keep queues small, and return a predictable `429` response with `Retry-After` when available. Treat in-process limits as per-instance controls unless a shared gateway enforces the global budget.

## Start with the Resource You Are Protecting

Different endpoints exhaust different resources. A cached health response and a report export should not share an arbitrary request count. Define the protected capacity first: concurrent database queries, outbound provider quota, CPU-heavy conversions, or account-level business operations.

ASP.NET Core provides fixed-window, sliding-window, token-bucket, and concurrency limiters. Time-based algorithms control arrival rate. A concurrency limiter caps simultaneous work and releases its permit when the request completes. It does not directly cap requests per minute.

The framework's [rate limiting guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) recommends load testing before deployment. Limits copied from an example cannot establish the capacity of a real dependency.

## Configure an Explicit Endpoint Policy

This .NET 10 minimal API applies a token-bucket policy to a report endpoint:

```csharp
using System.Globalization;
using System.Threading.RateLimiting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(
            MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds)
                    .ToString(CultureInfo.InvariantCulture);
        }

        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Request rate limit exceeded")
            .ExecuteAsync(context.HttpContext);
    };

    options.AddPolicy("report-export", httpContext =>
    {
        string partition = httpContext.User.FindFirst("sub")?.Value
            ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(
            partition,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                TokensPerPeriod = 2,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    });
});

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/reports/export", () => Results.Accepted())
    .RequireAuthorization()
    .RequireRateLimiting("report-export");

app.Run();
```

The numbers are illustrative. The policy permits a short burst, replenishes capacity over time, and bounds waiting requests. Authentication runs before the limiter because the partition uses an authenticated claim. Middleware order must match the data the policy needs.

Avoid returning internal capacity details or account identifiers in the rejection body. A stable problem response and optional `Retry-After` are enough for most clients.

## Choose Partition Keys as a Security Decision

A partition gives each key its own limiter. Good keys are stable, bounded, and aligned with the protected resource. An authenticated tenant or subject identifier is often more meaningful than an IP address.

Do not partition directly on arbitrary user input. Each distinct key can create cached limiter state, so unbounded attacker-controlled values can become a memory-denial vector. Validate API keys before using their owner ID, and bound any anonymous fallback strategy.

IP-based limits are useful as one abuse signal, but proxy handling must be configured from trusted networks. Never trust an arbitrary `X-Forwarded-For` value simply because it is present. NAT, mobile carriers, and IPv6 privacy addresses also make one-IP-per-user assumptions unreliable.

Layering can be appropriate: a coarse anonymous limit, an authenticated account limit, and a concurrency cap around the constrained dependency. When chaining policies, test how leases and time-based permits behave when a later limiter rejects the request.

## A Queue Is Still Load

Queueing smooths a small burst, but queued requests consume connections, memory, deadlines, and user patience. A queue limit of zero is often appropriate for interactive APIs whose clients can retry. If a small queue is justified, ensure the request timeout exceeds the maximum useful wait and propagates cancellation to downstream work.

`OldestFirst` is intuitive for fairness. `NewestFirst` can discard older queued requests when new ones arrive, which may be useful only when freshness matters more than completion. Neither fixes an overloaded dependency.

For long-running jobs, prefer accepting a durable job after admission and processing it asynchronously. Holding an HTTP request in a rate-limiter queue is not durable scheduling.

## Understand the Multi-Instance Boundary

The built-in limiter stores counters in the application process. With four replicas, a per-instance limit of ten concurrent requests can allow roughly forty across the service. Restarts also reset local time-based state.

That may be correct when each replica protects its own CPU or connection pool. It is not a global customer quota. Enforce fleet-wide budgets at a gateway or another shared control designed for distributed coordination, and decide how that layer behaves during partial failure.

Do not add a distributed counter casually to every request. Network latency, consistency, expiry, and failure behavior become part of the request path. Often the clean design is a coarse edge quota plus an in-process concurrency limit close to the resource.

## Make Limits Observable and Testable

Track accepted, queued, and rejected requests by policy and endpoint. Keep partition identifiers out of high-cardinality metrics; use secure diagnostic logs only when the operational need justifies them. Correlate rejections with latency, dependency saturation, instance count, and client retry volume.

Tests should cover:

- the burst boundary and replenishment behavior;
- distinct authenticated partitions;
- anonymous and malformed credentials;
- queue overflow and cancellation;
- `429` response shape and `Retry-After`;
- multiple replicas or the gateway policy that supplies a global limit;
- client retries with jitter and a finite deadline.

Rate limiting reduces overload and improves fairness. It is not authentication, authorization, request validation, or complete denial-of-service protection. Keep those controls separate and make the capacity policy visible to the operators who must tune it.
