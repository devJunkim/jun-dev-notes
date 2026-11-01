---
title: "ChurchServe React Tutorial Part 25-2: Production Hosting and Release Verification"
excerpt: "Serve the combined ChurchServe artifact with health probes, safe React routing, cache and security headers, executable smoke tests, and CI retention."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core React production hosting"
  description: "Host React with ASP.NET Core using health checks, safe SPA fallback, secure caching, release smoke tests, and verified CI artifacts."
  socialTitle: "ChurchServe React Part 25-2: Verify the Release"
  socialDescription: "Add production hosting policies and test the exact React and ASP.NET Core artifact before deployment."
---

# ChurchServe React Tutorial Part 25-2: Production Hosting and Release Verification

Part 25-1 created one artifact and made React consume validated public configuration before bootstrap. Packaging is only half of the deployment boundary. The host must serve client-side deep links without masking API failures, expose meaningful probes, apply compatible security and cache policies, and test the exact published directory.

> **Quick answer:** Serve Vite’s optimized files from ASP.NET Core, reserve the SPA fallback for non-file browser routes, keep unknown API routes as real 404 responses, separate process liveness from database-backed readiness, and launch `artifacts/publish` in a smoke test. Retain only an artifact that passes those checks.

## What Part 25-2 Adds

```text
ChurchServeReact/
├── .github/workflows/ci.yml
├── server/
│   ├── ChurchServe.Api/
│   │   ├── Deployment/
│   │   │   ├── DatabaseHealthCheck.cs
│   │   │   ├── DeploymentEndpoints.cs
│   │   │   ├── DeploymentHeadersExtensions.cs
│   │   │   └── PublicRuntimeOptions.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── ChurchServe.Api.Tests/
│       └── DeploymentEndpointsTests.cs
└── tools/
    └── verify-release.mjs
```

## Validate Browser-Readable Settings at Startup

`PublicRuntimeOptions` represents only values intended to cross the browser boundary:

```csharp
public sealed class PublicRuntimeOptions
{
    public const string SectionName = "PublicRuntime";

    public string ApiBaseUrl { get; init; } = "/api";

    public string Release { get; init; } = "local";

    public static bool HasValidRelease(PublicRuntimeOptions options) =>
        !string.IsNullOrWhiteSpace(options.Release) &&
        options.Release.Length <= 100;
}
```

The API path validator mirrors the browser’s same-origin rules, including rejection of protocol-relative paths, query strings, fragments, backslashes, and surrounding whitespace.

Bind and validate the section in `Program.cs`:

```csharp
builder.Services
    .AddOptions<PublicRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(PublicRuntimeOptions.SectionName))
    .Validate(
        PublicRuntimeOptions.HasValidApiBaseUrl,
        "PublicRuntime:ApiBaseUrl must be a same-origin absolute path.")
    .Validate(
        PublicRuntimeOptions.HasValidRelease,
        "PublicRuntime:Release must be between 1 and 100 characters.")
    .ValidateOnStart();
```

ASP.NET Core’s [options guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0) describes typed configuration and validation as separate concerns. `ValidateOnStart` makes bad deployment input prevent a healthy-looking instance from accepting traffic.

The checked-in defaults are safe and public:

```json
"PublicRuntime": {
  "ApiBaseUrl": "/api",
  "Release": "local"
}
```

A deployment can override them without rebuilding:

```bash
PublicRuntime__ApiBaseUrl=/api
PublicRuntime__Release=2026.11.01.1
```

## Separate Liveness from Readiness

Liveness asks whether the process can answer. Readiness asks whether this instance can perform required work. ChurchServe’s readiness depends on SQLite:

```csharp
public sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ChurchServeDbContext>();
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(
                    "The ChurchServe database is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "The ChurchServe database check failed.", exception);
        }
    }
}
```

The public response does not expose exception details. They belong in server telemetry. The broader design trade-offs are covered in [ASP.NET Core Health Checks: Liveness, Readiness, and Dependency Failures](https://dev.jun-kim.net/2026/09/24/asp-net-core-health-checks-liveness-readiness-and-dependency-failures/).

Register the database check with a readiness tag:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
```

ASP.NET Core’s [health-check documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) supports filtering checks by endpoint. ChurchServe’s liveness predicate selects none; readiness selects the `ready` tag.

## Map Runtime Configuration and Probes

Keep the deployment endpoints together:

```csharp
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
```

The response writer returns only `{ "status": "healthy" }`, or the corresponding bounded status. Monitoring systems do not need stack traces, connection strings, or dependency topology.

## Apply Headers That Match the Built React Application

Vite emits external JavaScript modules and CSS files for this application. ChurchServe can therefore disallow inline scripts and inline styles:

```csharp
headers.ContentSecurityPolicy =
    "default-src 'self'; script-src 'self'; " +
    "style-src 'self'; img-src 'self' data:; " +
    "font-src 'self'; connect-src 'self'; " +
    "frame-ancestors 'none'; form-action 'self'; " +
    "object-src 'none'; base-uri 'self'";
```

MDN’s [Content Security Policy guide](https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/CSP) explains how `script-src` constrains executable sources and how `frame-ancestors 'none'` prevents framing. This policy fits ChurchServe’s current output. Adding inline code, a CDN, telemetry collector, external font, or embedded frame requires an explicit policy review.

The middleware also sets `nosniff`, `DENY`, `no-referrer`, and a restrictive permissions policy. Response-aware caching distinguishes HTML from fingerprinted assets:

```csharp
if (context.Response.StatusCode == StatusCodes.Status200OK &&
    context.Response.ContentType?.StartsWith(
        "text/html",
        StringComparison.OrdinalIgnoreCase) == true)
{
    headers.CacheControl = "no-cache";
}
else if (
    context.Response.StatusCode == StatusCodes.Status200OK &&
    HasFingerprint().IsMatch(context.Request.Path.Value ?? string.Empty))
{
    headers.CacheControl = "public,max-age=31536000,immutable";
}
```

`no-cache` makes browsers revalidate HTML that names the current chunks. Hashed JavaScript and CSS receive a one-year immutable policy because changed content receives a changed URL. This is the cache-busting pattern described in MDN’s [`Cache-Control` reference](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control).

## Preserve Real 404 Responses Around the SPA Fallback

Compose the host after database initialization:

```csharp
app.UseDeploymentHeaders();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapSessionEndpoints();
// Map the known service-event and testing endpoints.
app.MapDeploymentEndpoints();

app.MapMethods(
    "/api/{**path}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"],
    () => Results.NotFound());
