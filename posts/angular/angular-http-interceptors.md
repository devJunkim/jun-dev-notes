---
title: "Angular HTTP Interceptors: Authentication, Errors, and Cross-Cutting Concerns"
excerpt: "Use modern Angular functional HTTP interceptors for authentication, correlation, logging, and consistent error policy without hiding feature behavior."
category: "Angular"

seo:
  focusKeyword: "Angular HTTP interceptors"
  description: "Build Angular HTTP interceptors with current functional APIs for authentication, correlation IDs, safe logging, error policy, and request metadata."
  socialTitle: "Angular HTTP Interceptors: A Practical Guide"
  socialDescription: "Keep cross-cutting HTTP behavior consistent while preserving feature ownership, cancellation, security, and predictable interceptor ordering."
---

# Angular HTTP Interceptors: Authentication, Errors, and Cross-Cutting Concerns

Angular HTTP interceptors sit around every request made through a configured `HttpClient`. That makes them useful for truly cross-cutting behavior—and dangerous when they become an invisible home for feature rules.

Modern Angular supports functional and DI-based interceptors. Angular recommends functional interceptors because their ordering is more predictable in complex applications.

> **Quick answer:** Register a short ordered chain of functional interceptors with `provideHttpClient(withInterceptors(...))`. Use them for consistent transport concerns such as trusted-host authentication, correlation, timing, and shared error policy. Keep feature decisions, UI messages, data mapping, and broad retry behavior at the boundary that owns them.

## Register an Explicit Functional Chain

An `HttpInterceptorFn` receives an immutable request and the next handler. The array order is the request order; responses flow back through the chain in reverse.

```typescript
import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([
        correlationInterceptor,
        authenticationInterceptor,
        telemetryInterceptor,
        errorPolicyInterceptor,
      ]),
    ),
  ],
};
```

Keep the order deliberate and test it. An authentication interceptor may add a header before telemetry observes the request, while an error policy closer to the backend sees failures before outer telemetry records the final outcome.

Class-based interceptors still exist through `withInterceptorsFromDi()`, but mixing registration styles makes ordering harder to reason about. Prefer the functional form for new code unless an existing application has a specific migration constraint.

## Add Credentials Only to Trusted Destinations

An authentication interceptor should not attach a bearer token to every URL. An application may call a CDN, analytics endpoint, or a URL supplied by an API response.

```typescript
import { inject } from '@angular/core';
import {
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';

export const authenticationInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const apiOrigin = inject(API_ORIGIN);
  const requestUrl = new URL(request.url, window.location.origin);

  if (request.context.get(SKIP_AUTH) ||
      requestUrl.origin !== apiOrigin ||
      request.headers.has('Authorization')) {
    return next(request);
  }

  const token = session.accessToken();
  if (!token) {
    return next(request);
  }

  return next(request.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  }));
};
```

Most request properties are immutable, so changes use `clone()`. Request bodies are not deeply immutable; avoid mutating an object body in place because the interceptor may run again during a retry.

The example assumes browser execution. An SSR application should inject a platform-safe origin abstraction instead of reading `window`. Token acquisition may also be asynchronous; coordinate refresh in a dedicated authentication service so simultaneous 401 responses do not start many refresh requests.

An interceptor can attach credentials, but authorization remains the server's responsibility. Do not treat a client-side role or hidden button as access control.

## Carry Request Policy with `HttpContext`

Not every API call should receive identical treatment. `HttpContextToken` adds typed metadata that is not sent to the server:

```typescript
import { HttpContextToken } from '@angular/common/http';

export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);
export const ERROR_MODE = new HttpContextToken<'global' | 'local'>(
  () => 'global',
);
```

A login call can opt out without relying on a fragile URL substring:

```typescript
return this.http.post<LoginResult>('/api/session', credentials, {
  context: new HttpContext().set(SKIP_AUTH, true),
});
```

Read `SKIP_AUTH` in the authentication interceptor before adding a token. Context is mutable and survives retries of the same request, so do not use it as an accidental global store.

Opt-outs should be rare and named around policy. If every feature supplies many flags, the interceptor is probably absorbing behavior that belongs in typed API services.

