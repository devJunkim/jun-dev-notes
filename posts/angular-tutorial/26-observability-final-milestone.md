---
title: "ChurchServe Angular Tutorial Part 26-1: Privacy-Safe Browser Telemetry and Correlation IDs"
excerpt: "Add bounded Angular telemetry, end-to-end correlation IDs, safe global error reporting, and a protected ASP.NET Core ingestion endpoint."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular telemetry correlation ID"
  description: "Build privacy-safe Angular telemetry with correlation IDs, bounded event contracts, an HTTP interceptor, and a protected ASP.NET Core endpoint."
  socialTitle: "ChurchServe Part 26-1: Safe Browser Telemetry"
  socialDescription: "Connect Angular failures to API diagnostics without collecting volunteer names, search text, URLs, or request bodies."
---

# ChurchServe Angular Tutorial Part 26-1: Privacy-Safe Browser Telemetry and Correlation IDs

Part 25 made ChurchServe deployable. A deployable application still leaves an operational question: when a coordinator reports a failed schedule request, how do we connect that browser failure to one server request without collecting the coordinator's work as telemetry?

This part adds a narrow browser event contract, an Angular HTTP interceptor, a protected telemetry endpoint, and one correlation ID that follows an API request into server logs and safe Problem Details responses.

> **Quick answer:** Generate a correlation ID for each Angular API request, classify the request into a fixed operation name, record only duration and a bounded outcome, validate the same allowlist again at the API, and reject extra fields. Never send raw URLs, query strings, names, email addresses, bodies, or exception messages.

## Files Added and Updated

```text
src/app/
  app.config.ts                                      updated
  core/errors/global-error-handler.ts                updated
  core/telemetry/browser-telemetry.ts                new
  core/telemetry/http-telemetry.interceptor.ts       new
server/ChurchServe.Api/
  Observability/BrowserTelemetryEndpoints.cs         new
  Observability/BrowserTelemetryRequest.cs           new
  Observability/CorrelationIdMiddleware.cs           new
  Program.cs                                         updated
```

## Define a Closed Browser Contract

The browser reports fixed technical dimensions rather than an open property bag. `SKIP_BROWSER_TELEMETRY` prevents the telemetry POST from recursively generating another telemetry POST, while delivery failure is ignored so monitoring cannot break scheduling.

```typescript
export type BrowserTelemetryEventName = 'http_request' | 'application_error';
export type BrowserTelemetryOperation =
  | 'application' | 'session.restore' | 'session.sign-in' | 'session.sign-out'
  | 'schedule.list' | 'schedule.detail' | 'schedule.create'
  | 'assignment.board' | 'assignment.create' | 'volunteer.search' | 'other';
export type BrowserTelemetryOutcome =
  | 'success' | 'cancelled' | 'validation' | 'unauthenticated' | 'forbidden'
  | 'not_found' | 'conflict' | 'unavailable' | 'unexpected';

export interface BrowserTelemetryEvent {
  readonly name: BrowserTelemetryEventName;
  readonly operation: BrowserTelemetryOperation;
  readonly outcome: BrowserTelemetryOutcome;
  readonly durationMs: number;
  readonly correlationId?: string;
}

export const SKIP_BROWSER_TELEMETRY = new HttpContextToken<boolean>(() => false);

@Injectable({ providedIn: 'root' })
export class BrowserTelemetry {
  private readonly endpointContext = new HttpContext().set(SKIP_BROWSER_TELEMETRY, true);

  constructor(private readonly http: HttpClient) {}

  record(event: BrowserTelemetryEvent): void {
    this.http.post('/api/telemetry', event, { context: this.endpointContext })
      .subscribe({ error: () => undefined });
  }
}
```

The required imports are `HttpClient`, `HttpContext`, and `HttpContextToken` from `@angular/common/http`, plus `Injectable` from `@angular/core`.

## Classify Requests Without Retaining Their URLs