app.MapFallbackToFile("index.html");
```

Ordering and endpoint specificity establish the contract:

- Existing Vite files are served from `wwwroot`.
- Known API endpoints win over the API catch-all.
- Unknown API paths return 404 instead of React HTML.
- Missing file-looking paths remain 404.
- A non-file `GET` or `HEAD`, such as `/schedule/sunday-2026-11-01`, receives `index.html` so React Router can resolve the route.

Microsoft’s [.NET 10 static-file guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0) defines `wwwroot` as the public boundary for static-file middleware. Files elsewhere in the artifact are not made public by `UseStaticFiles`.

TLS termination, forwarded headers, durable storage, backups, and a production identity provider depend on the selected platform. This tutorial does not invent those settings without a target host.

## Protect the HTTP Contract with Integration Tests

`DeploymentEndpointsTests.cs` adds four cases. They verify the anonymous runtime document, `no-store`, security headers, both healthy probes, and the unknown-API boundary:

```csharp
[Fact]
public Task Unknown_api_routes_are_not_rewritten_to_the_application_shell() =>
    WithApiAsync(async client =>
    {
        var response = await client.GetAsync(
            "/api/not-a-real-route",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(
            "text/html",
            response.Content.Headers.ContentType?.MediaType);
    });
```

The API tests use a migrated temporary SQLite database, but they do not prove that the final publish directory contains the right Vite files. The release smoke test closes that gap.

## Smoke-Test the Exact Published Directory

`tools/verify-release.mjs` starts `artifacts/publish/ChurchServe.Api.dll` in `Production` on an available loopback port. It supplies a temporary SQLite path and a known release ID, then waits for readiness.

Its central assertions exercise the artifact through HTTP:

```javascript
const root = await fetch(`${baseUrl}/`)
assert.equal(root.status, 200)
assert.match(root.headers.get('content-type') ?? '', /^text\/html/)
assert.match(root.headers.get('cache-control') ?? '', /no-cache/)
assert.equal(root.headers.get('x-content-type-options'), 'nosniff')
assert.match(
  root.headers.get('content-security-policy') ?? '',
  /script-src 'self'/,
)

const deepLink = await fetch(
  `${baseUrl}/schedule/sunday-2026-11-01`,
)
assert.equal(deepLink.status, 200)

const missingApi = await fetch(`${baseUrl}/api/not-a-real-route`)
assert.equal(missingApi.status, 404)
assert.doesNotMatch(
  missingApi.headers.get('content-type') ?? '',
  /^text\/html/,
)
```

The script also verifies both probes, the runtime JSON and release ID, a missing file-looking asset, an actual Vite chunk’s immutable cache policy, absence of public source maps, and absence of known secret markers in built JavaScript. It stops the child process and removes the temporary database in `finally`.

Run the production-shaped verification with:

```bash
npm run build:release
npm run test:release
```

This is stronger than `vite preview`. It exercises optimized client files through the same ASP.NET Core pipeline that serves API and deployment endpoints.

## Retain Only Verified CI Output

CI performs fast source checks, builds the combined artifact, runs API and migration checks, smoke-tests the published output, and then runs the Part 24 browser journeys. Only afterward does it upload the release:

```yaml
- name: Build production release artifact
  run: npm run build:release

- name: Run API integration tests
  run: dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj --configuration Release

- name: Verify published release artifact
  run: npm run test:release

- name: Run critical browser journeys
  run: npm run test:e2e

- name: Upload production release artifact
  uses: actions/upload-artifact@v4
  with:
    name: churchserve-react-${{ github.sha }}
    path: artifacts/publish/
    if-no-files-found: error
    retention-days: 7
```

The commit SHA connects downloadable bytes to reviewed source. Seven days is tutorial-scale retention, not a universal policy.

Build once and promote the same verified bytes. If a release fails, redeploy a preceding known-good artifact instead of rebuilding an old branch under a changed SDK or package registry. Application rollback still does not undo database changes; schema evolution must preserve compatibility for the rollback window.

Part 25 produces a production-shaped artifact, but a real platform still needs HTTPS, durable data, secret injection, capacity policies, and operational ownership. Part 26 can add the signals needed to understand the deployed application without collecting sensitive coordinator data.
