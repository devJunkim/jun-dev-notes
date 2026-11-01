---
title: "ChurchServe React Tutorial Part 19-2: Coordinate Schedule Caches"
excerpt: "Apply bounded request caching to React schedule lists, event details, and assignment boards without retaining speculative or identity-bound data."
category: "React Tutorial"

seo:
  focusKeyword: "React cache invalidation schedule data"
  description: "Coordinate React schedule caches with resource-specific TTLs, mutation invalidation, optimistic updates, route-scoped clearing, and tests."
  socialTitle: "ChurchServe React Part 19-2: Schedule Cache Coordination"
  socialDescription: "Reuse fresh schedule data while keeping event creation, optimistic assignments, and authenticated feature state consistent."
---

# ChurchServe React Tutorial Part 19-2: Coordinate Schedule Caches

Part 19-1 built a generic `RequestCache`. A cache primitive is only the mechanism; correctness depends on the policy each feature store applies to reads, mutations, and authenticated lifecycle boundaries.

> **Quick answer:** Give schedule lists, event details, and assignment boards separate freshness and capacity policies. Populate detail entries from list responses, invalidate broad results after creation, synchronize confirmed assignment summaries, never retain optimistic state, and clear every cache when the protected feature scope unmounts.

## Choose policies per resource

The schedule store creates two caches with the same injected clock:

```typescript
constructor(
  repository: ServiceEventRepository,
  now: () => number = Date.now,
) {
  this.repository = repository
  this.listCache = new RequestCache({
    maxAgeMs: 30_000,
    maxEntries: 1,
    now,
  })
  this.eventCache = new RequestCache({
    maxAgeMs: 60_000,
    maxEntries: 20,
    now,
  })
}
```

The current application has one unfiltered schedule list, so retaining more than one list would add capacity without adding value. Event details use IDs as keys and receive a longer lifetime. The value type is `ServiceEvent | undefined`, which also permits a recent not-found result to be reused.

The assignment store uses a shorter policy:

```typescript
this.boardCache = new RequestCache({
  maxAgeMs: 15_000,
  maxEntries: 10,
  now,
})
```

Assignment boards change more frequently than event descriptions. These lifetimes are explicit tutorial choices, not universal recommendations. Production values should reflect acceptable staleness and observed navigation behavior.

## Coordinate list loads

`ScheduleStore.load` first checks the retained list. A miss delegates request sharing and forced-refresh cancellation to `RequestCache`:

```typescript
load(force = false): Promise<void> {
  if (!force) {
    const cached = this.listCache.read('events')
    if (cached.hit) {
      this.applyEvents(cached.value)
      return Promise.resolve()
    }
  }

  if (!force && this.activeListRequest) {
    return this.activeListRequest
  }

  this.setSnapshot({ status: 'loading' })
  const request = this.listCache
    .getOrLoad('events', (signal) => this.repository.list(signal), force)
    .then((events) => {
      if (this.activeListRequest !== request) return
      this.applyEvents(events)
    })
    .catch((error: unknown) => {
      if (this.activeListRequest !== request || isAbortError(error)) return
      this.setSnapshot({
        status: 'error',
        message: 'The scheduling service did not respond. Try again.',
      })
    })

  this.activeListRequest = request
  return request
}
```

The store still owns UI state: loading, ready, and recoverable error snapshots. The cache owns reuse policy. Keeping those responsibilities separate prevents transport details from leaking into React components.

Every successful list also warms the event-detail cache:

```typescript
private applyEvents(events: readonly ServiceEvent[]) {
  for (const event of events) this.eventCache.set(event.id, event)
  this.setSnapshot({
    status: 'ready',
    events,
    totalOpenPositions: countOpenPositions(events),
  })
}
```

Opening a detail page immediately after viewing the schedule therefore avoids a redundant request.

## Cache details and preserve cancellation

The detail loader accepts the router's signal and combines it with the cache-owned signal:

```typescript
findById(eventId: string, signal?: AbortSignal) {
  signal?.throwIfAborted()
  const cached = this.eventCache.read(eventId)
  if (cached.hit) return Promise.resolve(cached.value)

  return this.eventCache.getOrLoad(eventId, (cacheSignal) => {
    const combinedSignal = signal
      ? AbortSignal.any([signal, cacheSignal])
      : cacheSignal
    return this.repository.findById(eventId, combinedSignal)
  })
}
```

Router cancellation stops obsolete navigation work. Cache cancellation stops work invalidated by the feature lifecycle. The repository receives either reason through one signal.

## Invalidate broadly after event creation

A newly created event can change list membership and aggregate counts. Do not append it blindly to every possible list representation:

```typescript
async create(command: CreateServiceEventCommand, signal?: AbortSignal) {
  const event = await this.repository.create(command, signal)
  signal?.throwIfAborted()
  this.listCache.clear()
  this.eventCache.set(event.id, event)
  return event
}
```

