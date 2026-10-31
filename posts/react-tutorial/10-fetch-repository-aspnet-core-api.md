---
title: "ChurchServe React Tutorial Part 10: Fetch Repository and ASP.NET Core API"
excerpt: "Connect React to an ASP.NET Core API through a fetch-based repository with cancellation, runtime response validation, REST status handling, and development proxying."
category: "React Tutorial"

seo:
  focusKeyword: "React fetch ASP.NET Core API TypeScript"
  description: "Connect React and TypeScript to an ASP.NET Core Minimal API with a fetch repository, runtime validation, AbortSignal, REST responses, and Vite proxying."
  socialTitle: "React Fetch Repository with an ASP.NET Core API"
  socialDescription: "Build a tested HTTP boundary between React and ASP.NET Core with fetch, runtime schemas, cancellation, validation, and correct status codes."
---

# ChurchServe React Tutorial Part 10: Fetch Repository and ASP.NET Core API

[Part 9](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-9-async-repositories-and-request-cancellation/) made the repository asynchronous and abort-aware. The routes already understand loading, retry, not-found, pending, and failure states, but the selected implementation still reads local browser storage.

Part 10 connects that interface to a real HTTP boundary. React uses a fetch-based repository that forwards cancellation, checks status codes, parses JSON as `unknown`, and validates every successful response. An ASP.NET Core Minimal API owns seeded records, server-side validation, identity generation, and REST responses. Vite proxies `/api` in development so the browser still uses one origin.

> **Quick answer:** Keep `fetch` and HTTP details inside a repository adapter. Treat a resolved fetch promise as only a received response, not automatic success. Check `response.ok`, handle expected statuses such as `404`, validate successful JSON at runtime, and pass `AbortSignal` through unchanged. On the server, repeat validation, generate authoritative IDs, return `201 Created` with `Location`, and keep client and API on a deliberate same-origin boundary.

## What Part 10 adds

The main additions are:

```text
src/features/schedule/
  httpServiceEventRepository.ts
  httpServiceEventRepository.test.ts
  serviceEventValidation.ts
server/ChurchServe.Api/
  ChurchServe.Api.csproj
  Program.cs
  Properties/launchSettings.json
  ServiceEvents/
    CreateServiceEventRequest.cs
    ServiceEvent.cs
    ServiceEventStore.cs
    ServiceEventValidator.cs
```

The checkpoint also updates `App.tsx`, `router.tsx`, `vite.config.ts`, `package.json`, `.gitignore`, and `README.md`.

Schedule, detail, and creation pages do not change. They already depend on the Part 9 repository contract.

## Extract runtime response validation

Part 8 validated records restored from browser storage. HTTP responses need the same protection, so move the guards into `serviceEventValidation.ts` rather than duplicating them in each adapter.

The complete record guard checks both types and invariants:

```typescript
export function isServiceEvent(value: unknown): value is ServiceEvent {
  if (!isRecord(value)) {
    return false
  }

  const startsAt = Date.parse(String(value.startsAt))
  const endsAt = Date.parse(String(value.endsAt))

  return (
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.title) &&
    isNonEmptyString(value.startsAt) &&
    isNonEmptyString(value.endsAt) &&
    isNonEmptyString(value.location) &&
    !Number.isNaN(startsAt) &&
    !Number.isNaN(endsAt) &&
    endsAt > startsAt &&
    isNonNegativeInteger(value.requiredPositions) &&
    isNonNegativeInteger(value.assignedPositions) &&
    value.assignedPositions <= value.requiredPositions
  )
}
```

Expose parsers for one record and a collection:

```typescript
export function parseServiceEvent(value: unknown): ServiceEvent {
  if (!isServiceEvent(value)) {
    throw new InvalidServiceEventResponseError()
  }

  return { ...value }
}

export function parseServiceEvents(value: unknown): readonly ServiceEvent[] {
  if (!Array.isArray(value) || !value.every(isServiceEvent)) {
    throw new InvalidServiceEventResponseError()
  }

  return value.map((event) => ({ ...event }))
}
```

