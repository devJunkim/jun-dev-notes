---
title: "ChurchServe Angular Tutorial Part 19-2: Coordinate Schedule Caches"
excerpt: "Apply bounded request caching to schedule pages, event details, and assignment boards with normalized keys, mutation invalidation, and identity isolation."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular cache invalidation schedule data"
  description: "Apply bounded Angular caching to schedule data with normalized keys, resource TTLs, mutation invalidation, identity clearing, and tests."
  socialTitle: "ChurchServe Part 19-2: Schedule Cache Coordination"
  socialDescription: "Reuse fresh schedule data while keeping assignments, mutations, and authenticated identities consistent."
---

# ChurchServe Angular Tutorial Part 19-2: Coordinate Schedule Caches

Part 19-1 built a reusable `RequestCache` that shares in-flight observables, retains fresh values, bounds memory, and prevents late responses from repopulating a cleared cache. This part applies it to ChurchServe’s real reads and writes.

> **Quick answer:** Use separate policies for schedule pages, event details, and assignment boards. Normalize every key, update or invalidate related resources after mutations, and clear all identity-bound state whenever the authenticated user or role set changes.

## What Changes in Part 19-2

```text
src/app/features/schedule/
├── assignment-store.spec.ts
├── assignment-store.ts
├── schedule-query.spec.ts
├── schedule-query.ts
├── schedule-store.spec.ts
└── schedule-store.ts
```

There is no intentional visual change. The browser network panel shows the difference:

- schedule pages are fresh for 30 seconds, with at most six retained pages;
- event details are fresh for 60 seconds, with at most twenty retained IDs;
- assignment boards are fresh for 15 seconds, with at most ten retained events;
- concurrent callers share an active request;
- mutations synchronize or invalidate related data;
- user and role changes clear the feature state.

These values are explicit tutorial policies, not universal defaults. Production values should reflect measured navigation patterns and acceptable staleness.

## Normalize Schedule Page Keys

A key must include every input that changes the server response. Add this function to `schedule-query.ts`:

```typescript
export function scheduleQueryCacheKey(query: ScheduleQuery): string {
  const ministry = query.ministry?.trim().toLowerCase() ?? '';
  return `${query.month}|${ministry}|${query.page}|${query.pageSize}`;
}
```

Normalizing ministry casing prevents equivalent filters from creating separate entries. Page and page size remain in the key because they select different result slices.

```typescript
describe('scheduleQueryCacheKey', () => {
  it('normalizes ministry casing and whitespace', () => {
    expect(
      scheduleQueryCacheKey({
        month: '2026-11',
        ministry: ' Technical ',
        page: 2,
        pageSize: 4,
      }),
    ).toBe('2026-11|technical|2|4');
  });

  it('keeps paging parameters in the key', () => {
    expect(
      scheduleQueryCacheKey({ month: '2026-11', ministry: null, page: 1, pageSize: 4 }),
    ).not.toBe(
      scheduleQueryCacheKey({ month: '2026-11', ministry: null, page: 2, pageSize: 4 }),
    );
  });
});
```

## Give Pages and Details Different Policies

Inject the clock and create two caches in `ScheduleStore`:

```typescript
private readonly clock = inject(CACHE_CLOCK);

private readonly pageCache = new RequestCache<string, Page<ServiceEvent>>({
  maxAgeMs: 30_000,
  maxEntries: 6,
  now: this.clock,
});

private readonly eventCache = new RequestCache<string, ServiceEvent | undefined>({
  maxAgeMs: 60_000,
  maxEntries: 20,
  now: this.clock,
});
```

Event details receive a longer lifetime because a single event changes less often than active assignment data. Caching `undefined` is intentional: a recent `404` does not need to be requested repeatedly. The `CacheLookup` result from Part 19-1 distinguishes that cached value from a miss.

## Coordinate Schedule Page Loads

Update `load`:

```typescript
load(query: ScheduleQuery, force = false): void {
  const currentQuery = this.queryValue();
  const isSameQuery = currentQuery !== null && equalScheduleQuery(currentQuery, query);

  if (!force && isSameQuery && this.loadStateValue() === 'loading') {
    return;
  }

  this.loadSubscription?.unsubscribe();
  this.queryValue.set(query);
  this.loadStateValue.set('loading');
  this.loadErrorValue.set('');

  const cacheKey = scheduleQueryCacheKey(query);
  this.loadSubscription = this.pageCache
    .getOrLoad(cacheKey, () => this.repository.list(query), force)
    .subscribe({
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
```

The early return applies only while the same query is loading. After completion, another call goes through `RequestCache`: a fresh value resolves immediately and an expired value starts a new request. `retry()` continues to use `force = true`.

The detail resolver now uses the ID cache:

```typescript
findById(id: string): Observable<ServiceEvent | undefined> {
  return this.eventCache.getOrLoad(id, () => this.repository.findById(id));
}
```

Events returned by a schedule page are also inserted into `eventCache`, so opening one of those details does not immediately repeat the read.

## Invalidate Pages After Event Creation

Creating an event may change the membership, count, or pagination of several list queries. Keep the new event for its detail route, but clear page results:

```typescript
create(command: CreateServiceEventCommand): Observable<ServiceEvent> {
  const requestGeneration = this.identityGeneration;
  return this.repository.create(command).pipe(
    tap((event) => {
      if (requestGeneration !== this.identityGeneration) {
        return;
      }

      this.pageCache.clear();
      this.cacheEvents([event]);
    }),
  );
}
```

The generation check prevents an event-creation response started under an older identity from entering the next identity’s cache.

## Synchronize Assignment Summaries

An assignment changes the parent event’s `assignedPositions` and `version`. Add this method to `ScheduleStore`:

```typescript
synchronizeAssignmentSummary(eventId: string, assignedPositions: number, version: number): void {
  const cached = this.eventCache.read(eventId);
  if (cached.hit && cached.value) {
    this.eventCache.set(eventId, { ...cached.value, assignedPositions, version });
  }

  const page = this.pageValue();
  if (page?.items.some((event) => event.id === eventId)) {
    const nextPage = {
      ...page,
      items: page.items.map((event) =>
        event.id === eventId ? { ...event, assignedPositions, version } : event,
      ),
    };
    this.pageValue.set(nextPage);
    this.cacheEvents(nextPage.items);
  }

  this.pageCache.clear();
}
```

The visible page and detail entry receive confirmed values immediately. Other retained pages are discarded because any of them may contain the changed event.

## Cache Assignment Boards Conservatively

`AssignmentStore` uses a shorter lifetime:

```typescript
private readonly boardCache = new RequestCache<string, AssignmentBoard>({
  maxAgeMs: 15_000,
  maxEntries: 10,
  now: this.clock,
});
```

Coordinate duplicate board loads:

```typescript
load(eventId: string, force = false): void {
  if (!force && this.loadingState() && this.requestedEventId === eventId) {
    return;
  }

  this.loadSubscription?.unsubscribe();
  this.requestedEventId = eventId;
  this.loadingState.set(true);
  this.loadErrorState.set(null);
  this.noticeState.set(null);

  this.loadSubscription = this.boardCache
    .getOrLoad(eventId, () => this.repository.loadBoard(eventId), force)
    .subscribe({
      next: (board) => {
        this.applyBoard(board);
        this.loadingState.set(false);
      },
      error: () => {
        this.loadingState.set(false);
        this.loadErrorState.set('Assignments could not be loaded. Try again.');
      },
    });
}
```

Calling `load` twice while the same event is pending starts one repository request. Returning within fifteen seconds applies the retained board synchronously.

## Coordinate Assignment Mutations

Invalidate the board as soon as an optimistic assignment begins:

```typescript
this.noticeState.set(null);
this.assignmentsState.update((current) => [...current, pending]);
this.boardCache.invalidate(eventId);
```

After confirmation, replace only the matching optimistic item, update the authoritative summary, and cache the board only when no other optimistic operation remains:

