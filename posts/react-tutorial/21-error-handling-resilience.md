---
title: "ChurchServe React Tutorial Part 21: Error Handling and Resilience"
excerpt: "Classify HTTP failures, retry only transient reads, preserve uncertain writes, show server validation beside fields, and add safe route recovery."
category: "React Tutorial"

seo:
  focusKeyword: "React error handling resilience tutorial"
  description: "Build resilient React error handling with typed problems, bounded read retries, contextual recovery UI, field validation, and route boundaries."
  socialTitle: "ChurchServe React Part 21: Resilient Error Handling"
  socialDescription: "Turn HTTP, validation, authentication, and unexpected failures into explicit React recovery states."
---

# ChurchServe React Tutorial Part 21: Error Handling and Resilience

Part 20 reduced avoidable rendering work, but a fast application still feels unreliable when every failure produces the same message. Invalid event details, an expired session, insufficient permission, a version conflict, and a temporarily unavailable API need different recovery paths.

> **Quick answer:** Convert HTTP failures into a small application-problem model at the repository boundary. Retry only transient reads with a bounded delay, keep writes single-attempt unless their outcome is known, show server validation beside recognized fields, and let the feature with the necessary context choose the recovery UI.

This part changes failure behavior without changing the successful schedule workflow or the API contract.

## Add a small problem vocabulary

Create `src/core/http/appProblem.ts`:

```typescript
export type AppProblem =
  | {
      readonly kind: 'validation'
      readonly fields: Readonly<Record<string, string>>
    }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'conflict' }
  | {
      readonly kind: 'unavailable'
      readonly retryAfterSeconds?: number
    }
  | {
      readonly kind: 'unexpected'
      readonly correlationId?: string
    }

export class AppProblemError extends Error {
  readonly problem: AppProblem

  constructor(problem: AppProblem) {
    super(`Application problem: ${problem.kind}`)
    this.name = 'AppProblemError'
    this.problem = problem
  }
}
```

This is not a copy of every possible server response. It is the set of distinctions the browser can act on:

- validation can highlight fields;
- unauthenticated users need a new session;
- forbidden users need different permissions;
- not-found details should offer navigation rather than retry;
- conflicts may have domain-specific reconciliation;
- unavailable reads may be retried;
- unexpected failures may include a safe support reference.

## Remember that Fetch does not reject HTTP errors

