---
title: "ChurchServe Angular Tutorial Part 21: Error Handling and Resilience"
excerpt: "Design ChurchServe to recover from validation, authentication, conflict, network, and server failures without hiding uncertainty from users."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular error handling resilience tutorial"
  description: "Build resilient Angular error handling with typed problems, safe retries, contextual recovery UI, and focused unit tests."
  socialTitle: "ChurchServe Part 21: Resilient Error Handling"
  socialDescription: "Turn failures into explicit, recoverable Angular application states instead of generic alerts and silent data loss."
---

# ChurchServe Angular Tutorial Part 21: Error Handling and Resilience

Part 20 reduced avoidable rendering work, but a fast application can still feel unreliable when every failed request produces the same message. Invalid event details, an expired session, insufficient permission, a version conflict, and a temporarily unavailable API require different recovery paths.

> **Quick answer:** Convert HTTP failures into a small application problem model, retry only transient reads, keep writes explicit, show validation beside the affected fields, and let each feature choose the recovery action it understands.

This part changes error behavior without changing the successful schedule workflow or the API contract.

## What We Are Adding

The new infrastructure lives under `core` because authentication, schedules, assignments, and volunteer search all use it.

```text
src/app/
  core/
    errors/
      global-error-handler.ts
      global-error-handler.spec.ts
    http/
      app-problem.ts
      app-problem.spec.ts
      retry-transient-read.ts
      retry-transient-read.spec.ts
  features/
    schedule/
      api/
        http-assignment-repository.ts
        http-service-event-repository.ts
      assignment-store.ts
      schedule-store.ts
      service-event-create/
      service-event-form/
    sign-in/sign-in.ts
    volunteers/
      api/http-volunteer-repository.ts
      volunteers.ts
```

Repositories normalize failures at the HTTP boundary. Stores and components receive application concepts rather than repeating numeric status-code checks.

## Model Failures the Application Understands

Create `src/app/core/http/app-problem.ts`:

```typescript
import { HttpErrorResponse } from '@angular/common/http';
import { MonoTypeOperatorFunction, catchError, throwError } from 'rxjs';

export type AppProblem =
  | { kind: 'validation'; fields: Readonly<Record<string, string>> }
  | { kind: 'unauthenticated' }
  | { kind: 'forbidden' }
  | { kind: 'not-found' }
  | { kind: 'conflict'; latestVersion?: number }
  | { kind: 'unavailable'; retryAfterSeconds?: number }
  | { kind: 'unexpected'; correlationId?: string };

export class AppProblemError extends Error {
  constructor(readonly problem: AppProblem) {
    super(`Application problem: ${problem.kind}`);
    this.name = 'AppProblemError';
  }
}

export function normalizeHttpProblem<T>(): MonoTypeOperatorFunction<T> {
  return (source) =>
    source.pipe(
      catchError((error: unknown) =>
        throwError(() =>
          error instanceof HttpErrorResponse || error instanceof AppProblemError
            ? asAppProblemError(error)
            : error,
        ),
      ),
    );
}

export function asAppProblemError(error: unknown): AppProblemError {
  return error instanceof AppProblemError ? error : new AppProblemError(toAppProblem(error));
}

export function toAppProblem(error: unknown): AppProblem {
  if (error instanceof AppProblemError) {
    return error.problem;
  }

  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'unexpected' };
  }

  switch (error.status) {
    case 400:
    case 422:
      return { kind: 'validation', fields: readValidationFields(error.error) };
    case 401:
      return { kind: 'unauthenticated' };
    case 403:
      return { kind: 'forbidden' };
    case 404:
      return { kind: 'not-found' };
    case 409:
      return { kind: 'conflict', latestVersion: readNumber(error.error, 'currentVersion') };
    case 0:
    case 429:
    case 502:
    case 503:
    case 504:
      return { kind: 'unavailable', retryAfterSeconds: readRetryAfter(error) };
    default:
      return { kind: 'unexpected', correlationId: readCorrelationId(error) };
  }
}

export function unexpectedProblemMessage(problem: AppProblem, fallback: string): string {
  return problem.kind === 'unexpected' && problem.correlationId
    ? `${fallback} Reference: ${problem.correlationId}.`
    : fallback;
}

function readValidationFields(value: unknown): Readonly<Record<string, string>> {
  if (!isRecord(value) || !isRecord(value['errors'])) {
    return {};
  }

  const fields: Record<string, string> = {};
  for (const [key, messages] of Object.entries(value['errors'])) {
    if (Array.isArray(messages)) {
      const message = messages.find((item): item is string => typeof item === 'string');
      if (message) {
        fields[key] = message;
      }
    }
  }
  return fields;
}

function readRetryAfter(error: HttpErrorResponse): number | undefined {
  const value = error.headers.get('Retry-After');
  if (!value || !/^\d+$/.test(value)) {
    return undefined;
  }

  const seconds = Number(value);
  return Number.isSafeInteger(seconds) ? seconds : undefined;
}

function readCorrelationId(error: HttpErrorResponse): string | undefined {
  const header = error.headers.get('X-Correlation-ID');
  if (header) {
    return header;
  }

  if (!isRecord(error.error)) {
    return undefined;
  }

  const value = error.error['traceId'] ?? error.error['correlationId'];
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}

function readNumber(value: unknown, property: string): number | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const candidate = value[property];
  return typeof candidate === 'number' && Number.isFinite(candidate) ? candidate : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
```

