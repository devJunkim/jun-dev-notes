---
title: "ChurchServe Angular Tutorial Part 10-2: RxJS Search, Cancellation, and Request State"
excerpt: "Build responsive Angular volunteer search with runtime DTO validation, debouncing, switchMap cancellation, explicit request state, retry behavior, and deterministic tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular RxJS search tutorial"
  description: "Build tested Angular search with RxJS debouncing, switchMap cancellation, explicit request state, runtime DTO validation, and retry behavior."
  socialTitle: "ChurchServe Part 10-2: Reliable RxJS Search"
  socialDescription: "Build responsive volunteer search that cancels stale requests and exposes honest loading, empty, and error states."
---

# ChurchServe Angular Tutorial Part 10-2: RxJS Search, Cancellation, and Request State

Part 10-1 added a SQLite-backed `GET /api/volunteers?query={name}` endpoint. The Angular page now needs to coordinate rapidly changing queries without allowing an older HTTP response to replace newer results.

> **Quick answer:** Treat search text as a stream, normalize it, reject short terms, place the debounce timer inside `switchMap`, validate `unknown` HTTP responses, model request state explicitly, and test time and unsubscription instead of only the final array.

## What Part 10-2 Adds

```text
src/app/
  app.config.ts
  features/
    schedule/
      assignment.ts
      assignment-store.ts
      assignment-store.spec.ts
    volunteers/
      api/
        http-volunteer-repository.spec.ts
        http-volunteer-repository.ts
        provide-volunteer-api.ts
        volunteer-dto.spec.ts
        volunteer-dto.ts
      volunteer-repository.ts
      volunteer.ts
      volunteers.html
      volunteers.scss
      volunteers.spec.ts
      volunteers.ts
```

The UI has idle, loading, loaded, empty, and failed states. It retries an unchanged query and immediately cancels stale debounce timers or HTTP subscriptions.

## Give the Volunteer Feature Its Model

Move `Volunteer` from the schedule assignment file to the feature that owns it:

```typescript
// src/app/features/volunteers/volunteer.ts
export interface Volunteer {
  readonly id: string;
  readonly displayName: string;
  readonly eligibleMinistries: readonly string[];
}
```

Remove that interface from `schedule/assignment.ts`, then update `assignment-store.ts` and its test:

```typescript
import { Volunteer } from '../volunteers/volunteer';
import { Assignment, MinistryPosition } from './assignment';
```

## Define a Use-Case Boundary

The component should ask to search volunteers without knowing about URLs, query parameters, or JSON parsing.

```typescript
// src/app/features/volunteers/volunteer-repository.ts
import { Observable } from 'rxjs';
import { Volunteer } from './volunteer';

export abstract class VolunteerRepository {
  abstract search(query: string): Observable<readonly Volunteer[]>;
}
```

## Validate the HTTP Response

TypeScript types do not validate network data at runtime. Request `unknown` and convert it only after checking every required field.

```typescript
// src/app/features/volunteers/api/volunteer-dto.ts
import { Volunteer } from '../volunteer';

interface VolunteerDto {
  readonly id: string;
  readonly displayName: string;
  readonly eligibleMinistries: readonly string[];
}

export function parseVolunteerList(value: unknown): readonly Volunteer[] {
  if (!Array.isArray(value)) {
    throw new Error('The volunteer response was not an array.');
  }

  return value.map(parseVolunteer);
}

function parseVolunteer(value: unknown): Volunteer {
  if (!isVolunteerDto(value)) {
    throw new Error('The API returned an invalid volunteer.');
  }

  return {
    id: value.id,
    displayName: value.displayName,
    eligibleMinistries: [...value.eligibleMinistries],
  };
}

function isVolunteerDto(value: unknown): value is VolunteerDto {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return (
    typeof candidate['id'] === 'string' &&
    candidate['id'].length > 0 &&
    typeof candidate['displayName'] === 'string' &&
    candidate['displayName'].length > 0 &&
    Array.isArray(candidate['eligibleMinistries']) &&
    candidate['eligibleMinistries'].every(
      (ministry: unknown) => typeof ministry === 'string' && ministry.length > 0,
    )
  );
}
```

