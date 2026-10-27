---
title: "ChurchServe Angular Tutorial Part 26-2: OpenTelemetry and Production Operations"
excerpt: "Instrument ChurchServe with structured JSON logs, ASP.NET Core traces, runtime metrics, an optional OTLP exporter, and release-level operational checks."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core OpenTelemetry tutorial"
  description: "Configure ASP.NET Core OpenTelemetry traces, metrics, structured logs, an optional OTLP exporter, and practical production verification."
  socialTitle: "ChurchServe Part 26-2: OpenTelemetry Operations"
  socialDescription: "Complete ChurchServe with vendor-neutral traces, metrics, JSON logs, safe browser signals, and an operational release checklist."
---

# ChurchServe Angular Tutorial Part 26-2: OpenTelemetry and Production Operations

Part 26-1 created a privacy-safe browser telemetry boundary and carried correlation IDs through ASP.NET Core. Those events are useful only if operators can aggregate them with server request traces, runtime health, and structured logs.

This final tutorial chapter uses OpenTelemetry's vendor-neutral .NET APIs. Local development keeps JSON console logs and makes no remote connection by default. A deployment opts into OTLP export through standard configuration, so ChurchServe is not coupled to one monitoring vendor.

> **Quick answer:** Register one service resource, instrument ASP.NET Core requests and .NET runtime metrics, create low-cardinality ChurchServe counters and histograms, include trace and correlation context in structured logs, and add OTLP exporters only when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.

## Files Added and Updated

```text
server/ChurchServe.Api/
  ChurchServe.Api.csproj                             updated
  Observability/
    ChurchServeDiagnostics.cs                       new
    ObservabilityExtensions.cs                      new
  Program.cs                                        updated
tests/ChurchServe.Api.Tests/
  ObservabilityEndpointsTests.cs                    new
tools/verify-release.mjs                            updated
README.md                                           updated
```

OpenTelemetry separates instrumentation from the backend that stores and visualizes data. The [.NET observability guidance from Microsoft](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) describes logs, metrics, and distributed tracing as the three pillars. The [OpenTelemetry .NET instrumentation documentation](https://opentelemetry.io/docs/languages/dotnet/instrumentation/) distinguishes framework instrumentation from custom application signals.

## Add the OpenTelemetry Packages

The API uses ASP.NET Core and runtime instrumentation plus the OTLP exporter. Keep these package versions aligned when upgrading.

```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.20.1" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.20.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.20.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.20.1" />
```

Instrumentation packages evolve independently of .NET. Recheck current stable versions and release notes instead of assuming the tutorial versions remain current.

## Create Low-Cardinality Application Metrics

ASP.NET Core instrumentation already measures HTTP requests. ChurchServe adds an accepted-browser-event counter and a browser-duration histogram. Their tags come only from the allowlists validated in Part 26-1.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ChurchServe.Api.Observability;

public sealed class ChurchServeDiagnostics : IDisposable
{
  public const string ActivitySourceName = "ChurchServe.Api";
  public const string MeterName = "ChurchServe.Api";

  public ActivitySource Activities { get; } = new(ActivitySourceName);

  private readonly Meter meter = new(MeterName);
  private readonly Counter<long> browserEvents;
  private readonly Histogram<double> browserDurations;

  public ChurchServeDiagnostics()
  {
    browserEvents = meter.CreateCounter<long>(
        "churchserve.browser.events",
        description: "Accepted privacy-safe browser telemetry events.");
    browserDurations = meter.CreateHistogram<double>(
        "churchserve.browser.duration",
        unit: "ms",
        description: "Browser-observed operation duration.");
  }

  public void RecordBrowserEvent(BrowserTelemetryRequest request)
  {
    var tags = new TagList
    {
      { "event.name", request.Name },
      { "operation", request.Operation },
      { "outcome", request.Outcome },
    };
    browserEvents.Add(1, tags);
    if (request.DurationMs > 0)
    {
      browserDurations.Record(request.DurationMs, tags);
    }
  }

  public void Dispose()
  {
    Activities.Dispose();
    meter.Dispose();
  }
}
```

Do not add correlation IDs to metric tags. They are nearly unique and would create unbounded time-series cardinality. Correlation belongs in logs and traces, where an operator retrieves a particular request.

## Configure All Three Signals

The complete extension keeps observability setup out of `Program.cs` and uses normal host configuration.

```csharp
using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ChurchServe.Api.Observability;

public static class ObservabilityExtensions
{
  private const string ServiceName = "ChurchServe.Api";

  public static WebApplicationBuilder AddChurchServeObservability(
      this WebApplicationBuilder builder)
  {
    var serviceVersion = builder.Configuration["PublicRuntime:Release"] ?? "unknown";
    var hasOtlpExporter = !string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    if (!builder.Environment.IsEnvironment("Testing"))
    {
      builder.Logging.ClearProviders();
      builder.Logging.AddJsonConsole(options =>
      {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
      });
    }
    builder.Logging.Configure(options =>
    {
      options.ActivityTrackingOptions =
          ActivityTrackingOptions.TraceId |
          ActivityTrackingOptions.SpanId |
          ActivityTrackingOptions.ParentId;
    });

    if (hasOtlpExporter)
    {
      builder.Logging.AddOpenTelemetry(options =>
      {
        options.IncludeScopes = true;
        options.ParseStateValues = true;
        options.SetResourceBuilder(CreateResource(serviceVersion));
        options.AddOtlpExporter();
      });
    }

    builder.Services.AddSingleton<ChurchServeDiagnostics>();
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource =>
            resource.AddService(ServiceName, serviceVersion: serviceVersion))
        .WithTracing(tracing =>
        {
          tracing
              .AddSource(ChurchServeDiagnostics.ActivitySourceName)
              .AddAspNetCoreInstrumentation(options =>
              {
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health/live");
              });
          if (hasOtlpExporter) tracing.AddOtlpExporter();
        })
        .WithMetrics(metrics =>
        {
          metrics
              .AddMeter(ChurchServeDiagnostics.MeterName)
              .AddAspNetCoreInstrumentation()
              .AddRuntimeInstrumentation();
          if (hasOtlpExporter) metrics.AddOtlpExporter();
        });

    return builder;
  }

  private static ResourceBuilder CreateResource(string serviceVersion) =>
      ResourceBuilder.CreateDefault()
          .AddService(ServiceName, serviceVersion: serviceVersion);
}
```

The resource gives all signals the same stable service name and release version. Liveness traffic is excluded from traces because frequent probes can overwhelm useful requests, although aggregate HTTP metrics remain. Tests keep their test logger rather than replacing it with JSON output.

Register the extension immediately after creating the builder:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddChurchServeObservability();
```

