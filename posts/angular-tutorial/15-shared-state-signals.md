---
title: "ChurchServe Angular Tutorial Part 15: Shared State with Signals"
excerpt: "Build a route-scoped Angular signal store that coordinates schedule loading, retry, event caching, creation, and derived UI state."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular signals state management tutorial"
  description: "Build route-scoped Angular state with private writable signals, computed values, request coordination, event caching, and focused tests."
  socialTitle: "ChurchServe Part 15: Shared State with Signals"
  socialDescription: "Coordinate schedule pages with a route-scoped signal store while keeping server data, derived state, and request policy explicit."
---

# ChurchServe Angular Tutorial Part 15: Shared State with Signals

[Part 14](https://dev.jun-kim.net/2026/10/17/churchserve-angular-tutorial-part-14-route-guards-resolvers-and-unsaved-changes/) made schedule navigation predictable with guards, a detail resolver, and unsaved-change protection. The schedule list, detail resolver, and creation page still talk to `ServiceEventRepository` independently, though. Each consumer owns a different fragment of the same feature state.

Part 15 gives that state a clear home. A route-scoped `ScheduleStore` now owns list request state, the current page, an event cache, retry policy, and derived open-position totals. The list component renders store signals, the resolver reuses cached events, and a newly created event is cached before navigation reaches its detail page.

> **Quick answer:** Keep writable signals private, expose readonly signals and computed values, and put async coordination behind store methods. Provide the store on a componentless parent route so its instance is shared by the schedule children but destroyed when navigation leaves the feature. Signals do not replace the API or repository; they make client-side ownership and derivation explicit.

## What Changes in Part 15

Part 15 changes state ownership rather than the server contract:

1. The schedule route provides one `ScheduleStore` to the list, create page, and detail resolver.
2. The store prevents duplicate loads for an equivalent query and cancels an obsolete request when filters or pages change.
3. Loaded and newly created events enter a small in-memory cache, so detail navigation can reuse data during the current schedule-route lifetime.
4. A computed signal derives the number of unfilled positions on the current page.
5. The schedule UI shows matching-service and open-position summaries.
6. Retry, loading, failure, and loaded states move out of the component and into the store.

There are no ASP.NET Core, API contract, or SQLite changes in this part. The API remains the durable source of truth. The store is a short-lived browser cache and coordination layer.

## Updated File Structure

The new store sits beside the schedule models and repository boundary because all three belong to the same feature:

```text
src/app/
├── features/
│   └── schedule/
│       ├── schedule-store.spec.ts       # New store behavior tests
│       ├── schedule-store.ts            # New route-scoped signal store
│       ├── schedule.html                # Reads store state and summary values
│       ├── schedule.scss                # Styles the summary pills
│       ├── schedule.spec.ts             # Verifies store-driven UI states
│       ├── schedule.ts                  # Connects URL query state to the store
│       ├── service-event-create/
│       │   └── service-event-create.ts  # Creates through the store
│       └── service-event-detail/
│           └── service-event.resolver.ts # Resolves through the store cache
└── app.routes.ts                        # Defines the feature provider scope
```

The existing `assignment-store.ts` remains separate. It is the local assignment model introduced earlier, while schedule events now come from the API and contain only assignment counts. Combining them would invent an API relationship that does not exist yet. A later API-backed assignment feature can either coordinate with this store or introduce its own feature store based on the real server contract.

## Model the Store's Public Contract

Create `src/app/features/schedule/schedule-store.ts`:

```typescript
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, of, tap } from 'rxjs';
import { CreateServiceEventCommand } from './create-service-event-command';
import { Page } from './page';
import { equalScheduleQuery, ScheduleQuery } from './schedule-query';
import { ServiceEventRepository } from './service-event-repository';
import { ServiceEvent } from './service-event';

export type ScheduleLoadState = 'idle' | 'loading' | 'loaded' | 'failed';

@Injectable()
export class ScheduleStore {
  private readonly repository = inject(ServiceEventRepository);
  private readonly destroyRef = inject(DestroyRef);
  private readonly loadStateValue = signal<ScheduleLoadState>('idle');
  private readonly queryValue = signal<ScheduleQuery | null>(null);
  private readonly pageValue = signal<Page<ServiceEvent> | null>(null);
  private readonly loadErrorValue = signal('');
  private readonly eventCacheValue = signal<ReadonlyMap<string, ServiceEvent>>(new Map());
  private loadSubscription?: Subscription;

  readonly loadState = this.loadStateValue.asReadonly();
  readonly query = this.queryValue.asReadonly();
  readonly page = this.pageValue.asReadonly();
  readonly loadError = this.loadErrorValue.asReadonly();
  readonly events = computed(() => this.pageValue()?.items ?? []);
  readonly totalOpenPositions = computed(() =>
    this.events().reduce(
      (total, event) => total + Math.max(0, event.requiredPositions - event.assignedPositions),
      0,
    ),
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.loadSubscription?.unsubscribe());
  }

  load(query: ScheduleQuery, force = false): void {
    const currentQuery = this.queryValue();
    const isSameQuery = currentQuery !== null && equalScheduleQuery(currentQuery, query);

    if (
      !force &&
      isSameQuery &&
      (this.loadStateValue() === 'loading' || this.loadStateValue() === 'loaded')
    ) {
      return;
    }

    this.loadSubscription?.unsubscribe();
    this.queryValue.set(query);
    this.loadStateValue.set('loading');
    this.loadErrorValue.set('');

    this.loadSubscription = this.repository.list(query).subscribe({
      next: (page) => {
        this.pageValue.set(page);
        this.cacheEvents(page.items);
        this.loadStateValue.set('loaded');
      },
      error: () => {
        this.loadStateValue.set('failed');
        this.loadErrorValue.set(
          'The schedule could not be loaded. Check your connection and try again.',
        );
      },
    });
  }

  retry(): void {
    const query = this.queryValue();
    if (query) {
      this.load(query, true);
    }
  }

  findById(id: string): Observable<ServiceEvent | undefined> {
    const cachedEvent = this.eventCacheValue().get(id);
    if (cachedEvent) {
      return of(cachedEvent);
    }

    return this.repository.findById(id).pipe(
      tap((event) => {
        if (event) {
          this.cacheEvents([event]);
        }
      }),
    );
  }

  create(command: CreateServiceEventCommand): Observable<ServiceEvent> {
    return this.repository.create(command).pipe(tap((event) => this.cacheEvents([event])));
  }

  private cacheEvents(events: readonly ServiceEvent[]): void {
    if (events.length === 0) {
      return;
    }

    const nextCache = new Map(this.eventCacheValue());
    for (const event of events) {
      nextCache.set(event.id, event);
    }
    this.eventCacheValue.set(nextCache);
  }
}
```

The class intentionally has `@Injectable()` without `providedIn: 'root'`. Its provider location will define its lifetime.

The writable signals use private names such as `pageValue`. Consumers receive readonly views through `asReadonly()`. That prevents a component from calling `set()` or `update()` and bypassing the store's request policy. It does not make nested JavaScript objects deeply immutable, so the models also use readonly properties and the cache is replaced instead of mutated in place.

`events` and `totalOpenPositions` are derived state. Storing either as a second writable signal would create another source of truth that every successful load would have to synchronize. Angular's [`computed`](https://angular.dev/guide/signals#computed-signals) values are lazy and memoized, and Angular invalidates them when their signal dependencies change.

`Math.max(0, required - assigned)` protects the UI summary from becoming negative if temporarily inconsistent data reports more assigned positions than required positions. It is a display invariant, not a substitute for server-side validation.

## Coordinate Loads, Retries, and Stale Requests

`load()` compares the requested filters and pagination with the current query. If an equivalent request is already loading or loaded, it returns without another HTTP call. A retry passes `force = true`, so a failed request can run again even though its query is unchanged.

When the query changes, the store unsubscribes from the previous repository observable before starting the next one. Angular's `HttpClient` observes that cancellation and aborts the obsolete request. More importantly, an old response can no longer replace the state for a newer filter.

The store owns that list subscription, so it also owns cleanup. `DestroyRef.onDestroy()` unsubscribes when Angular destroys the route-scoped injector. Methods such as `findById()` and `create()` still return observables because the resolver and create component already own those operation-specific subscriptions and outcomes.

This is one deliberate policy, not a universal store rule. A larger store may use RxJS pipelines, a command queue, or a dedicated data-query library. The important part is that duplicate, cancellation, retry, and lifetime behavior are explicit and tested.

## Scope One Store to the Schedule Route

Replace the three top-level schedule route entries in `src/app/app.routes.ts` with a componentless parent route:

```typescript
import { Routes } from '@angular/router';
import { signedInGuard } from './core/auth/signed-in.guard';
import { unsavedChangesGuard } from './core/routing/unsaved-changes.guard';
import { serviceEventResolver } from './features/schedule/service-event-detail/service-event.resolver';
import { ScheduleStore } from './features/schedule/schedule-store';

export const routes: Routes = [
  // Home and sign-in routes remain unchanged.
  {
    path: 'schedule',
    canActivate: [signedInGuard],
    providers: [ScheduleStore],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/schedule/schedule').then((module) => module.Schedule),
        title: 'Schedule | ChurchServe',
      },
      {
        path: 'new',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/schedule/service-event-create/service-event-create').then(
            (module) => module.ServiceEventCreate,
          ),
        title: 'Create service | ChurchServe',
      },
      {
        path: ':eventId',
        resolve: { serviceEvent: serviceEventResolver },
        loadComponent: () =>
          import('./features/schedule/service-event-detail/service-event-detail').then(
            (module) => module.ServiceEventDetail,
          ),
        title: 'Service details | ChurchServe',
      },
    ],
  },
  // Volunteer, unavailable, not-found, and wildcard routes remain unchanged.
];
```

Angular creates an environment injector for a route with `providers`. Its children resolve the same `ScheduleStore` instance while that route is active. Moving between `/schedule`, `/schedule/new`, and `/schedule/:eventId` therefore preserves the cache. Navigating to `/volunteers` leaves the parent route and destroys the store.

This scope is narrower and safer than a root singleton. Schedule data does not survive indefinitely or cross a later sign-out/sign-in boundary. If product requirements eventually need a cross-feature cache, moving the provider to root must be accompanied by an explicit invalidation policy for logout, tenant changes, permissions, and server freshness.

## Connect URL State to the Store

Replace `src/app/features/schedule/schedule.ts` with:

```typescript
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Panel } from '../../shared/ui/panel/panel';
import { equalScheduleQuery, parseScheduleQuery, scheduleMinistries } from './schedule-query';
import { ServiceEventCard } from './service-event-card/service-event-card';
import { ScheduleStore } from './schedule-store';

@Component({
  selector: 'app-schedule',
  imports: [RouterLink, ServiceEventCard, EmptyState, Panel],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Schedule {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly store = inject(ScheduleStore);
  protected readonly ministries = scheduleMinistries;
  protected readonly query = computed(
    () => this.store.query() ?? parseScheduleQuery(this.route.snapshot.queryParamMap),
  );
  protected readonly totalPages = computed(() => {
    const page = this.store.page();
    return page ? Math.max(1, Math.ceil(page.totalItems / page.pageSize)) : 1;
  });
  protected readonly hasPreviousPage = computed(() => this.query().page > 1);
  protected readonly hasNextPage = computed(() => this.query().page < this.totalPages());

  constructor() {
    this.route.queryParamMap
      .pipe(
        map((params) => parseScheduleQuery(params)),
        distinctUntilChanged(equalScheduleQuery),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((query) => this.store.load(query));

    this.canonicalizeQueryString();
  }

  protected changeMonth(month: string): void {
    void this.navigate({ month, page: 1 });
  }

  protected changeMinistry(ministry: string): void {
    void this.navigate({ ministry: ministry || null, page: 1 });
  }

  protected showAllMinistries(): void {
    this.changeMinistry('');
  }

  protected changePage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      void this.navigate({ page });
    }
  }

  protected reload(): void {
    this.store.retry();
  }

  private navigate(queryParams: Record<string, string | number | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  private canonicalizeQueryString(): void {
    const query = this.query();
    const params = this.route.snapshot.queryParamMap;

    if (
      params.get('month') === query.month &&
      params.get('page') === String(query.page) &&
      params.get('ministry') === query.ministry
    ) {
      return;
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { month: query.month, ministry: query.ministry, page: query.page },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
```

The URL remains the source of truth for filters and pagination because it makes a schedule view refresh-safe, bookmarkable, and shareable. The component translates router state into the store's `load()` command. The store owns remote request state; the component still owns navigation behavior.

The fallback in `query` covers the brief initial period before the store receives its first query. Once `load()` runs, controls and pagination read the same normalized query the repository used.

## Render Store State and Derived Values

Update the state-rendering portion of `src/app/features/schedule/schedule.html`:

```html
@switch (store.loadState()) {
  @case ('loading') {
    <p class="state-panel" role="status">Loading service schedule…</p>
  }

  @case ('failed') {
    <section class="state-panel error-state" role="alert">
      <h2>Schedule unavailable</h2>
      <p>{{ store.loadError() }}</p>
      <button type="button" (click)="reload()">Try again</button>
    </section>
  }

  @case ('loaded') {
    <section class="schedule-summary" aria-label="Current schedule summary">
      <p><strong>{{ store.page()?.totalItems ?? 0 }}</strong> matching services</p>
      <p><strong>{{ store.totalOpenPositions() }}</strong> open positions on this page</p>
    </section>

    <div class="event-grid">
      @for (event of store.events(); track event.id) {
        <app-service-event-card [event]="event" />
      } @empty {
        <app-empty-state>
          <span empty-title>No matching services</span>
          <p>Try a different month or ministry.</p>
          @if (query().ministry) {
            <button empty-actions type="button" (click)="showAllMinistries()">
              Show all ministries
            </button>
          }
        </app-empty-state>
      }
    </div>

    @if (store.page(); as currentPage) {
      <nav class="pagination" aria-label="Schedule pages">
        <button
          type="button"
          [disabled]="!hasPreviousPage()"
          (click)="changePage(query().page - 1)"
        >
          Previous
        </button>
        <p aria-live="polite">
          Page {{ currentPage.page }} of {{ totalPages() }} · {{ currentPage.totalItems }} results
        </p>
        <button type="button" [disabled]="!hasNextPage()" (click)="changePage(query().page + 1)">
          Next
        </button>
      </nav>
    }
  }
}
```

The existing heading, create link, and filters stay above this block. Add the summary styles to `schedule.scss`:

```scss
.schedule-summary {
  display: flex;
  flex-wrap: wrap;
  margin-top: 2rem;
  gap: 0.75rem;
}

.schedule-summary p {
  margin: 0;
  padding: 0.7rem 0.9rem;
  border: 1px solid #b7d8cb;
  border-radius: 999px;
  background: #eefbf6;
  color: #285f4c;
}
```

With `OnPush` change detection, Angular tracks signals read by the template and marks the component when those values change. No manual `ChangeDetectorRef` call or signal-to-observable conversion is needed.

The open-position value describes the current page, not the full result set. The API returns totals for matching events but not a server-calculated open-position aggregate across every page. The label makes that limitation explicit instead of presenting a misleading global number.

## Reuse the Cache in Create and Detail Workflows

The creation page changes only its dependency and call target in `service-event-create.ts`:

```typescript
import { ScheduleStore } from '../schedule-store';

export class ServiceEventCreate implements DirtyAware {
  private readonly router = inject(Router);
  private readonly store = inject(ScheduleStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly eventForm = viewChild(ServiceEventForm);

  protected readonly saveError = signal('');

  protected createEvent(command: CreateServiceEventCommand): void {
    this.saveError.set('');
    this.store
      .create(command)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          this.eventForm()?.form.markAsPristine();
          void this.router.navigate(['/schedule', event.id]);
        },
        error: () =>
          this.saveError.set(
            'The event could not be created. Check your connection and try again.',
          ),
      });
  }
}
```

The store still delegates persistence to `ServiceEventRepository`. Its added responsibility is to cache the confirmed server response. It does not optimistically invent an ID or treat local state as durable.

Replace `service-event.resolver.ts` with:

```typescript
import { inject } from '@angular/core';
import { RedirectCommand, ResolveFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { ScheduleStore } from '../schedule-store';
import { ServiceEvent } from '../service-event';

export const serviceEventResolver: ResolveFn<ServiceEvent | RedirectCommand> = (route, state) => {
  const store = inject(ScheduleStore);
  const router = inject(Router);
  const id = route.paramMap.get('eventId');

  if (!id) {
    return new RedirectCommand(router.parseUrl('/not-found'));
  }

  return store.findById(id).pipe(
    map((event) => event ?? new RedirectCommand(router.parseUrl('/not-found'))),
    catchError(() =>
      of(
        new RedirectCommand(
          router.createUrlTree(['/unavailable'], {
            queryParams: { returnUrl: state.url },
          }),
        ),
      ),
    ),
  );
};
```

`findById()` returns an observable in both paths. A cache hit uses `of(cachedEvent)`, while a miss calls the repository and caches a successful result. The resolver therefore keeps one contract and its missing/unavailable redirect behavior from Part 14.

This cache is deliberately simple. It has no time-to-live and does not promise freshness after another user changes an event. Route destruction bounds its lifetime, and future mutation features should replace cached records with confirmed responses or explicitly invalidate them.

## Test Behavior Through the Public Store API

The store tests provide a fake repository and assert only public signals and methods. These are the most important cases from `schedule-store.spec.ts`:

```typescript
it('loads a page and derives its open-position count', () => {
  store.load(query);
  expect(store.loadState()).toBe('loaded');
  expect(store.page()).toEqual(page([event]));
  expect(store.events()).toEqual([event]);
  expect(store.totalOpenPositions()).toBe(3);
});

it('does not start a duplicate request for the same pending or loaded query', () => {
  const response = new Subject<Page<ServiceEvent>>();
  repository.list.mockReturnValue(response);
  store.load(query);
  store.load({ ...query });
  response.next(page([event]));
  store.load({ ...query });
  expect(repository.list).toHaveBeenCalledTimes(1);
});

it('cancels the previous request when the query changes', () => {
  const firstResponse = new Subject<Page<ServiceEvent>>();
  const secondResponse = new Subject<Page<ServiceEvent>>();
  repository.list.mockReturnValueOnce(firstResponse).mockReturnValueOnce(secondResponse);
  store.load(query);
  store.load({ ...query, page: 2 });
  firstResponse.next(page([{ ...event, id: 'stale' }]));
  secondResponse.next(page([{ ...event, id: 'current' }]));
  expect(store.events()[0].id).toBe('current');
});

it('records a load failure and supports an explicit retry', () => {
  repository.list
    .mockReturnValueOnce(throwError(() => new Error('API unavailable')))
    .mockReturnValueOnce(of(page([event])));
  store.load(query);
  expect(store.loadState()).toBe('failed');
  store.retry();
  expect(store.loadState()).toBe('loaded');
  expect(repository.list).toHaveBeenCalledTimes(2);
});

it('caches a newly created event for the detail resolver', () => {
  store.create(command).subscribe();
  store.findById(event.id).subscribe();
  expect(repository.create).toHaveBeenCalledWith(command);
  expect(repository.findById).not.toHaveBeenCalled();
});
```

The component tests now provide the real `ScheduleStore` with a fake repository. `NEVER` holds the request in loading state, `throwError()` exercises failure and retry, and a `Subject<Page<ServiceEvent>>` lets the test publish reordered results. This tests the UI and store integration without exposing test-only state mutation methods in production code.

Router tests remain valuable because they prove the route provider is visible to the lazy list, create page, and resolver. A store test alone cannot catch a missing `providers: [ScheduleStore]` entry.

## Run and Verify Part 15

Start the API and Angular application as before:

```bash
dotnet run --project server/ChurchServe.Api
npm start
```

Sign in and open `http://localhost:4201/schedule`. The schedule now displays two summary pills above the cards. Change the month or ministry and watch the summary follow the loaded page. Open a listed event and return to the schedule; the route-scoped store remains available while navigation stays inside the feature. Create an event and its detail page can use the confirmed event cached by the create command.

Run the automated checks:

```bash
npm test -- --watch=false
npm run build
dotnet test ChurchServe.slnx --configuration Release
```

For Part 15, the Angular suite contains 84 passing tests across 21 files, the production build passes strict Angular and TypeScript compilation, and all 20 API integration tests still pass. Release mode for the API tests avoids colliding with a locally running Debug API executable on Windows.

## Shared State Without Global State

ChurchServe now shares schedule state across related pages without turning it into application-global state. The repository remains responsible for HTTP and persistence boundaries. The route-scoped store owns client request policy and a bounded cache. Components render readonly signals and send commands. Computed signals describe values that can be derived rather than synchronized manually.

That division is small enough for this application but scales better than letting every component fetch and cache independently. Part 16 will build on these seams to organize component, service, HTTP, and router tests into a practical testing strategy.