The mapper reads only fields ChurchServe deliberately supports. It does not display an arbitrary API `message`, `title`, or response body. That prevents implementation details and potentially sensitive content from becoming user-facing text.

`normalizeHttpProblem` also preserves non-HTTP exceptions. A malformed successful response is a client contract failure, not a server availability problem, so the DTO parser's original error remains available to tests and diagnostics.

## Retry Only Safe Reads

A brief network interruption or `503 Service Unavailable` can justify retrying a `GET`. Validation, authentication, and authorization failures will not become successful because the same request is repeated.

Create `src/app/core/http/retry-transient-read.ts`:

```typescript
import { MonoTypeOperatorFunction, retry, throwError, timer } from 'rxjs';
import { toAppProblem } from './app-problem';

export interface RetryTransientReadOptions {
  readonly count?: number;
  readonly initialDelayMs?: number;
  readonly maximumDelayMs?: number;
}

export function retryTransientRead<T>(
  options: RetryTransientReadOptions = {},
): MonoTypeOperatorFunction<T> {
  const count = options.count ?? 2;
  const initialDelayMs = options.initialDelayMs ?? 250;
  const maximumDelayMs = options.maximumDelayMs ?? 2_000;

  return retry({
    count,
    delay: (error: unknown, retryCount) => {
      const problem = toAppProblem(error);
      if (problem.kind !== 'unavailable') {
        return throwError(() => error);
      }

      const retryAfterMs =
        problem.retryAfterSeconds === undefined ? undefined : problem.retryAfterSeconds * 1_000;
      if (retryAfterMs !== undefined && retryAfterMs > maximumDelayMs) {
        return throwError(() => error);
      }

      const backoffMs = Math.min(maximumDelayMs, initialDelayMs * 2 ** (retryCount - 1));
      return timer(Math.max(retryAfterMs ?? 0, backoffMs));
    },
  });
}
```

The default sequence waits 250 milliseconds and then 500 milliseconds. If `Retry-After` fits inside the configured delay budget, ChurchServe waits at least that long. If it exceeds the budget, the request fails immediately instead of retrying sooner than the server allowed. A larger deployment could add jitter when many clients might retry together.

Use the operator on repository reads before mapping the DTO and normalizing the final failure:

```typescript
return this.http
  .get<unknown>(this.baseUrl, { params })
  .pipe(retryTransientRead(), map(parseServiceEventPage), normalizeHttpProblem());
```

The assignment-board and volunteer repositories use the same ordering:

```typescript
return this.http
  .get<unknown>(`/api/service-events/${encodeURIComponent(eventId)}/assignment-board`)
  .pipe(retryTransientRead(), map(parseAssignmentBoard), normalizeHttpProblem());
```

```typescript
return this.http
  .get<unknown>(this.baseUrl, { params })
  .pipe(retryTransientRead(), map(parseVolunteerList), normalizeHttpProblem());
```

