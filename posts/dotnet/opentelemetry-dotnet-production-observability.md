---
title: "OpenTelemetry in .NET: Production Traces, Metrics, and Correlated Logs"
excerpt: "Instrument .NET services with OpenTelemetry while controlling cardinality, sampling, sensitive data, exporter failures, and cross-service correlation."
category: ".NET"

seo:
  focusKeyword: "OpenTelemetry .NET"
  description: "Configure OpenTelemetry in .NET with traces, metrics, correlated logs, OTLP export, sampling, cardinality controls, and production validation."
  socialTitle: "OpenTelemetry in .NET for Production Observability"
  socialDescription: "Collect useful traces and metrics without turning identifiers, payloads, or unbounded labels into an operational and privacy problem."
---

# OpenTelemetry in .NET: Production Traces, Metrics, and Correlated Logs

A service exports every request path with raw customer IDs, records exception bodies, and samples all traces. The dashboard looks detailed until telemetry cost spikes, secrets appear in search, and the collector drops data during the incident it was meant to explain.

OpenTelemetry provides interoperable plumbing. Useful observability still requires intentional signal design, bounded dimensions, and failure behavior.

> **Quick answer:** Instrument framework and application boundaries, export through OTLP, and correlate logs with the active trace. Keep metric attributes low-cardinality, sanitize span data, sample deliberately, and ensure telemetry export never blocks the request path. Validate dashboards and failure modes with realistic load before relying on them operationally.

## Give Each Signal a Job

Metrics show aggregate behavior and support alerts: request rate, errors, latency, queue depth, and resource saturation. Traces explain one distributed operation. Logs record discrete events that need searchable context.

Do not force one signal to imitate another. A metric tagged with `order.id` creates unbounded time series. A trace containing every debug statement becomes expensive noise. A log line without trace correlation makes cross-service diagnosis harder.

Microsoft's [.NET OpenTelemetry overview](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) maps framework APIs to the three signals: `ActivitySource` for traces, `Meter` for metrics, and `ILogger` for logs.

## Configure Resources and Export Once

This .NET web application registers ASP.NET Core and `HttpClient` tracing, runtime metrics, an application meter, and OTLP exporters:

```csharp
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

const string serviceName = "orders-api";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(OrderTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(OrderTelemetry.MeterName)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
});

WebApplication app = builder.Build();
app.MapGet("/orders/{id}", (string id) => Results.Ok(new { id }));
app.Run();
```

The example relies on the OpenTelemetry hosting, ASP.NET Core, `HttpClient`, runtime, and OTLP exporter packages. Pin compatible versions in the real project. Configure the endpoint and credentials through protected deployment configuration, not source code.

Use a stable service name and add environment, version, and instance metadata as resource attributes. Do not put per-request data on the resource; resources describe the process producing telemetry.

## Instrument Business Boundaries Deliberately

Framework instrumentation cannot know whether an operation reserved inventory or rejected an order. Add focused spans and metrics:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class OrderTelemetry
{
    public const string ActivitySourceName = "JunDevNotes.Orders";
    public const string MeterName = "JunDevNotes.Orders";

    public static readonly ActivitySource Activities = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> OrdersAccepted =
        Meter.CreateCounter<long>("orders.accepted");
}

public static class OrderOperations
{
    public static async Task AcceptOrderAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        using Activity? activity = OrderTelemetry.Activities
            .StartActivity("order.accept", ActivityKind.Internal);

        activity?.SetTag("order.id", orderId);
        await Task.Delay(10, cancellationToken);

        OrderTelemetry.OrdersAccepted.Add(1);
    }
}
```

An order ID can be useful on a sampled trace if policy permits it. It should not be a metric attribute. Prefer opaque identifiers over customer names or emails, and define retention and access controls.

Record exceptions with safe classifications. Do not attach request bodies, authorization headers, connection strings, prompts, or raw external responses by default.

## Control Cardinality Before Production

Route templates such as `/orders/{id}` are bounded; raw paths such as `/orders/847291` are not. The same rule applies to tenant IDs, query strings, exception messages, SQL text, and remote URLs.

Allowlist metric dimensions and estimate the product of their possible values. Ten regions times five outcomes is manageable. Millions of user IDs times thousands of paths is not.

Traces tolerate richer per-operation data because they are sampled and stored differently, but they still have cost and privacy limits. Attribute limits, span limits, and collector processors should complement application-side discipline rather than replace it.

## Sampling Changes What You Can Conclude

Head sampling decides near trace creation and keeps overhead predictable, but it cannot know that a later span will fail. Tail sampling can retain slow or failed traces after observing them, but it requires collector-side buffering and capacity.

Metrics should remain the primary source for complete rates and alerts. Do not calculate an error percentage from a biased set of retained error traces.

Propagate W3C trace context across trusted service boundaries. Validate inbound context and avoid accepting arbitrary baggage that becomes expensive or sensitive downstream. Messaging instrumentation must preserve causality without pretending every asynchronous consumer is one synchronous request.

## Treat the Collector as a Dependency With a Fail-Open Policy

Telemetry export must not make business requests fail. Use batching, bounded queues, short export timeouts, and a collector close to the workload. When the backend is unavailable, drop or buffer within a fixed budget rather than growing memory indefinitely.

Monitor the observability pipeline itself: export failures, dropped spans, queue utilization, collector CPU and memory, backend ingestion lag, and configuration version. A green application dashboard is not trustworthy if the exporter has been failing for an hour.

Test collector loss and slow export under load. Confirm shutdown flushes within the host budget without delaying termination indefinitely.

## Validate the Operational Questions

Before rollout, write the questions responders need to answer:

- Which endpoint and dependency explain the latency increase?
- Is the error isolated to one version or region?
- Did retries amplify downstream load?
- Can a trace be found from a safe correlation ID in a support case?
- Which telemetry is unavailable because of sampling or export loss?

Build dashboards and alerts from those questions, then rehearse an incident. OpenTelemetry is successful when the signals reduce uncertainty during failure—not merely when the collector receives data.