The next list navigation asks the server for authoritative membership. The confirmed event remains available for the detail route used after creation.

## Synchronize confirmed assignment summaries

The assignment store reports confirmed aggregate changes to the schedule store. Update both visible and retained representations without accepting an older version:

```typescript
updateStaffing(
  eventId: string,
  assignedPositions: number,
  version: number,
) {
  const cached = this.eventCache.read(eventId)
  const current = cached.hit ? cached.value : undefined
  if (current && version >= current.version) {
    this.eventCache.set(eventId, {
      ...current,
      assignedPositions,
      version,
    })
  }

  if (this.snapshot.status === 'ready') {
    const events = this.snapshot.events.map((event) =>
      event.id === eventId && version >= event.version
        ? { ...event, assignedPositions, version }
        : event,
    )
    this.listCache.set('events', events)
    this.applyEvents(events)
  } else {
    this.listCache.clear()
  }
}
```

When the list is visible, the confirmed result can safely update that known representation. Without a visible list, clearing is safer than guessing what an older retained list contains.

## Cache assignment boards conservatively

`AssignmentStore.load` uses event ID as the key. It reuses a fresh board, shares an active load for the same event, and permits a forced reload:

```typescript
if (!force) {
  const cached = this.boardCache.read(eventId)
  if (cached.hit) {
    this.applyBoard(cached.value, undefined, false)
    return
  }
  if (this.activeLoad?.eventId === eventId) {
    return this.activeLoad.promise
  }
}
```

Passing `false` to `applyBoard` avoids storing the cached value again. A read should update LRU recency, but it should not reset the freshness timestamp and turn a fifteen-second policy into sliding expiration.

## Keep optimistic state out of retained data

[Part 18-3](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-18-3-optimistic-react-assignments/) added a pending assignment before the server responds. That prediction must not become a reusable board.

Invalidate the board when the mutation begins:

```typescript
this.boardCache.invalidate(current.eventId)
this.setSnapshot({
  ...current,
  assignments: [...current.assignments, pending],
  notice: undefined,
})
```

After success, replace the matching pending item with the confirmed assignment. Cache the current board only when no pending items remain:

```typescript
private cacheCurrentBoard() {
  if (
    this.snapshot.status !== 'ready' ||
    this.snapshot.assignments.some(
      (item) => item.syncStatus === 'pending',
    )
  ) {
    return
  }

  this.boardCache.set(this.snapshot.eventId, {
    eventId: this.snapshot.eventId,
    eventVersion: this.snapshot.eventVersion,
    assignedPositions: this.snapshot.assignedPositions,
    positions: this.snapshot.positions,
    volunteers: this.snapshot.volunteers,
    assignments: this.snapshot.assignments,
  })
}
```

A conflict response contains an authoritative board, so applying it can populate the cache. An ordinary failure rolls back its optimistic item but leaves the board invalidated; the next load checks the server rather than assuming the remaining local snapshot is current.

## Clear at the authenticated feature boundary

The schedule feature owns both stores. Its providers dispose them when the protected route scope unmounts:

```typescript
clear() {
  this.boardCache.clear()
  this.activeLoad = undefined
  this.generation++
  this.setSnapshot({ status: 'idle' })
}

dispose() {
  this.clear()
}
```

Sign-out removes the authenticated schedule route and therefore clears retained data, aborts active requests, and invalidates late mutation callbacks. A future design that changes users or roles without unmounting this feature must add an explicit identity key and call `clear()` whenever that key changes.

This distinction matters: cache isolation is a lifecycle guarantee, not a property of TypeScript types.

## Test policy with injected clocks

Store tests advance a plain number rather than waiting for timers:

```typescript
let now = 0
const store = new ScheduleStore(repository, () => now)

await store.load()
now = 30_000
await store.load() // Still fresh at the boundary.
now = 30_001
await store.load() // Starts a new repository request.
```

The expanded tests verify:

- separate 30-second list and 60-second detail lifetimes;
- list invalidation after event creation;
- synchronization of visible and retained staffing data;
- a 15-second assignment-board lifetime;
- reuse after a successful confirmed assignment;
- reload after clearing or an ordinary mutation failure;
- cancellation and idle reset when a feature store is disposed.

## Verify the whole application

Part 19 finishes with more than cache-unit tests. The application passes its architecture rule, linter, TypeScript build, production Vite build, 90 React tests, and 16 API integration tests. Statement coverage is 91.48 percent, and `requestCache.ts` has complete line coverage.

These numbers do not prove that a freshness policy is ideal. They show that the chosen behavior is explicit and protected while the feature evolves.

## Result

ChurchServe now reuses server data without treating every response equally. Lists, details, and assignment boards have separate bounded policies. Event creation invalidates broad results, confirmed assignments synchronize related views, speculative data stays out of the cache, and protected-route teardown clears identity-bound state.

Part 20 will move from network work to rendering work and keep larger schedule views responsive.