The interceptor adds `X-Correlation-ID`, measures elapsed browser time, and converts each route shape into a bounded operation. It never puts `request.url`, an event ID, or a search query in the telemetry event.

```typescript
export const httpTelemetryInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.context.get(SKIP_BROWSER_TELEMETRY) || !isApiRequest(request.url)) {
    return next(request);
  }
  return observeRequest(request, next, inject(BrowserTelemetry));
};

function observeRequest(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  telemetry: BrowserTelemetry,
): Observable<HttpEvent<unknown>> {
  const correlationId = createCorrelationId();
  const startedAt = performance.now();
  const operation = classifyOperation(request);
  let recorded = false;

  const record = (outcome: BrowserTelemetryOutcome, responseId?: string): void => {
    if (recorded) return;
    recorded = true;
    telemetry.record({
      name: 'http_request', operation, outcome,
      durationMs: Math.max(0, performance.now() - startedAt),
      correlationId: responseId ?? correlationId,
    });
  };

  return next(request.clone({ setHeaders: { 'X-Correlation-ID': correlationId } })).pipe(
    tap({
      next: event => {
        if (event instanceof HttpResponse) {
          record('success', event.headers.get('X-Correlation-ID') ?? undefined);
        }
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse) {
          record(classifyOutcome(error.status),
            error.headers.get('X-Correlation-ID') ?? undefined);
        } else {
          record('unexpected');
        }
      },
    }),
    finalize(() => record('cancelled')),
  );
}

function createCorrelationId(): string {
  return globalThis.crypto?.randomUUID?.() ??
    `web-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
```

`finalize` also runs after success and error, so the `recorded` guard is essential. Cancellation reaches `finalize` without a response and becomes a distinct outcome. Route classification maps session, schedule, assignment, and volunteer endpoints to the unions above; HTTP status classification maps validation, authentication, authorization, missing data, conflict, and transient availability failures without retaining response bodies.

Register telemetry before the runtime URL interceptor so it classifies the canonical `/api` route:

```typescript
provideHttpClient(withInterceptors([httpTelemetryInterceptor, apiBaseUrlInterceptor]))
```

## Keep Global Error Reporting Safe

The global handler reports only a fixed event. The console receives the error class, never its message or stack.

```typescript
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  constructor(private readonly telemetry: BrowserTelemetry) {}

  handleError(error: unknown): void {
    const summary = error instanceof Error ? error.name : 'Unknown error';
    this.telemetry.record({
      name: 'application_error', operation: 'application',
      outcome: 'unexpected', durationMs: 0,
    });
    console.error('Unexpected application error.', summary);
  }
}
```

## Enforce the Same Boundary in the API

TypeScript types do not protect a server. `BrowserTelemetryRequest` repeats the event, operation, and outcome allowlists, bounds duration to ten minutes, constrains correlation IDs, and uses `JsonExtensionData` to reject unknown properties. An accidental `volunteerName` field therefore returns `400` instead of being silently stored.

```csharp
public sealed partial class BrowserTelemetryRequest
{
  private static readonly HashSet<string> EventNames = ["http_request", "application_error"];
  private static readonly HashSet<string> Operations =
  [
    "application", "session.restore", "session.sign-in", "session.sign-out",
    "schedule.list", "schedule.detail", "schedule.create", "assignment.board",
    "assignment.create", "volunteer.search", "other",
  ];
  private static readonly HashSet<string> Outcomes =
  [
    "success", "cancelled", "validation", "unauthenticated", "forbidden",
    "not_found", "conflict", "unavailable", "unexpected",
  ];

  public string? Name { get; init; }
  public string? Operation { get; init; }
  public string? Outcome { get; init; }
  public double DurationMs { get; init; }
  public string? CorrelationId { get; init; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }

  public Dictionary<string, string[]> Validate()
  {
    var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    if (Name is null || !EventNames.Contains(Name))
      errors["name"] = ["The telemetry event name is not allowed."];
    if (Operation is null || !Operations.Contains(Operation))
      errors["operation"] = ["The telemetry operation is not allowed."];
    if (Outcome is null || !Outcomes.Contains(Outcome))
      errors["outcome"] = ["The telemetry outcome is not allowed."];
    if (!double.IsFinite(DurationMs) || DurationMs is < 0 or > 600_000)
      errors["durationMs"] = ["The telemetry duration is outside the allowed range."];
    if (CorrelationId is not null &&
        (CorrelationId.Length is < 8 or > 64 ||
         !ValidCorrelationId().IsMatch(CorrelationId)))
      errors["correlationId"] = ["The correlation identifier is invalid."];
    if (AdditionalProperties is { Count: > 0 })
      errors["payload"] = ["The telemetry payload contains unsupported properties."];
    return errors;
  }

  [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
  private static partial Regex ValidCorrelationId();
}
```

The endpoint records only validated dimensions and remains authenticated and antiforgery-protected because telemetry ingestion is still an application write:

```csharp
public static RouteHandlerBuilder MapBrowserTelemetryEndpoint(
    this IEndpointRouteBuilder endpoints) =>
    endpoints.MapPost("/api/telemetry", (
        BrowserTelemetryRequest request,
        ChurchServeDiagnostics diagnostics,
        ILogger<BrowserTelemetryRequest> logger) =>
    {
      var errors = request.Validate();
      if (errors.Count > 0) return Results.ValidationProblem(errors);

      diagnostics.RecordBrowserEvent(request);
      Activity.Current?.SetTag("churchserve.browser.event", request.Name);
      Activity.Current?.SetTag("churchserve.browser.operation", request.Operation);
      Activity.Current?.SetTag("churchserve.browser.outcome", request.Outcome);
      logger.LogInformation(
          "Browser event {EventName} for {Operation} completed with {Outcome} in " +
          "{DurationMs} ms and browser correlation {BrowserCorrelationId}",
          request.Name, request.Operation, request.Outcome,
          request.DurationMs, request.CorrelationId ?? "none");
      return Results.Accepted();
    }).ValidateAntiforgery().RequireAuthorization();
```

## Correlate Browser, Response, Trace, and Log

`CorrelationIdMiddleware` accepts a constrained caller ID or creates one from the current activity trace. It tags the activity, echoes the response header, and opens a structured logging scope.

```csharp
public async Task InvokeAsync(HttpContext context)
{
  var correlationId = ReadRequestId(
      context.Request.Headers[HeaderName].ToString()) ?? CreateId();
  context.Items[ItemKey] = correlationId;
  Activity.Current?.SetTag("churchserve.correlation_id", correlationId);
  context.Response.OnStarting(() =>
  {
    context.Response.Headers[HeaderName] = correlationId;
    return Task.CompletedTask;
  });

  using (logger.BeginScope(new Dictionary<string, object?>
  {
    ["CorrelationId"] = correlationId,
  }))
  {
    await next(context);
  }
}

private static string? ReadRequestId(string value) =>
    value.Length is >= 8 and <= 64 && ValidId().IsMatch(value) ? value : null;

private static string CreateId() =>
    Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId
        ? traceId
        : Guid.NewGuid().ToString("N");
```

Problem Details returns the same safe value as `traceId`, while exception details remain in server diagnostics:

```csharp
builder.Services.AddProblemDetails(options =>
{
  options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] =
        CorrelationIdMiddleware.Get(context.HttpContext);
});

app.UseCorrelationId();
app.UseExceptionHandler();
app.MapBrowserTelemetryEndpoint();
```

The correlation ID is a diagnostic reference, not authentication or authorization.

## Verify the Boundary

Angular tests prove that volunteer search text never enters the event, failures map to bounded outcomes, cancellation is distinct, and telemetry does not observe itself. API tests prove that valid authenticated events return `202`, anonymous writes return `401`, extra properties return `400`, invalid IDs are replaced, and handled exceptions return matching header and Problem Details references without the exception message.

Part 26-2 connects these safe events to structured logs, traces, metrics, and an optional OpenTelemetry collector.
