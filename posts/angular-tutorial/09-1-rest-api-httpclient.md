---
title: "ChurchServe Angular Tutorial Part 9-1: Connect to a REST API with HttpClient"
excerpt: "Replace ChurchServe's browser-only repository with a typed Angular HttpClient implementation, runtime DTO validation, explicit request states, and focused HTTP tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular HttpClient REST API tutorial"
  description: "Connect Angular to a REST API using HttpClient, Observable repository contracts, runtime DTO validation, request states, and HTTP tests."
  socialTitle: "ChurchServe Part 9-1: Angular HttpClient and REST"
  socialDescription: "Move ChurchServe from local storage to a tested Angular REST API client without coupling components to transport details."
---

# ChurchServe Angular Tutorial Part 9-1: Connect to a REST API with HttpClient

Part 8 stores ChurchServe data in one browser. A shared ministry schedule needs a server boundary, so Part 9-1 replaces the local-storage repository with an Angular `HttpClient` implementation.

The important change is not merely importing `HttpClient`. Network operations finish later, can fail independently, and return untrusted JSON. This article evolves the repository contract from synchronous values to Observables, validates responses at runtime, and keeps loading, missing, empty, and failed states distinct in the UI.

> **Quick answer:** Configure `HttpClient` once, return Observables from the repository contract, validate unknown response bodies before mapping them into domain models, represent request states explicitly, and test methods, URLs, bodies, responses, and errors with Angular's HTTP testing provider.

## Files Added and Changed

Part 9-1 replaces the Part 8 browser-persistence implementation with a transport-focused API folder:

```text
src/app/
  app.config.ts                                      updated
  app.routes.spec.ts                                updated
  features/
    schedule/
      api/                                           new
        http-service-event-repository.ts             new
        http-service-event-repository.spec.ts        new
        provide-service-event-api.ts                 new
        service-event-dto.ts                         new
        service-event-dto.spec.ts                    new
      service-event-repository.ts                    updated
      schedule.ts                                    updated
      schedule.spec.ts                               updated
      service-event-create/
        service-event-create.ts                     updated
        service-event-create.spec.ts                updated
      service-event-detail/
        service-event-detail.ts                     updated
        service-event-detail.html                   updated
        service-event-detail.scss                   updated
      persistence/                                   removed
```

`ChurchServe.slnx` and `README.md` also change. The `ServiceEventRepository` abstraction remains, but its operations become asynchronous and its implementation moves from browser storage to HTTP.

## Define the Client-Side API Contract

The Angular client expects these endpoints:

```text
GET  /api/service-events
GET  /api/service-events/{eventId}
POST /api/service-events
```

Part 9-1 deliberately focuses on the client boundary. Until an API implements these routes, the schedule displays its error state and create requests fail without losing the form values. Part 9-2 scaffolds the ASP.NET Core API, adds EF Core and SQLite, seeds the schedule, and makes the complete UI runnable.

A generic `HttpClient` type argument does not validate JSON at runtime. The client therefore receives response bodies as `unknown` and validates them before constructing domain values.

## Make the Repository Asynchronous

Update `service-event-repository.ts`:

```typescript
import { Observable } from 'rxjs';
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEvent } from './service-event';

export abstract class ServiceEventRepository {
  abstract list(): Observable<readonly ServiceEvent[]>;
  abstract findById(id: string): Observable<ServiceEvent | undefined>;
  abstract create(command: CreateServiceEventCommand): Observable<ServiceEvent>;
}
```

Part 8 could return immediately because browser storage is synchronous. HTTP cannot. Returning an Observable makes completion, cancellation, and failure explicit without putting `HttpClient` into components.

`findById` keeps “missing” separate from “failed.” A successful 404 mapping produces `undefined`; a network or server failure travels through the Observable error channel.

## Keep the Wire Contract Separate

Create `api/service-event-dto.ts`:

```typescript
import { ServiceEvent } from '../service-event';

export interface ServiceEventDto {
  readonly id: string;
  readonly title: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly location: string;
  readonly requiredPositions: number;
  readonly assignedPositions: number;
  readonly version: number;
}

export function parseServiceEvent(value: unknown): ServiceEvent {
  if (!isServiceEventDto(value)) {
    throw new Error('The service-event response did not match the expected contract.');
  }

  return {
    id: value.id,
    title: value.title,
    startsAt: value.startsAt,
    endsAt: value.endsAt,
    location: value.location,
    requiredPositions: value.requiredPositions,
    assignedPositions: value.assignedPositions,
  };
}

export function parseServiceEventList(value: unknown): readonly ServiceEvent[] {
  if (!Array.isArray(value)) {
    throw new Error('The service-event list response was not an array.');
  }

  return value.map(parseServiceEvent);
}

function isServiceEventDto(value: unknown): value is ServiceEventDto {
  return (
    isRecord(value) &&
    hasNonEmptyString(value, 'id') &&
    hasNonEmptyString(value, 'title') &&
    hasIsoDateString(value, 'startsAt') &&
    hasIsoDateString(value, 'endsAt') &&
    hasNonEmptyString(value, 'location') &&
    isNonNegativeInteger(value['requiredPositions']) &&
    isNonNegativeInteger(value['assignedPositions']) &&
    isNonNegativeInteger(value['version'])
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasNonEmptyString(value: Record<string, unknown>, key: string): boolean {
  return typeof value[key] === 'string' && value[key].trim().length > 0;
}

function hasIsoDateString(value: Record<string, unknown>, key: string): boolean {
  const candidate = value[key];
  return (
    typeof candidate === 'string' &&
    /^\d{4}-\d{2}-\d{2}T/.test(candidate) &&
    !Number.isNaN(Date.parse(candidate))
  );
}

function isNonNegativeInteger(value: unknown): boolean {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}
```

The API's `version` field is a transport concern for later concurrency work, so the mapper does not leak it into the current `ServiceEvent` model. Timestamps remain validated ISO strings because that is the established domain representation used by date pipes, HTML `datetime` attributes, commands, and storage in earlier parts.

This validator checks structure, not every business invariant. The API must still enforce authorization, time-range rules, staffing constraints, and concurrency because a browser client cannot be trusted.

## Implement the HTTP Repository

Create `api/http-service-event-repository.ts`:

```typescript
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of, throwError } from 'rxjs';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { ServiceEventRepository } from '../service-event-repository';
import { ServiceEvent } from '../service-event';
import { parseServiceEvent, parseServiceEventList } from './service-event-dto';

@Injectable()
export class HttpServiceEventRepository extends ServiceEventRepository {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/service-events';

  list(): Observable<readonly ServiceEvent[]> {
    return this.http.get<unknown>(this.baseUrl).pipe(map(parseServiceEventList));
  }

  findById(id: string): Observable<ServiceEvent | undefined> {
    return this.http.get<unknown>(`${this.baseUrl}/${encodeURIComponent(id)}`).pipe(
      map(parseServiceEvent),
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 404
          ? of(undefined)
          : throwError(() => error),
      ),
    );
  }

  create(command: CreateServiceEventCommand): Observable<ServiceEvent> {
    return this.http.post<unknown>(this.baseUrl, command).pipe(map(parseServiceEvent));
  }
}
```

Relative URLs keep deployment topology out of feature code. `encodeURIComponent` prevents an event ID containing reserved URL characters from changing the request path. Only a 404 becomes `undefined`; authentication, authorization, validation, network, and server errors remain failures for the caller to represent honestly.

Do not automatically retry `POST`. Without an idempotency strategy, a lost response followed by a retry can create duplicate events.

## Register HttpClient and the Repository

Create `api/provide-service-event-api.ts`:

```typescript
import { provideHttpClient } from '@angular/common/http';
import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { ServiceEventRepository } from '../service-event-repository';
import { HttpServiceEventRepository } from './http-service-event-repository';

export function provideServiceEventApi(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(),
    { provide: ServiceEventRepository, useClass: HttpServiceEventRepository },
  ]);
}
```

Use that provider group in `app.config.ts`:

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideServiceEventApi } from './features/schedule/api/provide-service-event-api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideServiceEventApi(),
  ],
};
```

Components still inject `ServiceEventRepository`. The composition root is the only place that chooses HTTP instead of local storage.

## Load the Schedule Asynchronously

The schedule already has loading, error, and ready states. Update `reload` to drive those states from the repository Observable:

```typescript
private readonly repository = inject(ServiceEventRepository);
private readonly destroyRef = inject(DestroyRef);