## Run Without or With a Collector

Ordinary local work needs no collector:

```bash
dotnet run --project server/ChurchServe.Api
```

The API writes structured JSON logs. It creates activities and measurements, but without an exporter it makes no remote connection.

To export logs, traces, and metrics to an OpenTelemetry Collector or compatible backend, configure the standard endpoint:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project server/ChurchServe.Api
```

Authentication headers, transport security, retention, and access control belong in deployment configuration. Never put exporter credentials in Angular runtime configuration because that file is public to every browser.

## Build a Small Operational View

Start with questions rather than a large dashboard:

| Signal | Dimensions | Question |
| --- | --- | --- |
| API requests | route, status, release | Did a release increase failures? |
| API duration | route, release | Which server operation became slow? |
| Browser outcomes | operation, outcome | What did users experience? |
| Browser duration | operation, outcome | Is perceived latency worse than server latency? |
| Runtime metrics | process, GC, thread pool | Is infrastructure pressure involved? |

Alert on sustained user impact rather than every isolated exception. Each alert needs an owner, severity, runbook, and resolution condition.

Correlation provides the drill-down path:

1. A user reports the safe Problem Details reference.
2. Support searches structured logs for `CorrelationId`.
3. The log includes trace and span identifiers.
4. The trace shows the server request and its timing.
5. Aggregate metrics show whether the incident is isolated or systemic.

## Verify the Release Artifact

The release smoke test now checks that published API and runtime-configuration responses contain valid correlation headers:

```javascript
assert.match(
  missingApi.headers.get('x-correlation-id') ?? '',
  /^[A-Za-z0-9_-]{8,64}$/,
);
assert.match(
  runtime.headers.get('x-correlation-id') ?? '',
  /^[A-Za-z0-9_-]{8,64}$/,
);
```

Run the complete milestone verification:

```bash
dotnet test ChurchServe.slnx
npm run test:ci
npm run test:coverage
npm run check:architecture
npm run build
npm run test:e2e
npm run build:release
npm run test:release
```

The integration tests cover authenticated ingestion, extra-field rejection, anonymous rejection, correlation echo, invalid-ID replacement, and safe handled failures. Angular tests cover bounded route classification, search-text exclusion, response correlation, unavailable outcomes, cancellation, recursion prevention, and global-error privacy.

## Understand the Remaining Operational Work

Instrumentation does not choose a backend, retention policy, service-level objective, or incident owner. A production team must also decide how telemetry is encrypted and access-controlled, whether local privacy rules require consent, how sampling changes interpretation, and which workflow impact is serious enough to page someone.

The tutorial deliberately excludes payments, pastoral notes, and detailed children's records. Those domains require their own threat, privacy, compliance, and safeguarding reviews.

## Complete the ChurchServe Milestone

Across 26 parts, ChurchServe evolved from an Angular shell into a server-backed scheduling application with service events, ministry positions, volunteer discovery, assignments, shareable filters, authentication, authorization, conflict handling, recovery, accessibility checks, browser journeys, production packaging, health probes, and operational feedback.

The important result is the boundaries: Angular owns interaction, the API owns authorization and validation, persistence remains replaceable, tests cover units through the published artifact, and observability describes technical outcomes without copying personal domain data.

Future work should come from real workflow feedback. Substitute handling, availability reminders, calendar export, or multilingual communication should repeat the same cycle: define the rule, protect the data boundary, implement the smallest useful slice, test failures, deploy safely, and learn from production evidence.
