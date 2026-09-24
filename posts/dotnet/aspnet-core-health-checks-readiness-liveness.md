---
title: "ASP.NET Core Health Checks: Liveness, Readiness, and Dependency Failures"
excerpt: "Design ASP.NET Core health endpoints that distinguish process liveness from traffic readiness without turning transient dependency failures into restart loops."
category: ".NET"

seo:
  focusKeyword: "ASP.NET Core health checks"
  description: "Configure ASP.NET Core health checks for liveness, readiness, dependency timeouts, safe responses, and production probe behavior."
  socialTitle: "ASP.NET Core Health Checks: Liveness vs Readiness"
  socialDescription: "Keep unhealthy instances out of traffic without restarting healthy processes merely because a shared dependency is temporarily unavailable."
---

# ASP.NET Core Health Checks: Liveness, Readiness, and Dependency Failures

A database outage causes every application instance to fail its liveness probe. The orchestrator restarts all of them, adding connection storms and cold starts while the database remains unavailable. The probe detected a real dependency failure and prescribed the wrong recovery action.

Health checks are operational contracts. Each endpoint should answer one specific question for one specific caller.

> **Quick answer:** Keep liveness shallow so it answers whether the process should be restarted. Use readiness to decide whether an instance should receive traffic, including bounded checks for critical dependencies and startup state. Return minimal public details, enforce timeouts, and monitor dependencies separately from probe-driven restart policy.

## Define the Action Behind Each Probe

Liveness typically drives process restart. Readiness typically adds or removes an instance from traffic. A diagnostic endpoint may provide richer information to authenticated operators without controlling either action.

These meanings should remain separate:

| Signal | Question | Typical action |
| --- | --- | --- |
| Liveness | Is this process responsive enough to continue? | Restart the instance |
| Readiness | Can this instance safely serve normal traffic now? | Remove or add it to routing |
| Dependency monitoring | Is a database, queue, or provider healthy? | Alert, degrade, or invoke a service-specific response |

ASP.NET Core's [health check guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) describes separate readiness and liveness probes. The distinction matters because a restart cannot repair every failure.

## Tag Checks by Operational Meaning

Register a readiness check with an explicit timeout and map separate endpoints:

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck<OrdersDatabaseHealthCheck>(
        "orders-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));

WebApplication app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.Run();
```

The liveness endpoint runs no registered dependency checks. A successful response proves that the request pipeline can answer; it does not prove every application feature works.

The two-second timeout is illustrative. It must be shorter than the probe timeout and consistent with dependency latency. Probe intervals, failure thresholds, and deployment startup behavior belong in the hosting configuration, not just application code.

## Keep Dependency Checks Bounded and Cheap

A health check should perform the smallest operation that proves the required capability. For a database, that may be opening a connection and executing a lightweight command. It should not scan a business table, run migrations, or repair data.

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class OrdersDatabaseHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        string connectionString = configuration
            .GetConnectionString("Orders")
            ?? throw new InvalidOperationException(
                "Orders connection string is missing.");

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 1;
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database check timed out.");
        }
        catch (SqlException)
        {
            return HealthCheckResult.Unhealthy("Database check failed.");
        }
    }
}
```

The check avoids returning exception text, server names, or credentials. Application startup validation should catch missing required configuration before probes begin; the explicit exception prevents silently reporting a misleading dependency result.

A probe from every replica every few seconds can become meaningful database load. Consider a connection-only check, increase the interval, or monitor the dependency independently when the query adds little evidence.

## Readiness Can Represent Startup and Draining

An instance may be alive before it has loaded required reference data or warmed a local model. Register a singleton readiness state that a hosted service marks complete, and keep the instance out of routing until initialization succeeds.

Do not put unbounded startup work directly inside the health request. The hosted service owns retries and cancellation; the check reads its current state cheaply.

Shutdown needs similar coordination. The platform should stop routing new traffic before terminating the process, while the application stops accepting new long-running work and completes or checkpoints in-flight operations within its shutdown budget.

Readiness is not a global availability claim. If all instances depend on the same unavailable database, removing every instance from routing may be correct for endpoints that cannot function, but it can also hide a useful degraded response. Decide whether the application can still serve cached reads, static content, or a controlled `503`.

## Keep Probe Responses Safe

Detailed component names, exception messages, connection targets, and timing can help an attacker map infrastructure. Public or platform-facing probes should usually return only status and an appropriate HTTP code.

Expose richer diagnostics on a separately authenticated and network-restricted endpoint, or send detail to logs and metrics. Never include secrets or raw connection strings in health data.

Protect probe capacity as well. Bypass ordinary user authentication only where the hosting platform requires it, restrict hosts or networks when possible, and avoid expensive response writers. Ensure rate limiting or authorization middleware does not accidentally block the orchestrator.

## Test Failure Policy, Not Just Status Codes

Automated tests should verify tag selection, timeouts, startup transitions, cancellation, and safe response bodies. Deployment tests should verify the platform behavior:

- a deadlocked or terminated process is restarted;
- a database outage removes instances from traffic without causing a restart storm;
- startup remains unready until required initialization completes;
- a slow check finishes within the platform timeout;
- recovery returns instances to service gradually;
- rolling deployments receive enough startup time.

Monitor probe duration and outcomes, but avoid high-cardinality labels. Alert on sustained not-ready fleets and dependency failures rather than every single transient probe.

A useful health endpoint does not merely describe a failure. It causes the platform to take an action that improves recovery—or at least avoids making the incident worse.