protected reload(): void {
  this.replaceState({ status: 'loading' });
  this.repository
    .list()
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (events) => this.replaceState({ status: 'ready', events }),
      error: () =>
        this.replaceState({
          status: 'error',
          message: 'The schedule could not be loaded. Check your connection and try again.',
        }),
    });
}
```

An empty successful array produces the existing empty state. A failed request produces the retry panel instead. `takeUntilDestroyed` prevents a response from updating a component after Angular destroys it.

## Navigate Only after POST Succeeds

Update the create page's command handler:

```typescript
private readonly router = inject(Router);
private readonly repository = inject(ServiceEventRepository);
private readonly destroyRef = inject(DestroyRef);

protected readonly saveError = signal('');

protected createEvent(command: CreateServiceEventCommand): void {
  this.saveError.set('');
  this.repository
    .create(command)
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (event) => void this.router.navigate(['/schedule', event.id]),
      error: () =>
        this.saveError.set(
          'The event could not be created. Check your connection and try again.',
        ),
    });
}
```

Navigation happens only when the API returns a validated created event. The user remains on the form after a failure, and the UI does not expose raw server details or stack traces.

## Distinguish Detail Loading, Missing, and Failure

The detail page can no longer call `findById` synchronously. Map each route ID into a request-state Observable:

```typescript
export type ServiceEventDetailState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; event: ServiceEvent | undefined };

protected readonly state = toSignal(
  this.route.paramMap.pipe(
    map((params) => params.get('eventId')),
    switchMap((id) =>
      (id ? this.repository.findById(id) : of(undefined)).pipe(
        map(
          (event): ServiceEventDetailState => ({
            status: 'ready',
            event,
          }),
        ),
        startWith<ServiceEventDetailState>({ status: 'loading' }),
        catchError(() =>
          of<ServiceEventDetailState>({
            status: 'error',
            message: 'The service event could not be loaded. Check your connection and try again.',
          }),
        ),
      ),
    ),
  ),
  { initialValue: { status: 'loading' } as ServiceEventDetailState },
);
```

The template switches on this state. Loading uses `role="status"`, a request failure uses `role="alert"`, and a successful `undefined` result renders “Service not found.” A slow server no longer causes a false missing-event message while the request is pending.

## Test Angular Requests without a Server

Angular's HTTP testing backend captures requests without making network calls:

```typescript
beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      HttpServiceEventRepository,
    ],
  });
  repository = TestBed.inject(HttpServiceEventRepository);
  http = TestBed.inject(HttpTestingController);
});

afterEach(() => http.verify());
```

Test a list request and mapping:

```typescript
it('lists and maps service events returned by the API', async () => {
  const result = firstValueFrom(repository.list());

  const request = http.expectOne('/api/service-events');
  expect(request.request.method).toBe('GET');
  request.flush([dto]);

  await expect(result).resolves.toEqual([
    expect.objectContaining({ id: 'event-1', title: 'Sunday Service' }),
  ]);
});
```

Test the create request body rather than only its response:

```typescript
it('posts a create command and maps the created event', async () => {
  const result = firstValueFrom(repository.create(command));

  const request = http.expectOne('/api/service-events');
  expect(request.request.method).toBe('POST');
  expect(request.request.body).toEqual(command);
  request.flush({ ...dto, ...command, id: 'event-2' });

  await expect(result).resolves.toEqual(expect.objectContaining({ id: 'event-2' }));
});
```

The completed tests also cover URL encoding, 404 mapping, malformed list and record responses, schedule failures and retry, successful navigation after creation, failed creation feedback, and the existing route behaviors. `http.verify()` ensures an unexpected or unconsumed request cannot pass unnoticed.

## Run the Part 9-1 Checks

```bash
npm test -- --watch=false
npm run build
```

The completed Part 9-1 client has ten test files and thirty-nine passing tests. The production build verifies the HTTP providers, Observable contract, templates, and every lazy route together.

To exercise the UI against real data, continue to Part 9-2 and run its API. The intentional error state in Part 9-1 is evidence that network failure is represented honestly rather than being confused with an empty schedule.

## Part 9-1 Checkpoint

ChurchServe's Angular client now uses a tested REST boundary instead of browser persistence. Components remain storage-agnostic, API data is validated before entering the domain, and the UI distinguishes loading, missing, empty, and failed outcomes.

Part 9-2 implements the matching ASP.NET Core endpoints and persists service events in SQLite. The later production database plan replaces SQLite with Microsoft SQL Server without changing the Angular repository contract.