```typescript
this.assignmentsState.update((current) =>
  current.map((assignment) =>
    assignment.clientRequestId === clientRequestId ? result.assignment : assignment,
  ),
);
this.eventVersionState.set(result.eventVersion);
this.assignedPositionsState.set(result.assignedPositions);
this.cacheCurrentBoard();
this.scheduleStore?.synchronizeAssignmentSummary(
  eventId,
  result.assignedPositions,
  result.eventVersion,
);
```

`cacheCurrentBoard` avoids retaining speculative data:

```typescript
private cacheCurrentBoard(): void {
  const eventId = this.eventIdState();
  const eventVersion = this.eventVersionState();
  const assignedPositions = this.assignedPositionsState();
  if (!eventId || eventVersion === null || assignedPositions === null) {
    return;
  }

  if (this.assignmentsState().some((item) => item.syncStatus === 'pending')) {
    return;
  }

  this.boardCache.set(eventId, {
    eventId,
    eventVersion,
    assignedPositions,
    positions: this.positionsState(),
    volunteers: this.volunteersState(),
    assignments: this.assignmentsState().filter((item) => item.syncStatus === 'confirmed'),
  });
}
```

A `409 Conflict` already contains the server’s board, so apply and cache it before synchronizing the event summary. An ordinary failure rolls back its optimistic item and leaves the board invalidated; the next load must ask the server.

## Clear Data When Identity or Roles Change

Cache isolation must consider permissions as well as user ID. Both stores derive a key from the authenticated user and sorted roles:

```typescript
function sessionCacheIdentity(session: ReturnType<SessionStore['session']>): string {
  if (session.status !== 'authenticated') {
    return session.status;
  }

  return `${session.user.id}|${[...session.user.roles].sort().join(',')}`;
}
```

Initialize the marker synchronously before registering the effect:

```typescript
constructor() {
  this.destroyRef.onDestroy(() => this.loadSubscription?.unsubscribe());
  this.cacheIdentity = this.currentCacheIdentity();
  effect(() => {
    const identity = this.currentCacheIdentity();
    if (identity !== this.cacheIdentity) {
      this.cacheIdentity = identity;
      this.clear();
    }
  });
}
```

Angular effects run during change detection. Synchronous initialization prevents the first effect execution from looking like a change and clearing data that a component just loaded.

`clear()` cancels the active load, resets signals, clears retained values, and increments `identityGeneration`. Assignment callbacks compare their captured generation before changing state, so a late response from a previous user is ignored.

## Test the Resource Policies

Provide a controllable clock in store tests:

```typescript
{ provide: CACHE_CLOCK, useValue: () => now }
```

Then test fresh navigation and expiry without timers:

```typescript
it('reuses a recently loaded page when navigating between query keys', () => {
  store.load(query);
  store.load({ ...query, page: 2 });
  store.load({ ...query });

  expect(repository.list).toHaveBeenCalledTimes(2);
});

it('refreshes a page after its freshness window expires', () => {
  store.load(query);
  now += 30_001;
  store.load({ ...query });

  expect(repository.list).toHaveBeenCalledTimes(2);
});
```

Assignment-board tests cover pending coordination and retained reuse:

```typescript
it('shares a pending load and reuses a fresh assignment board', () => {
  store.load('event-1');
  store.load('event-1');
  expect(repository.loadResponses).toHaveLength(1);

  repository.loadResponses[0].next(board());
  repository.loadResponses[0].complete();
  store.load('event-1');

  expect(repository.loadResponses).toHaveLength(1);
  expect(store.eventVersion()).toBe(3);
});
```

The expanded suite also covers expired boards, failed-request recovery, event-creation invalidation, assignment-summary synchronization, failed-mutation invalidation, logout clearing, and role changes for the same user.

## Result

ChurchServe now reuses fresh schedule data without treating every `GET` response the same. Pages, details, and assignment boards have separate bounded policies. Mutations synchronize or invalidate related resources, and authentication changes clear identity-dependent state.

This part intentionally does not add `ETag` support or stale-while-revalidate UI. The application serves only fresh retained values and uses the existing loading state once an entry expires. Conditional requests can be introduced later when the server participates in freshness decisions.

Part 20 will move from network work to rendering work and keep larger schedule views responsive.