The create-event and assign-volunteer `POST` requests are deliberately not retried. The assignment API has a client request ID for idempotency, but automatic write retries still require an explicit decision about timeouts and confirmation behavior. ChurchServe keeps uncertain outcomes visible instead of assuming a write failed.

## Preserve Domain-Specific Conflicts

Part 18 already returns the latest authoritative assignment board with a `409 Conflict`. That response contains more recovery information than the generic problem model, so the repository keeps its specialized error and normalizes other failures:

```typescript
catchError((error: unknown) => {
  if (error instanceof HttpErrorResponse && error.status === 409) {
    const conflict = parseAssignmentConflict(error.error);
    return throwError(() => new AssignmentConflictError(conflict.message, conflict.board));
  }

  return throwError(() =>
    error instanceof HttpErrorResponse || error instanceof AppProblemError
      ? asAppProblemError(error)
      : error,
  );
})
```

The assignment store continues to replace optimistic state with the server board after a conflict. Other failures remove the pending assignment and choose a contextual message:

```typescript
function assignmentSaveMessage(error: unknown): string {
  const problem = toAppProblem(error);
  switch (problem.kind) {
    case 'unauthenticated':
      return 'Your session expired. The assignment was not saved.';
    case 'forbidden':
      return 'Your account does not have permission to assign volunteers.';
    case 'validation':
      return 'The assignment was rejected. Refresh the schedule and try again.';
    case 'unavailable':
      return 'The assignment was not saved because the service is unavailable. Try again.';
    default:
      return unexpectedProblemMessage(problem, 'The assignment was not saved. Try again.');
  }
}
```

This is different from retrying a page load. A user can safely request the same schedule again. Repeating a command without knowing its server outcome needs stronger guarantees.

## Put Server Validation Beside the Field

Client validation improves feedback, but the server remains authoritative. Add this method to `ServiceEventForm`:

```typescript
applyServerErrors(fields: Readonly<Record<string, string>>): void {
  for (const [field, message] of Object.entries(fields)) {
    const control =
      field === 'title'
        ? this.form.controls.title
        : field === 'startsAt'
          ? this.form.controls.startsAt
          : field === 'endsAt'
            ? this.form.controls.endsAt
            : field === 'location'
              ? this.form.controls.location
              : undefined;

    if (control) {
      control.setErrors({ ...control.errors, server: message });
      control.markAsTouched();
    }
  }
}
```

Only known controls are accepted. Unknown keys are ignored rather than injected into the page. Each field renders its server message before its local fallback:

```html
@if (form.controls.title.touched && form.controls.title.invalid) {
  <p id="service-title-error" class="field-error">
    {{ form.controls.title.getError('server') ?? 'Enter a service name.' }}
  </p>
}
```

The create page interprets the normalized problem and leaves the form untouched when saving fails:

```typescript
error: (error: unknown) => {
  const problem = toAppProblem(error);
  if (problem.kind === 'validation' && Object.keys(problem.fields).length > 0) {
    this.eventForm()?.applyServerErrors(problem.fields);
    this.saveError.set('Review the highlighted fields and try again.');
    return;
  }

  this.saveError.set(createEventMessage(problem));
},
```

```typescript
function createEventMessage(problem: ReturnType<typeof toAppProblem>): string {
  switch (problem.kind) {
    case 'unauthenticated':
      return 'Your session expired. Sign in again before creating this event.';
    case 'forbidden':
      return 'Your account does not have permission to create service events.';
    case 'conflict':
      return 'The schedule changed while you were editing. Review it and try again.';
    case 'unavailable':
      return 'The event was not saved because the service is unavailable. Your entries are still here.';
    default:
      return unexpectedProblemMessage(problem, 'The event could not be created. Try again.');
  }
}
```

Because the component resets the form only after success, a coordinator can retry without re-entering the event.

## Recover Where the Context Is Known

The schedule store distinguishes an expired session from temporary availability:

```typescript
function scheduleLoadMessage(error: unknown): string {
  const problem = toAppProblem(error);
  switch (problem.kind) {
    case 'unauthenticated':
      return 'Your session expired. Sign in again, then retry the schedule.';
    case 'forbidden':
      return 'Your account does not have permission to view this schedule.';
    case 'unavailable':
      return 'The schedule service is temporarily unavailable. Try again.';
    default:
      return unexpectedProblemMessage(problem, 'The schedule could not be loaded. Try again.');
  }
}
```