The browser resolves `fetch()` when it receives an HTTP response, including `404` and `503`. Only network-level failures and certain request errors reject the promise. MDN documents this behavior in its [`fetch()` reference](https://developer.mozilla.org/en-US/docs/Web/API/Window/fetch).

Repositories must check `response.ok` or `response.status` explicitly:

```typescript
async function ensureSuccess(response: Response) {
  if (!response.ok) throw await responseProblem(response)
}
```

`responseProblem` reads only supported problem fields:

```typescript
export async function responseProblem(response: Response) {
  const body = await readProblemBody(response)
  const problem = problemForStatus(response.status, body)

  if (problem.kind === 'unavailable') {
    return new AppProblemError({
      ...problem,
      retryAfterSeconds: readRetryAfter(response),
    })
  }

  if (problem.kind === 'unexpected') {
    return new AppProblemError({
      ...problem,
      correlationId: readCorrelationId(response, body),
    })
  }

  return new AppProblemError(problem)
}
```

Do not show arbitrary `title`, `detail`, `message`, or response-body text. Those fields may contain implementation details or sensitive data. ChurchServe accepts validation messages for recognized fields and a correlation reference for support; it supplies its own user-facing prose for everything else.

## Normalize validation fields safely

ASP.NET Core validation problems use an `errors` object whose values are arrays. Read the first nonblank string and normalize the first character of the key:

```typescript
function readValidationFields(
  value: unknown,
): Readonly<Record<string, string>> {
  if (!isRecord(value) || !isRecord(value.errors)) return {}

  const fields: Record<string, string> = {}
  for (const [key, messages] of Object.entries(value.errors)) {
    if (!Array.isArray(messages)) continue
    const message = messages.find(
      (item): item is string =>
        typeof item === 'string' && item.trim().length > 0,
    )
    if (message) fields[lowerFirst(key)] = message
  }
  return fields
}
```

The general mapper does not decide which fields exist on a particular form. That second allowlist belongs to the feature.

## Keep contract errors distinct from availability

A browser network failure commonly appears as a `TypeError`. Normalize that at the repository boundary:

```typescript
export function normalizeNetworkError(error: unknown): unknown {
  return error instanceof TypeError
    ? new AppProblemError({ kind: 'unavailable' })
    : error
}
```

Do not convert every exception into `unavailable`. A successful `200` response that fails runtime DTO validation is a contract defect, not a temporary connectivity problem. Preserving its original error helps tests and diagnostics identify the real boundary that failed.

## Retry only transient reads

Create `retryTransientRead.ts`. The helper retries only an `unavailable` problem:

```typescript
export async function retryTransientRead<T>(
  operation: () => Promise<T>,
  options: RetryTransientReadOptions = {},
  signal?: AbortSignal,
): Promise<T> {
  const count = options.count ?? 2
  const initialDelayMs = options.initialDelayMs ?? 250
  const maximumDelayMs = options.maximumDelayMs ?? 2_000
  const delay = options.delay ?? abortableDelay

  for (let attempt = 0; ; attempt++) {
    signal?.throwIfAborted()

    try {
      return await operation()
    } catch (caught) {
      const error = normalizeNetworkError(caught)
      const problem = toAppProblem(error)
      if (problem.kind !== 'unavailable' || attempt >= count) {
        throw error
      }

      const retryAfterMs =
        problem.retryAfterSeconds === undefined
          ? undefined
          : problem.retryAfterSeconds * 1_000

      if (retryAfterMs !== undefined && retryAfterMs > maximumDelayMs) {
        throw error
      }

      const backoffMs = Math.min(
        maximumDelayMs,
        initialDelayMs * 2 ** attempt,
      )
      await delay(Math.max(retryAfterMs ?? 0, backoffMs), signal)
    }
  }
}
```

The default waits are 250 and 500 milliseconds. A numeric `Retry-After` is honored only when it fits inside the client’s delay budget. If the server requests a longer wait, ChurchServe stops instead of retrying earlier than allowed.

The delay observes the same `AbortSignal` as the request. Leaving the route during backoff should cancel the recovery attempt rather than start an obsolete request.

## Apply retry around the complete read

Wrap the fetch, status mapping, JSON parsing, and DTO validation in one operation:

```typescript
readonly list = async (signal?: AbortSignal) => {
  return retryTransientRead(async () => {
    const response = await this.fetcher(this.baseUrl, {
      credentials: 'same-origin',
      signal,
    })
    await ensureSuccess(response)
    return parseServiceEvents(await readJson(response))
  }, undefined, signal)
}
```

`list`, event detail, assignment-board loading, and session restoration are safe reads. Validation, authentication, authorization, not-found, conflict, and malformed-success failures stop immediately.

## Do not automatically retry writes

Event creation, volunteer assignment, sign-in, and sign-out stay single-attempt:

```typescript
try {
  const response = await this.fetcher(this.baseUrl, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': readAntiforgeryToken(this.cookieSource),
    },
    body: JSON.stringify(command),
    signal,
  })
  await ensureSuccess(response)
  return parseServiceEvent(await readJson(response))
} catch (error) {
  throw normalizeNetworkError(error)
}
```

A timeout does not prove that a POST failed. The server may have committed the change before the response was lost. Retrying automatically could duplicate a non-idempotent operation.

The assignment endpoint already accepts a client request ID, but automatic replay is still a product decision involving confirmation and conflict behavior. Part 21 keeps uncertain outcomes visible.

## Preserve specialized assignment conflicts

Part 18 returns an authoritative assignment board with `409 Conflict`. That contains more recovery information than a generic `{ kind: 'conflict' }` value:

```typescript
if (response.status === 409) {
  const conflict = parseAssignmentConflict(await readJson(response))
  throw new AssignmentConflictError(
    conflict.message,
    conflict.board,
  )
}
```

The assignment store continues to replace optimistic state with that server board. Other failures roll back only the matching pending assignment and use a contextual message:

```typescript
function assignmentSaveMessage(error: unknown) {
  const problem = toAppProblem(error)

  switch (problem.kind) {
    case 'unauthenticated':
      return 'Your session expired. The assignment was not saved.'
    case 'forbidden':
      return 'Your account does not have permission to assign volunteers.'
    case 'validation':
      return 'The assignment was rejected. Refresh the schedule and try again.'
    case 'unavailable':
      return 'The assignment was not saved because the service is unavailable. Try again.'
    default:
      return unexpectedProblemMessage(
        problem,
        'The assignment was not saved. Try again.',
      )
  }
}
```

The store has enough context to say that a write was not confirmed. A generic HTTP layer does not.

## Show server validation beside known fields

The create page converts the general field dictionary to the form’s exact type:

```typescript
function serviceEventServerErrors(
  fields: Readonly<Record<string, string>>,
): ServiceEventFormErrors {
  const errors: ServiceEventFormErrors = {}

  for (const field of [
    'title',
    'startsAt',
    'endsAt',
    'location',
  ] as const) {
    if (fields[field]) errors[field] = fields[field]
  }

  return errors
}
```

Unknown server keys are ignored. They are not inserted into the DOM or treated as selectors.

`ServiceEventForm` merges the server errors with its client-side errors during rendering:

```typescript
const displayedErrors = { ...errors, ...serverErrors }
const showErrorSummary =
  hasSubmitted || Object.keys(serverErrors ?? {}).length > 0
```

Editing a field tells the parent to clear the previous server-error set. A later submission may return a new authoritative validation result.

Because the form resets only after `onSubmit` resolves, a rejected write preserves every entered value. The summary explains the overall failure while the field message explains what to correct.

## Choose recovery where context exists

The schedule store distinguishes session and permission failures from temporary availability:

```typescript
function scheduleLoadMessage(error: unknown) {
  const problem = toAppProblem(error)

  switch (problem.kind) {
    case 'unauthenticated':
      return 'Your session expired. Sign in again, then retry the schedule.'
    case 'forbidden':
      return 'Your account does not have permission to view this schedule.'
    case 'unavailable':
      return 'The schedule service is temporarily unavailable. Try again.'
    default:
      return unexpectedProblemMessage(
        problem,
        'The schedule could not be loaded. Try again.',
      )
  }
}
```

The event-detail loader converts application problems into route responses. Its error page can render not-found navigation, authentication guidance, authorization guidance, transient retry, or an unexpected-failure message without exposing the underlying response body.

Sign-in interprets `401` as incorrect credentials because that meaning is correct for the sign-in command. Session restoration interprets the same status as an anonymous session. Numeric status alone never provides enough user context.

## Add a last-resort route error boundary

Expected request failures stay in feature state. Unexpected errors from route loading or rendering still need a safe page rather than a blank screen.

React Router recommends a root `ErrorBoundary` and exposes caught route errors through [`useRouteError`](https://reactrouter.com/api/hooks/useRouteError):

```tsx
export function AppErrorPage() {
  const error = useRouteError()
  const summary = isRouteErrorResponse(error)
    ? `Route response ${error.status}`
    : error instanceof Error
      ? error.name
      : 'Unknown error'

  useEffect(() => {
    console.error('Unexpected application error.', summary)
  }, [summary])

  return (
    <main id="main-content" className="session-panel" tabIndex={-1}>
      <h1>ChurchServe could not open this page</h1>
      <p>Reload the page. If the problem continues, contact support.</p>
      <button type="button" onClick={() => window.location.reload()}>
        Reload ChurchServe
      </button>
    </main>
  )
}
```

Register it on the root route:

```tsx
{
  path: '/',
  element: <App sessionClient={sessionClient} />,
  ErrorBoundary: AppErrorPage,
  children: [/* ... */],
}
```

This tutorial logs only the error category. Production reporting may add a release identifier, route name, and correlation ID, but should not copy credentials, request bodies, volunteer names, or contact information into another system.

## Test the policy, not just the wording

The focused tests prove that:

- supported status codes map to the intended problem kinds;
- validation mapping ignores non-string and unrelated response data;
- an unexpected problem exposes only its safe correlation reference;
- network `TypeError` becomes unavailable while DTO errors retain identity;
- unavailable reads use bounded exponential delays;
- authentication and validation problems are never retried;
- a `Retry-After` outside the budget stops immediately;
- repository reads retry, while event creation makes one request;
- known server validation appears beside its field and unknown keys stay hidden;
- entered form values survive rejected writes;
- the root error boundary shows recovery UI and logs only an error category.

Injected delay functions make retry tests deterministic without real timers. Repository integration tests separately verify the Fetch boundary.

## What Part 21 does not implement

This part does not create an offline write queue. Durable offline commands require storage security, expiry, replay ordering, idempotency, conflict resolution, and UI that distinguishes pending work from confirmed work.

It also does not treat retry as monitoring. Correlation references become useful only when server logs and telemetry preserve them. A later observability part can build on this safe client boundary.

## Verify the application

Part 21 passes the architecture rule, linter, strict TypeScript checks, production Vite build, 111 React tests, and 16 API tests. Statement coverage is 90.31 percent.

## Result

ChurchServe now classifies failures, retries only bounded transient reads, keeps write outcomes honest, preserves unsaved values, shows authoritative validation beside recognized fields, and provides a safe last-resort route boundary.

Part 22 will build on this boundary by tightening authorization and privacy across the application.