The parsed value begins as `unknown`. TypeScript cannot verify data that crossed a network merely because the client declared an interface.

Copying accepted records also prevents consumers from retaining references into a mutable decoded container.

## Represent HTTP failures separately

Create a status-aware error:

```typescript
export class ServiceEventHttpError extends Error {
  readonly status: number

  constructor(status: number) {
    super(`The service-event request failed with status ${status}.`)
    this.name = 'ServiceEventHttpError'
    this.status = status
  }
}
```

This error remains infrastructure-facing. Current route messages are intentionally generic, but diagnostics and later authentication behavior can distinguish `401`, `403`, `409`, `500`, and unavailable transport without parsing an error string.

Schema failure uses `InvalidServiceEventResponseError`. A `200` response with invalid JSON is not a successful domain result and is not the same failure as a `503` response.

## Implement the fetch repository

The adapter implements the existing interface and accepts an injectable fetch function:

```typescript
type Fetch = typeof fetch

export class HttpServiceEventRepository implements ServiceEventRepository {
  private readonly baseUrl: string
  private readonly fetcher: Fetch

  constructor(
    baseUrl = '/api/service-events',
    fetcher: Fetch = globalThis.fetch,
  ) {
    this.baseUrl = baseUrl.replace(/\/$/, '')
    this.fetcher = fetcher
  }
}
```

Injecting `fetch` avoids global replacement in tests and makes every request observable.

The default URL is relative. Development proxying and same-origin production hosting can route it without embedding an environment-specific host in the bundle.

## List events and forward cancellation

The list operation is small because status and schema handling are shared:

```typescript
readonly list = async (signal?: AbortSignal) => {
  const response = await this.fetcher(this.baseUrl, { signal })
  await ensureSuccess(response)
  return parseServiceEvents(await readJson(response))
}
```

`fetch` rejects for transport failures and aborts, but an HTTP error such as `404` or `500` still resolves to a `Response`. Check `response.ok`, which covers the `200` through `299` range:

```typescript
async function ensureSuccess(response: Response) {
  if (!response.ok) {
    throw new ServiceEventHttpError(response.status)
  }
}
```

Forwarding the route's signal allows Part 9 cleanup to cancel both the request and response-body read.

## Treat detail 404 as absence

A missing detail is part of the repository contract, not a generic route failure:

```typescript
readonly findById = async (eventId: string, signal?: AbortSignal) => {
  const response = await this.fetcher(
    `${this.baseUrl}/${encodeURIComponent(eventId)}`,
    { signal },
  )

  if (response.status === 404) {
    return undefined
  }

  await ensureSuccess(response)
  return parseServiceEvent(await readJson(response))
}
```

Encode the path segment. Route parameters are untrusted strings and can contain characters with URL meaning.

Only `404` becomes `undefined`. Authorization failure, server failure, and invalid response data remain failures so the detail page does not falsely tell the user that a record is absent.

## Post a creation command

Creation sends only the command fields:

```typescript
readonly create = async (
  command: CreateServiceEventCommand,
  signal?: AbortSignal,
): Promise<ServiceEvent> => {
  const response = await this.fetcher(this.baseUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(command),
    signal,
  })

  await ensureSuccess(response)
  return parseServiceEvent(await readJson(response))
}
```

The client does not send an ID or staffing counts. The server owns those fields and returns the created representation.

JSON reading is also an untrusted boundary:

```typescript
async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new InvalidServiceEventResponseError()
  }
}
```

Malformed JSON from a successful response is reported as an invalid response schema rather than a valid event or an unrelated status error.

## Move composition to the router boundary

`App` no longer constructs persistence. It receives the repository selected by its composition root:

```typescript
type AppProps = {
  repository: ServiceEventRepository
}

export function App({ repository }: AppProps) {
  return (
    <ServiceEventProvider repository={repository}>
      {/* application shell and Outlet */}
    </ServiceEventProvider>
  )
}
```

