---
title: "HttpClient in .NET: Best Practices for Calling External APIs"
excerpt: "Build reliable external API clients in .NET with correct connection lifetimes, cancellation, response handling, bounded retries, and focused tests."
category: ".NET"

seo:
  focusKeyword: "HttpClient best practices in .NET"
  description: "Use HttpClient and IHttpClientFactory in .NET with sensible lifetimes, typed clients, cancellation, response handling, retries, and safe logging."
  socialTitle: "HttpClient in .NET: Best Practices for External APIs"
  socialDescription: "Design an HTTP boundary that handles connection reuse, failure semantics, deadlines, authentication, and testing without hiding important decisions."
---

# HttpClient in .NET: Best Practices for Calling External APIs

Calling another API introduces a boundary your application does not control. Connections fail, requests outlive their callers, and a timeout may occur after the remote system has already accepted a write.

A useful HTTP client makes those outcomes explicit. Its job extends beyond deserializing JSON into a convenient type.

> **Quick answer:** Reuse connections through a correctly configured long-lived client or `IHttpClientFactory`. Give each operation a cancellation budget, handle expected responses deliberately, and retry only when both the failure and the operation make retrying safe.

## Client Lifetime and Connection Lifetime Are Different

Creating `new HttpClient()` for every request normally creates a new underlying handler and connection pool. Disposing that client tears down its pool, causing connection churn and potentially exhausting available ports under load.

Two supported approaches address this:

| Approach | What owns reuse? | Typical fit |
| --- | --- | --- |
| Long-lived `HttpClient` with `SocketsHttpHandler.PooledConnectionLifetime` | The retained handler and its pool | Explicitly managed clients in services or libraries |
| Short-lived clients created by `IHttpClientFactory` | Factory-managed, pooled handlers | Applications using DI and named or typed configuration |

DNS is resolved when a connection is established. A client does not continuously track DNS TTL changes. A connection lifetime allows eventual replacement and another lookup; it is not an immediate response to a DNS update.

The factory creates client instances while reusing handlers. Disposing a factory-created client does not immediately destroy that pooled handler. Handler lifetime controls eligibility for handler replacement, rather than specifying a deadline for every connection or request.

Do not capture a typed client indefinitely in a singleton and assume factory rotation will refresh it. Obtain named clients from the factory as needed in a singleton, or deliberately configure a long-lived handler strategy. [Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) covers the lifetime mismatch behind this problem.

## Choose Named or Typed Clients by the Boundary

A named client is useful when several call sites need the same configuration, or a long-lived service needs to request clients on demand. A typed client gives one external API a dedicated interface in your code: request construction, response interpretation, and serialization stay together.

This .NET example registers a typed catalog client. The URL is deliberately an example host and must come from validated application configuration in a real deployment.

```csharp
using Microsoft.Extensions.DependencyInjection;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services)
    {
        services.AddHttpClient<CatalogClient>(client =>
        {
            client.BaseAddress = new Uri("https://catalog.example/api/");
            // The operation below owns a budget covering headers and body.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        return services;
    }
}
```

The composition root calls `builder.Services.AddCatalog()`. Do not separately register `CatalogClient` with a conflicting lifetime.

For a named client, use `AddHttpClient("catalog", ...)` with the same configuration and obtain it through `factory.CreateClient("catalog")`. Names are useful configuration keys, but scattering string names and response parsing across business handlers weakens the boundary a typed client provides.

The trailing slash in `BaseAddress` matters. With the address above, `products/42` resolves under `/api/`; `/products/42` starts at the host root. Test the final URI, especially when a reverse proxy adds a path prefix.

## Make the Operation Own Its Deadline

The following client treats a catalog 404 as an expected absence. Other unsuccessful statuses become failures. It assumes the catalog contract returns a bounded JSON object and that callers supply a nonnegative product ID validated at their boundary.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed record CatalogProduct(int Id, string Name, decimal Price);

