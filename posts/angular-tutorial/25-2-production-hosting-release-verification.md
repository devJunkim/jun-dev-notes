---
title: "ChurchServe Angular Tutorial Part 25-2: Production Hosting and Release Verification"
excerpt: "Serve the combined ChurchServe artifact with health probes, safe SPA routing, cache and security headers, executable smoke tests, and CI retention."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core Angular production hosting"
  description: "Host Angular with ASP.NET Core using health checks, safe SPA fallback, secure caching, release smoke tests, and CI artifacts."
  socialTitle: "ChurchServe Part 25-2: Verify the Release"
  socialDescription: "Add production hosting policies and test the exact Angular and ASP.NET Core artifact before deployment."
---

# ChurchServe Angular Tutorial Part 25-2: Production Hosting and Release Verification

Part 25-1 created one release artifact and made Angular consume validated public configuration before bootstrap. Packaging is only half the deployment boundary. The host must serve deep links without masking API failures, expose meaningful probes, apply compatible security and cache policies, and prove the exact published output—not merely its source projects.

> **Quick answer:** Serve the Angular files from ASP.NET Core, reserve SPA fallback for non-file browser routes, make unknown API routes stay 404, separate liveness from database-backed readiness, and run a smoke-test process against `artifacts/publish`. Upload only an artifact that passes those checks.

## Files Added in Part 25-2

```text
ChurchServe/
├── .github/workflows/ci.yml
├── server/ChurchServe.Api/
│   ├── Deployment/
│   │   ├── DatabaseHealthCheck.cs
│   │   ├── DeploymentEndpoints.cs
│   │   ├── DeploymentHeadersExtensions.cs
│   │   └── PublicRuntimeOptions.cs
│   ├── Program.cs
│   └── appsettings.json
├── tests/ChurchServe.Api.Tests/
│   └── DeploymentEndpointsTests.cs
└── tools/verify-release.mjs
```

## Validate Public Server Configuration at Startup

`PublicRuntimeOptions.cs` represents only browser-readable values:

```csharp
namespace ChurchServe.Api.Deployment;

public sealed class PublicRuntimeOptions
{
  public const string SectionName = "PublicRuntime";

  public string ApiBaseUrl { get; init; } = "/api";

  public string Release { get; init; } = "local";

  public static bool HasValidApiBaseUrl(PublicRuntimeOptions options) =>
      !string.IsNullOrWhiteSpace(options.ApiBaseUrl) &&
      options.ApiBaseUrl.StartsWith('/') &&
      !options.ApiBaseUrl.StartsWith("//", StringComparison.Ordinal) &&
      !options.ApiBaseUrl.Contains('?') &&
      !options.ApiBaseUrl.Contains('#');

  public static bool HasValidRelease(PublicRuntimeOptions options) =>
      !string.IsNullOrWhiteSpace(options.Release) && options.Release.Length <= 100;
}
```

The defaults in `appsettings.json` are:

```json
"PublicRuntime": {
  "ApiBaseUrl": "/api",
  "Release": "local"
}
```

A host overrides them without rebuilding:

```bash
PublicRuntime__ApiBaseUrl=/api
PublicRuntime__Release=2026.10.25.1
```

`ValidateOnStart` rejects bad deployment input before traffic reaches the instance.

## Separate Liveness from Readiness

Liveness asks whether the process can answer. Readiness asks whether it can do required work. ChurchServe's readiness depends on its database:

```csharp
using ChurchServe.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChurchServe.Api.Deployment;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(
      HealthCheckContext context,
      CancellationToken cancellationToken = default)
  {
    try
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var dbContext = scope.ServiceProvider.GetRequiredService<ChurchServeDbContext>();
      return await dbContext.Database.CanConnectAsync(cancellationToken)
          ? HealthCheckResult.Healthy()
          : HealthCheckResult.Unhealthy("The ChurchServe database is unavailable.");
    }
    catch (Exception exception)
    {
      return HealthCheckResult.Unhealthy("The ChurchServe database check failed.", exception);
    }
  }
}
```

The public response stays bounded. Detailed exception data belongs in server telemetry, not the probe body.

## Map Configuration and Probes

`DeploymentEndpoints.cs` keeps the deployment endpoints together:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ChurchServe.Api.Deployment;

public static class DeploymentEndpoints
{
  public static IEndpointRouteBuilder MapDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
  {
    endpoints.MapGet("/runtime-config.json", (
        HttpContext context,
        IOptions<PublicRuntimeOptions> options) =>
    {
      context.Response.Headers.CacheControl = "no-store";
      return Results.Json(new PublicRuntimeResponse(
          NormalizeApiBaseUrl(options.Value.ApiBaseUrl),
          options.Value.Release.Trim()));
    }).AllowAnonymous();

    endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
    {
      Predicate = _ => false,
      ResponseWriter = WriteHealthResponseAsync,
    }).AllowAnonymous();

    endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
      Predicate = registration => registration.Tags.Contains("ready"),
      ResponseWriter = WriteHealthResponseAsync,
    }).AllowAnonymous();

    return endpoints;
  }

  private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
  {
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
      status = report.Status.ToString().ToLowerInvariant(),
    }));
  }

  private static string NormalizeApiBaseUrl(string value) =>
      value.Length > 1 ? value.TrimEnd('/') : value;

  private sealed record PublicRuntimeResponse(string ApiBaseUrl, string Release);
}
```

`/health/live` runs no dependency checks. `/health/ready` selects checks tagged `ready`. `/runtime-config.json` uses `no-store` so browsers do not retain the wrong release identity.

## Add Compatible Security and Cache Headers

Angular's automatic CSP supplies a hash-based script policy in `index.html`. The server header must not add a conflicting `default-src` or `script-src`; browsers enforce every supplied CSP.

```csharp
using System.Text.RegularExpressions;

namespace ChurchServe.Api.Deployment;

public static partial class DeploymentHeadersExtensions
{
  public static IApplicationBuilder UseDeploymentHeaders(this IApplicationBuilder app) =>
      app.Use(async (context, next) =>
      {
        context.Response.OnStarting(() =>
        {
          var headers = context.Response.Headers;
          headers.XContentTypeOptions = "nosniff";
          headers.XFrameOptions = "DENY";
          headers["Referrer-Policy"] = "no-referrer";
          headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
          headers.ContentSecurityPolicy =
              "style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
              "connect-src 'self'; frame-ancestors 'none'; form-action 'self'; " +
              "object-src 'none'; base-uri 'self'";

          if (context.Response.StatusCode == StatusCodes.Status200OK &&
              context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
          {
            headers.CacheControl = "no-cache";
          }
          else if (context.Response.StatusCode == StatusCodes.Status200OK &&
                   HasFingerprint().IsMatch(context.Request.Path.Value ?? string.Empty))
          {
            headers.CacheControl = "public,max-age=31536000,immutable";
          }

          return Task.CompletedTask;
        });

        await next();
      });

  [GeneratedRegex(@"[-.][A-Za-z0-9_-]{8,}\.(?:css|js)$", RegexOptions.IgnoreCase)]
  private static partial Regex HasFingerprint();
}
```

Angular inserts component styles at runtime, so the static-hosting policy permits inline styles. It does not permit arbitrary inline scripts. Fingerprinted JavaScript and CSS can be cached for one year; HTML uses `no-cache` so it revalidates the document that points to those assets.

These directives fit ChurchServe's current resources. Adding a CDN, telemetry origin, embedded frame, or external font requires an explicit CSP review.

## Compose the Host in `Program.cs`

Register the public options and readiness check:

```csharp
builder.Services
    .AddOptions<PublicRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(PublicRuntimeOptions.SectionName))
    .Validate(PublicRuntimeOptions.HasValidApiBaseUrl,
        "PublicRuntime:ApiBaseUrl must be a same-origin absolute path.")
    .Validate(PublicRuntimeOptions.HasValidRelease,
        "PublicRuntime:Release must be between 1 and 100 characters.")
    .ValidateOnStart();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
```

Then compose the pipeline after database initialization:

```csharp
app.UseDeploymentHeaders();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapSessionEndpoints();
app.MapServiceEventEndpoints().RequireAuthorization();
app.MapAssignmentEndpoints().RequireAuthorization();
app.MapVolunteerEndpoints().RequireAuthorization();
app.MapDeploymentEndpoints();
if (app.Environment.IsEnvironment("Testing"))
{
  app.MapTestFixtureEndpoints();
}

app.MapMethods(
    "/api/{**path}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"],
    () => Results.NotFound());