## Implement and Register the HTTP Adapter

`HttpParams` safely encodes spaces and reserved characters.

```typescript
// src/app/features/volunteers/api/http-volunteer-repository.ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { Volunteer } from '../volunteer';
import { VolunteerRepository } from '../volunteer-repository';
import { parseVolunteerList } from './volunteer-dto';

@Injectable()
export class HttpVolunteerRepository extends VolunteerRepository {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/volunteers';

  search(query: string): Observable<readonly Volunteer[]> {
    const params = new HttpParams().set('query', query);
    return this.http.get<unknown>(this.baseUrl, { params }).pipe(map(parseVolunteerList));
  }
}
```

Bind the abstraction to the HTTP implementation:

```typescript
// src/app/features/volunteers/api/provide-volunteer-api.ts
import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { VolunteerRepository } from '../volunteer-repository';
import { HttpVolunteerRepository } from './http-volunteer-repository';

export function provideVolunteerApi(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: VolunteerRepository, useClass: HttpVolunteerRepository },
  ]);
}
```

Import `provideVolunteerApi` in `app.config.ts` and add `provideVolunteerApi()` to its providers array.

## Model Request State Explicitly

A discriminated union prevents contradictory state such as “not loading, no error, no results” when the application has not searched yet.

```typescript
export type VolunteerSearchState =
  | { status: 'idle'; volunteers: readonly Volunteer[] }
  | { status: 'loading'; volunteers: readonly Volunteer[] }
  | { status: 'loaded'; volunteers: readonly Volunteer[] }
  | { status: 'failed'; volunteers: readonly Volunteer[]; message: string };
```

## Build the Latest-Query-Wins Pipeline

```typescript
// src/app/features/volunteers/volunteers.ts
import { AsyncPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  catchError,
  distinctUntilChanged,
  map,
  merge,
  Observable,
  of,
  shareReplay,
  startWith,
  Subject,
  switchMap,
  timer,
} from 'rxjs';
import { Volunteer } from './volunteer';
import { VolunteerRepository } from './volunteer-repository';

export type VolunteerSearchState =
  | { status: 'idle'; volunteers: readonly Volunteer[] }
  | { status: 'loading'; volunteers: readonly Volunteer[] }
  | { status: 'loaded'; volunteers: readonly Volunteer[] }
  | { status: 'failed'; volunteers: readonly Volunteer[]; message: string };

@Component({
  selector: 'app-volunteers',
  imports: [AsyncPipe, ReactiveFormsModule],
  templateUrl: './volunteers.html',
  styleUrl: './volunteers.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Volunteers {
  private readonly repository = inject(VolunteerRepository);
  private readonly retryRequested = new Subject<void>();

  protected readonly query = new FormControl('', { nonNullable: true });

  private readonly queryChanges = this.query.valueChanges.pipe(
    startWith(this.query.value),
    map(normalizeQuery),
    distinctUntilChanged(),
  );

  protected readonly results$: Observable<VolunteerSearchState> = merge(
    this.queryChanges,
    this.retryRequested.pipe(map(() => normalizeQuery(this.query.value))),
  ).pipe(
    switchMap((query) =>
      query.length < 2
        ? of<VolunteerSearchState>({ status: 'idle', volunteers: [] })
        : timer(250).pipe(
            switchMap(() =>
              this.repository.search(query).pipe(
                map(
                  (volunteers): VolunteerSearchState => ({ status: 'loaded', volunteers }),
                ),
                startWith<VolunteerSearchState>({ status: 'loading', volunteers: [] }),
                catchError(() =>
                  of<VolunteerSearchState>({
                    status: 'failed',
                    volunteers: [],
                    message: 'Volunteer search is unavailable. Try again.',
                  }),
                ),
              ),
            ),
          ),
    ),
    shareReplay({ bufferSize: 1, refCount: true }),
  );

  protected retrySearch(): void {
    this.retryRequested.next();
  }
}

function normalizeQuery(value: string): string {
  return value.trim().replace(/\s+/g, ' ');
}
```

The outer `switchMap` cancels a pending timer immediately when the query changes. Once the timer has emitted, the same operator cancels the active HTTP subscription. Angular then aborts that obsolete request. A plain `debounceTime` before `switchMap` would leave the older HTTP request active until the new debounce period ended.