Create routes from an injected repository:

```typescript
export function createAppRoutes(
  repository: ServiceEventRepository,
): RouteObject[] {
  return [
    {
      path: '/',
      element: <App repository={repository} />,
      children: [
        // existing route objects
      ],
    },
  ]
}

export function createAppRouter() {
  return createBrowserRouter(
    createAppRoutes(new HttpServiceEventRepository()),
  )
}
```

Production browser startup selects HTTP. Route tests call `createAppRoutes(new InMemoryServiceEventRepository())`, keeping them fast and deterministic without a hidden test-only branch inside application code.

## Configure the development proxy

Add the API proxy to `vite.config.ts`:

```typescript
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5100',
    },
  },
})
```

The browser requests `/api/service-events` from the Vite origin. The development server forwards that path to ASP.NET Core.

This avoids development CORS configuration and mirrors the intended production boundary: one public origin serves the interface and API routes. If deployment later uses different origins, configure a narrow allowlist rather than enabling every origin.

## Create the ASP.NET Core project

The API uses .NET 10 and the Web SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

No extra package is needed for the first Minimal API boundary.

The launch profile listens on the proxy target, `http://localhost:5100`. HTTPS and production hosting will be configured at the deployment boundary rather than simulated in this checkpoint.

## Define API request and response records

The response record contains server-owned fields:

```csharp
public sealed record ServiceEvent(
    string Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    int RequiredPositions,
    int AssignedPositions);
```

The creation request permits missing values so validation can return useful field errors:

```csharp
public sealed record CreateServiceEventRequest(
    string? Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Location);
```

`DateTimeOffset` preserves an absolute time and its submitted offset. The client still renders it for the chosen display time zone.

## Repeat validation on the server

Client validation improves interaction but cannot protect the API. A caller can skip React entirely.

The server validator checks required text, length, required timestamps, and ordering:

```csharp
public static Dictionary<string, string[]> Validate(
    CreateServiceEventRequest request)
{
    var errors = new Dictionary<string, string[]>();

    ValidateText(request.Title, "title", "Service name", errors);
    ValidateText(request.Location, "location", "Location", errors);

    if (request.StartsAt is null)
    {
        errors["startsAt"] = ["Enter a valid start date and time."];
    }

    if (request.EndsAt is null)
    {
        errors["endsAt"] = ["Enter a valid end date and time."];
    }

    if (request.StartsAt is not null &&
        request.EndsAt is not null &&
        request.EndsAt <= request.StartsAt)
    {
        errors["timeRange"] = ["End time must be after start time."];
    }

    return errors;
}
```

The POST endpoint returns `Results.ValidationProblem(errors)`, producing a `400` Problem Details response instead of creating invalid state.

## Use a thread-safe checkpoint store

Part 10 deliberately avoids a database so the HTTP boundary remains visible. The singleton store locks access because ASP.NET Core handles concurrent requests:

```csharp
public IReadOnlyList<ServiceEvent> List()
{
    lock (_lock)
    {
        return _events.ToArray();
    }
}

public ServiceEvent? FindById(string eventId)
{
    lock (_lock)
    {
        return _events.Find(serviceEvent => serviceEvent.Id == eventId);
    }
}
```

Returning an array prevents callers from mutating the store's internal list.

Creation generates the authoritative ID and adds the record under the lock:

```csharp
public ServiceEvent Create(CreateServiceEventRequest request)
{
    var serviceEvent = new ServiceEvent(
        Guid.NewGuid().ToString(),
        request.Title!.Trim(),
        request.StartsAt!.Value,
        request.EndsAt!.Value,
        request.Location!.Trim(),
        0,
        0);

    lock (_lock)
    {
        _events.Add(serviceEvent);
    }

    return serviceEvent;
}
```

The null-forgiving operators are safe only because the endpoint calls this method after validation. Keeping creation behind that validated route is part of the invariant.

## Map REST endpoints and status codes

Register the singleton and map one route group:

```csharp
builder.Services.AddSingleton<ServiceEventStore>();

var app = builder.Build();
var serviceEvents = app.MapGroup("/api/service-events");

serviceEvents.MapGet("", (ServiceEventStore store) =>
    Results.Ok(store.List()));

serviceEvents.MapGet("/{eventId}", (string eventId, ServiceEventStore store) =>
    store.FindById(eventId) is { } serviceEvent
        ? Results.Ok(serviceEvent)
        : Results.NotFound());
```

The POST handler returns the created representation and its canonical path:

```csharp
serviceEvents.MapPost("", (
    CreateServiceEventRequest request,
    ServiceEventStore store) =>
{
    var errors = ServiceEventValidator.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var created = store.Create(request);
    return Results.Created($"/api/service-events/{created.Id}", created);
});
```

The result is `201 Created` with a `Location` header. Missing detail returns `404`, invalid creation returns `400`, and successful reads return `200`.

## Test the HTTP adapter

Adapter tests inject a typed mock fetch function and use real `Response` objects. They verify:

- the normalized collection URL and forwarded signal;
- URL encoding for record IDs;
- `404` becoming `undefined`;
- non-success status becoming `ServiceEventHttpError`;
- malformed JSON and invalid schemas being rejected;
- POST method, content type, body, signal, and validated created response.

For example:

```typescript
const fetcher = vi.fn<typeof fetch>().mockResolvedValue(
  new Response(null, { status: 404 }),
)
const repository = new HttpServiceEventRepository(
  '/api/service-events',
  fetcher,
)

await expect(repository.findById('missing/event')).resolves.toBeUndefined()
expect(fetcher).toHaveBeenCalledWith(
  '/api/service-events/missing%2Fevent',
  { signal: undefined },
)
```

Route tests continue to use the in-memory repository. They test UI behavior, while adapter tests own HTTP translation.

## Run and validate Part 10

Start the API:

```bash
npm run api
```

In another terminal, start Vite:

```bash
npm run dev
```

Run the automated checks:

```bash
npm run lint
npm run typecheck
npm test
npm run build
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj --configuration Release
```

The finalized checkpoint passes lint and strict TypeScript checking, runs 49 React tests across 9 files, builds the Vite production bundle, and builds the .NET 10 API with zero warnings.

A live API smoke test also verified:

- list returned two seeded events;
- known detail returned `200`;
- missing detail returned `404`;
- invalid creation returned `400`;
- valid creation returned `201` and a `Location` header;
- the created ID was immediately retrievable with `200`.

## Production boundaries

Part 10 establishes the transport contract, not a production data platform.

- The API store is process memory. Restarting the process resets it, and multiple instances would not share records.
- Authentication and authorization are not implemented. Future endpoints must enforce ministry and role permissions on the server.
- The same-origin proxy is development configuration. Production hosting must route `/api` and SPA fallback paths without rewriting one as the other.
- HTTPS is required outside local development.
- Problem Details should eventually include consistent trace identifiers while logs retain technical causes.
- The create endpoint needs idempotency or duplicate detection before automatic retries are safe.
- Persistence should move to a database with migrations and concurrency handling.
- Successful response validation protects the client from contract drift, but shared contract tests or generated clients can reduce accidental divergence.
- The API must add rate limiting, request-size limits, authentication, authorization, and operational telemetry as the system grows.

Part 11 can replace the singleton list with durable database persistence while preserving the HTTP repository contract and visible route states.

## What Part 10 established

ChurchServe now crosses a real browser-to-server boundary.

- React uses an injected fetch repository rather than browser storage.
- Fetch cancellation flows from route cleanup to the transport.
- HTTP statuses and domain absence remain distinct.
- Successful JSON is validated before entering UI state.
- ASP.NET Core owns validation, identity, and REST responses.
- A locked in-memory API store is safe for this single-process checkpoint.
- Vite proxies `/api` without broad development CORS.
- Adapter tests and live smoke tests verify both sides of the contract.

The application is now ready to replace process memory with durable server persistence without changing its React routes.