app.MapFallbackToFile("index.html");
```

Ordering defines behavior. Existing static files are served first. Known API endpoints win through endpoint specificity. The `/api/{**path}` catch-all returns a real 404 before the final non-file fallback can turn an unknown API GET into Angular HTML. A missing `.js` file also remains 404 because the SPA fallback excludes file-looking paths.

Microsoft's [.NET 10 static-file documentation](https://learn.microsoft.com/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0) describes the web-root boundary. TLS termination and forwarded-header settings belong to the selected reverse proxy or platform; Part 25 does not invent them without a target host.

## Protect the HTTP Contract with API Tests

`DeploymentEndpointsTests.cs` adds four tests:

```csharp
[Fact]
public async Task Runtime_configuration_is_public_same_origin_and_not_cached()
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var response = await client.GetAsync("/runtime-config.json");
  var configuration = await response.Content.ReadFromJsonAsync<RuntimeConfigResponse>();

  Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  Assert.NotNull(configuration);
  Assert.Equal("/api", configuration.ApiBaseUrl);
  Assert.Equal("local", configuration.Release);
  Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
  Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
  Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
}

[Theory]
[InlineData("/health/live")]
[InlineData("/health/ready")]
public async Task Health_endpoints_report_healthy(string path)
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var response = await client.GetAsync(path);
  var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

  Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  Assert.Equal("healthy", health?.Status);
}

[Fact]
public async Task Unknown_api_routes_are_not_rewritten_to_the_application_shell()
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var response = await client.GetAsync("/api/not-a-real-route");

  Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
}
```

These integration tests do not prove the final publish directory, so Part 25 adds another layer.

## Smoke-Test the Exact Published Output

`tools/verify-release.mjs` starts `artifacts/publish/ChurchServe.Api.dll` in `Production` on an available loopback port. It provides a temporary SQLite path and a known release identifier, waits for readiness, and exercises the combined artifact.

Its central assertions are:

```javascript
const root = await fetch(`${baseUrl}/`);
assert.equal(root.status, 200);
assert.match(root.headers.get('content-type') ?? '', /^text\/html/);
assert.match(root.headers.get('cache-control') ?? '', /no-cache/);
assert.equal(root.headers.get('x-content-type-options'), 'nosniff');
assert.equal(root.headers.get('x-frame-options'), 'DENY');
assert.match(root.headers.get('content-security-policy') ?? '', /frame-ancestors 'none'/);

const deepLink = await fetch(`${baseUrl}/schedule/sunday-2026-11-01`);
assert.equal(deepLink.status, 200);
assert.match(deepLink.headers.get('content-type') ?? '', /^text\/html/);

const missingAsset = await fetch(`${baseUrl}/missing.12345678.js`);
assert.equal(missingAsset.status, 404);
assert.doesNotMatch(missingAsset.headers.get('content-type') ?? '', /^text\/html/);

const missingApi = await fetch(`${baseUrl}/api/not-a-real-route`);
assert.equal(missingApi.status, 404);
assert.doesNotMatch(missingApi.headers.get('content-type') ?? '', /^text\/html/);

const runtime = await fetch(`${baseUrl}/runtime-config.json`);
assert.equal(runtime.status, 200);
assert.match(runtime.headers.get('cache-control') ?? '', /no-store/);
assert.deepEqual(await runtime.json(), {
  apiBaseUrl: '/api',
  release: 'part-25-verification',
});
```

The remaining script verifies both health endpoints, finds a fingerprinted JavaScript file, checks its one-year immutable policy, rejects public source maps, scans built JavaScript for known configuration-secret markers, stops the child process, and removes the temporary database.

Run it with:

```bash
npm run build:release
npm run test:release
```

This is stronger than launching `ng serve`: it exercises optimized files from `artifacts/publish/wwwroot` through the production ASP.NET Core pipeline.

## Retain Only Verified CI Output

Replace the standalone Angular build with this sequence:

```yaml
- name: Build production release artifact
  run: npm run build:release

- name: Run API integration tests
  run: dotnet test ChurchServe.slnx --configuration Release

- name: Verify published release artifact
  run: npm run test:release

- name: Run critical browser journeys
  run: npm run test:e2e

- name: Upload production release artifact
  uses: actions/upload-artifact@v4
  with:
    name: churchserve-${{ github.sha }}
    path: artifacts/publish/
    if-no-files-found: error
    retention-days: 7
```

Naming the artifact with the commit SHA connects deployable bytes to reviewed source. Seven days is tutorial-scale retention, not a universal production policy.

## Roll Back the Artifact, Not a Rebuild

Build once and promote the same verified bytes. If a release fails, redeploy the preceding known-good artifact instead of rebuilding an old branch with a different SDK or dependency registry state.

Application rollback does not undo data changes. Schema evolution must keep both the new and previous application compatible during the rollback window. Use expand-and-contract changes, postpone destructive cleanup, and rehearse rollback outside production.

Part 25 produces and verifies a production-shaped artifact, but the selected platform still needs HTTPS, durable storage and backups, secret injection, a real identity provider, capacity policies, and operational ownership. Part 26 adds the signals needed to understand the deployed application.