## Correlation and Telemetry Need Safe Data

A correlation interceptor can accept an existing ID from application context or create one per logical request:

```typescript
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  const correlationId = crypto.randomUUID();

  return next(request.clone({
    setHeaders: { 'X-Correlation-ID': correlationId },
  }));
};
```

Coordinate the header name with the server and gateway. A browser-generated correlation ID supports troubleshooting; distributed tracing systems may use standards such as `traceparent` and should usually own those headers through their instrumentation rather than a competing custom implementation.

Telemetry can measure the final response or error with `tap` and `finalize`. Log method, sanitized route pattern, status, duration, and correlation ID. Avoid tokens, cookies, request bodies, response bodies, and query strings containing personal data.

```typescript
import { finalize } from 'rxjs';

export const telemetryInterceptor: HttpInterceptorFn = (request, next) => {
  const telemetry = inject(TelemetryService);
  const startedAt = performance.now();

  return next(request).pipe(
    finalize(() => telemetry.recordHttpDuration(
      request.method,
      sanitizeRoute(request.url),
      performance.now() - startedAt,
    )),
  );
};
```

`finalize` also runs on unsubscription. Record whether a request completed, failed, or was cancelled if those outcomes matter; duration alone can be misleading.

## Centralize Error Policy, Not Every Recovery Decision

An interceptor can translate transport failures into a small application-wide error vocabulary and handle truly global cases such as an expired session. It should preserve enough information for the feature to decide what the failure means.

```typescript
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

export const errorPolicyInterceptor: HttpInterceptorFn = (request, next) => {
  const errors = inject(ErrorReporter);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status >= 500) {
        errors.reportDependencyFailure(request.method, request.url, error.status);
      }

      return throwError(() => error);
    }),
  );
};
```

Do not show a toast for every failure. A typeahead request cancelled by a newer search, a background refresh, and a failed checkout need different UX. The component or feature service owns that context. [Angular Observables and RxJS](https://dev.jun-kim.net/2026/09/11/angular-observables-and-rxjs-a-practical-guide/) covers cancellation and error handling inside feature streams.

Similarly, do not turn all 404 responses into `null` or all errors into successful fallback values. A resolver may redirect for an expected missing resource, while another feature must show that its dependency is unavailable. [Angular Route Resolvers](https://dev.jun-kim.net/2026/09/17/angular-route-resolvers-data-loading-errors-and-ux-trade-offs/) keeps that navigation decision at the route boundary.

## Retry Only an Operation That Is Safe to Repeat

A global `retry(3)` is risky. It can repeat a timed-out write that the server already committed, multiply load during an outage, and delay feedback for failures that will not improve.

If a shared interceptor implements retry, limit it to explicitly eligible requests, transient status codes, a small attempt count, and delayed backoff with jitter. Respect cancellation and server retry guidance. Prefer a stable idempotency key for retryable writes; client-side retry alone cannot make a server operation idempotent.

Often the typed API service knows more: whether a request is a read, whether stale data is acceptable, and what overall deadline the interaction owns. Keep retry there when the policy is dependency- or operation-specific.

## What Does Not Belong in an Interceptor

Avoid putting these concerns in a global chain:

- feature-specific response mapping or business validation;
- navigation for every status code;
- UI state for a particular component;
- silent fallback that converts failures into plausible data;
- unbounded caching without user isolation and invalidation;
- automatic retry of every method;
- mutable global counters that break under concurrent requests;
- secrets or full payloads in logs.

Data-access services remain the clearer place for endpoint contracts and DTO mapping. [Angular Services and Dependency Injection](https://dev.jun-kim.net/2026/09/11/angular-services-and-dependency-injection-a-practical-guide/) describes that boundary.

## Test the Chain as Behavior

Use Angular's HTTP testing support to assert the final outgoing headers, opt-outs, error propagation, and cancellation. Include two concurrent requests so shared refresh or loading logic cannot hide a race. Test the configured chain rather than only invoking each function in isolation; ordering is part of the behavior.

An interceptor earns its global reach when the rule is truly global, transparent to features, and safe for every matching request. Keep the chain small enough that a developer can still predict what `http.get()` will do.