Volunteer search and assignment loading use equivalent context-specific functions. Sign-in treats `401` as invalid credentials because that meaning is correct for the sign-in command, while session restoration treats the same status as an anonymous session.

This policy does not belong in an interceptor that displays a toast for every failure. An interceptor sees transport details, but it cannot know whether a user was signing in, refreshing a background list, or submitting an important command.

## Report Unexpected Angular Errors Safely

Expected request failures remain inside feature state. Angular still needs a last-resort handler for programming errors that escape components and subscriptions.

Create `src/app/core/errors/global-error-handler.ts`:

```typescript
import { ErrorHandler, Injectable } from '@angular/core';

@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  handleError(error: unknown): void {
    const summary = error instanceof Error ? error.name : 'Unknown error';
    console.error('Unexpected application error.', summary);
  }
}
```

Register it in `app.config.ts`:

```typescript
import { ErrorHandler } from '@angular/core';
import { GlobalErrorHandler } from './core/errors/global-error-handler';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideHttpClient(),
    provideAppInitializer(() => firstValueFrom(inject(SessionStore).restore())),
    provideRouter(routes),
  ],
};
```

This tutorial handler logs only the error category. A production reporter can send a correlation ID, route name, release version, and safe diagnostic metadata, but should not copy credentials, request bodies, volunteer names, or contact information into a second data store.

## Test the Failure Policy

The mapper test checks the public model rather than the original response text:

```typescript
it('maps validation problem details without exposing unrelated response data', () => {
  const problem = toAppProblem(
    new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Internal validation details',
        errors: { title: ['A service name is required.'], ignored: [42] },
      },
    }),
  );

  expect(problem).toEqual({
    kind: 'validation',
    fields: { title: 'A service name is required.' },
  });
});
```

Fake time proves that a transient read is attempted no more than configured:

```typescript
it('retries a transient read with bounded exponential backoff', async () => {
  vi.useFakeTimers();
  let attempts = 0;
  const result = firstValueFrom(
    defer(() => {
      attempts++;
      return attempts < 3
        ? throwError(() => new HttpErrorResponse({ status: 503 }))
        : of('loaded');
    }).pipe(retryTransientRead({ count: 2, initialDelayMs: 10, maximumDelayMs: 20 })),
  );

  await vi.runAllTimersAsync();

  await expect(result).resolves.toBe('loaded');
  expect(attempts).toBe(3);
});
```

The form test verifies that a server message is visible and unknown field keys are ignored. The create-page test verifies that entered values remain after a rejected write. Separate tests cover authentication mapping, correlation references, non-transient failures, conflict recovery, and sanitized global reporting.

## Try the Recovery UI

Run the API and Angular application normally, then use these checks:

1. Open a populated schedule and stop the API.
2. Change the month or choose **Try again**. ChurchServe performs the bounded read retries and then shows the availability message.
3. Open **Create service**, enter valid values, and submit while the API is stopped. The page says the event was not saved and retains every entry.
4. Restart the API and submit again. The successful workflow opens the new event detail page.
5. Sign out or invalidate the authentication cookie, then request protected data. The feature reports that the session expired instead of describing it as a connectivity failure.

Browser developer tools can simulate offline reads, but stopping the local API also exercises the Angular proxy and real repository boundary. Do not use repeated write submissions as a retry test unless the first request's result is known.

## What Part 21 Does Not Implement

This part does not create an offline write queue. Durable offline writes require storage encryption decisions, expiry, replay ordering, idempotency, conflict resolution, and UI that distinguishes pending work from confirmed work.

It also does not treat automatic retry as a substitute for monitoring. Correlation IDs are useful only when the server and telemetry pipeline preserve them. Part 26 will add the wider observability design; Part 21 establishes the safe client boundary it can build on.

## Result

ChurchServe now classifies failures, retries only bounded transient reads, keeps write outcomes honest, preserves unsaved form values, and presents recovery choices in the feature that understands them. Part 22 builds on this boundary by tightening authorization and privacy across the application.