Keep `catchError` inside the request observable. Putting it after the outer `switchMap` would let one failure terminate the entire query stream. The retry subject bypasses `distinctUntilChanged`, so the same query can be tried again.

## Render Idle, Loading, Results, and Failure

The template uses a single `AsyncPipe` subscription. The complete file renders the search field, an idle prompt, an announced loading message, an empty result, volunteer cards, and a retryable alert.

```html
<section class="search-panel" aria-labelledby="volunteer-search-heading">
  <h2 id="volunteer-search-heading">Volunteer directory</h2>
  <label for="volunteer-query">Name</label>
  <input
    id="volunteer-query"
    type="search"
    autocomplete="off"
    [formControl]="query"
    aria-describedby="volunteer-search-hint"
    placeholder="Try Anna or Morgan"
  />
  <p id="volunteer-search-hint" class="hint">Enter at least two characters.</p>
</section>

@if (results$ | async; as state) {
  @switch (state.status) {
    @case ('idle') {
      <p class="request-state">Start typing to search the directory.</p>
    }
    @case ('loading') {
      <p class="request-state" role="status" aria-live="polite">Searching volunteers…</p>
    }
    @case ('failed') {
      <div class="request-state error-state" role="alert">
        <p>{{ state.message }}</p>
        <button type="button" (click)="retrySearch()">Try again</button>
      </div>
    }
    @case ('loaded') {
      @if (state.volunteers.length === 0) {
        <p class="request-state">No volunteers match that name.</p>
      } @else {
        <ul class="volunteer-list">
          @for (volunteer of state.volunteers; track volunteer.id) {
            <li>
              <h3>{{ volunteer.displayName }}</h3>
              <ul class="ministry-list" aria-label="Eligible ministries">
                @for (ministry of volunteer.eligibleMinistries; track ministry) {
                  <li>{{ ministry }}</li>
                }
              </ul>
            </li>
          }
        </ul>
      }
    }
  }
}
```

`volunteers.scss` supplies the responsive page, search panel, visible keyboard focus, high-contrast status panels, and volunteer cards. Keeping those styles feature-local avoids growing the global stylesheet.

## Test Time and Cancellation

Use `HttpTestingController` to verify the GET request, encoded `query` parameter, and runtime parsing. Use Vitest fake timers for the component so a 250 ms debounce does not add real waiting to the suite.

```typescript
it('cancels stale results when a newer query arrives', async () => {
  const first = new Subject<readonly Volunteer[]>();
  const second = new Subject<readonly Volunteer[]>();
  repository.search.mockReturnValueOnce(first).mockReturnValueOnce(second);

  enterQuery('ann');
  await vi.advanceTimersByTimeAsync(250);
  enterQuery('anna');
  expect(first.observed).toBe(false);
  await vi.advanceTimersByTimeAsync(250);

  first.next([{ id: 'ann', displayName: 'Ann', eligibleMinistries: [] }]);
  second.next([{ id: 'anna', displayName: 'Anna', eligibleMinistries: ['welcome'] }]);
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('Anna');
  expect(fixture.nativeElement.textContent).not.toContain('AnnEligible');
});
```

The remaining tests cover whitespace normalization, the two-character minimum, the precise debounce boundary, invalid DTOs, error rendering, and retrying the unchanged query.

## Run Part 10-2

Start the API and Angular app in separate terminals:

```bash
dotnet run --project server/ChurchServe.Api
npm start
```

Open `http://localhost:4201/volunteers` and search for `ann`, `morgan`, or `sam`.

```bash
dotnet test ChurchServe.slnx --no-restore
npx prettier --check "src/app/features/volunteers/**/*.{ts,html,scss}"
npm test -- --watch=false
npm run build
```

## Part 10-2 Checkpoint

ChurchServe now has a search workflow with a clear concurrency rule: only the latest normalized query is relevant. The component does not know about URLs or JSON, failures do not kill future searches, and tests control time instead of depending on it.

Part 11 carries calendar filters into the URL and adds pagination so schedule views survive refreshes and can be shared safely.