public sealed class CatalogClient(HttpClient httpClient)
{
    public async Task<CatalogProduct?> FindAsync(
        int productId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(10));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"products/{productId}");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            budget.Token);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var product = await response.Content.ReadFromJsonAsync<CatalogProduct>(
            cancellationToken: budget.Token);

        if (product is null || string.IsNullOrWhiteSpace(product.Name))
        {
            throw new JsonException("The catalog returned an invalid product.");
        }

        return product;
    }
}
```

`ResponseHeadersRead` completes the send once headers are available; it does not mean body processing has completed. Passing the same budget to deserialization covers that second phase. A finite `HttpClient.Timeout` alone is not a body deadline when using this completion mode.

The linked token also honors caller cancellation. In ASP.NET Core, pass the request's cancellation token through the application operation. Do not replace it with `CancellationToken.None` merely to simplify a signature. The ten-second budget is an example service decision, not a recommended timeout for every dependency.

Disposing the response releases its resources. If you return a stream instead, document who owns the response and how long cancellation remains valid; returning a stream from inside this `using` scope would leave the caller with disposed resources.

Streaming avoids buffering the entire raw response before deserialization, but the resulting object still uses memory. For untrusted or potentially large payloads, enforce a response-size policy as well as a time limit. A `Content-Length` check alone does not cover responses without that header.

## Expected Statuses Belong in the Contract

`EnsureSuccessStatusCode()` is appropriate when every non-success response is exceptional for that operation. Explicit handling is better when a status means something the application expects: absence, a version conflict, or a rejected business request.

Do not convert every error to `null`; an unavailable catalog is different from a missing product. Conversely, do not expose an upstream error body directly to an API caller. Translate the dependency failure into your application's existing error contract. [Global Exception Handling in ASP.NET Core](https://dev.jun-kim.net/2026/09/13/global-exception-handling-in-asp-net-core-a-clean-approach/) discusses the transport-level boundary.

Successful HTTP status does not guarantee a valid business response. JSON syntax, required fields, identifiers, and acceptable values may still need checking. Add validation appropriate to the upstream contract rather than trusting that a non-null DTO proves correctness.

For outgoing JSON, `JsonContent.Create(command)` on an `HttpRequestMessage` keeps serialization close to request construction. Request content belongs to that request and is disposed with it. Avoid manually concatenating JSON strings.

## Keep Credentials Request-Specific

The token parameter above represents a token obtained from a trusted credential provider. Do not embed tokens in source code or fetch arbitrary secrets inside a domain entity. Depending on the integration, a dedicated authentication handler may obtain and attach service credentials.

Set headers that vary by caller on `HttpRequestMessage`. Mutating shared `DefaultRequestHeaders.Authorization` for concurrent users can send the wrong credential with a request. Defaults are better suited to stable configuration such as an application-wide user agent.

Validate configured destinations, and avoid allowing untrusted input to replace the host of an authenticated request. Do not put secrets in query strings, where URLs may be logged by several systems.

## Add Bounded Resilience, Not Unlimited Repetition

`Microsoft.Extensions.Http.Resilience` provides modern HTTP resilience handlers. Add its package explicitly when using it. For the registration above, a standard handler can be attached to the returned `IHttpClientBuilder`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

services.AddHttpClient<CatalogClient>(client =>
{
    client.BaseAddress = new Uri("https://catalog.example/api/");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    options.Retry.UseJitter = true;
    options.Retry.DisableForUnsafeHttpMethods();
});
```

This replaces the earlier registration; it is not a second registration to stack on top. The standard retry strategy uses exponential backoff. Its additional timeout and circuit-breaker strategies must be tuned with the operation budget and the dependency's latency characteristics.

Disabling retries for unsafe methods is a conservative guard. The standard handler otherwise permits retries that may include writes. A timed-out POST may already have created an order. Retry such an operation only with a documented idempotency contract and a stable idempotency key that the server actually enforces.

Transient network failures, throttling, and some server errors may justify another attempt. Authentication failures and invalid requests usually need correction. A business rejection does not become transient because it arrived over HTTP. Review the handler's configured failure predicate and how it honors `Retry-After` for your dependency.

Jitter reduces synchronized retry bursts; a small attempt count limits amplification. Avoid retries at every layer: two retries in an HTTP handler and two more in a surrounding workflow can multiply outbound attempts. Concurrency limits and circuit breaking protect downstream capacity in ways that retries cannot.

## Test the Boundary Without Mocking Business Logic

`HttpClient` accepts an `HttpMessageHandler`, so tests can exercise real request construction and response parsing with a small test handler:

```csharp
public sealed class StubHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => send(request, cancellationToken);
}
```

Return a fresh response for each invocation. Assert the method, resolved URI, and request-specific headers; cover a valid response, 404, malformed JSON, other failure statuses, and cancellation. Test configured retry behavior separately so a unit test does not accidentally wait through production backoff.

These tests do not verify DNS refresh, TLS, real connection reuse, or upstream compatibility. A small integration suite against an appropriate test endpoint covers those boundaries more honestly than a large set of mocks.

## What to Review Before Shipping

Log dependency name, operation, duration, status, and a correlation or trace identifier. Avoid authorization headers, full bodies, and sensitive URL parameters; review automatic HTTP logging as well as your own messages.

Before release, confirm that client ownership is clear, cancellation reaches body reads, expected statuses retain their meaning, and retries fit within an overall deadline. Then test a dependency outage. A client that fails predictably under that condition is more useful than one that succeeds only on the happy path.
