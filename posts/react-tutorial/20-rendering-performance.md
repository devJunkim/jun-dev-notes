---
title: "ChurchServe React Tutorial Part 20: Rendering Performance for Large Schedules"
excerpt: "Preserve stable event identity, memoize card rendering, replace repeated row scans with indexed lookups, and optimize only from measured constraints."
category: "React Tutorial"

seo:
  focusKeyword: "React rendering performance tutorial"
  description: "Improve React rendering performance with stable keys, keyed reconciliation, React memo, indexed lookups, profiling, and focused tests."
  socialTitle: "ChurchServe React Part 20: Faster Schedule Rendering"
  socialDescription: "Reduce avoidable React schedule work with stable references, memoized cards, and store-owned lookup indexes."
---

# ChurchServe React Tutorial Part 20: Rendering Performance for Large Schedules

[Part 19-2](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-19-2-coordinate-schedule-caches/) reduced duplicate network work with bounded caches. A refreshed response can still contain new JavaScript objects for records whose values did not change, and the assignment board can still scan or filter whole arrays for every rendered position.

> **Quick answer:** Keep `event.id` as the React key, reconcile refreshed entities by ID so unchanged records retain their object references, and wrap the card boundary in `memo`. Move repeated assignment and volunteer searches into store-owned indexes that rebuild only when their source arrays change. Profile a production build before adding heavier techniques such as virtualization.

## Start with a specific interaction

Use the React DevTools Profiler and browser Performance panel to record one workflow, such as forcing a schedule refresh or confirming a volunteer assignment. Separate time spent in:

- the HTTP request and JSON parsing;
- external-store updates;
- React component rendering;
- DOM reconciliation;
- layout and paint.

A smaller bundle does not fix a repeated row scan. Memoization does not fix a slow API. Virtualization does not help a short list. Part 20 protects concrete structural assumptions with tests, but it does not invent a frame-rate improvement without a repeatable browser baseline.

## Understand keys and memoization as different tools

The schedule already renders with a stable business key:

```tsx
{state.events.map((event) => (
  <li key={event.id}>
    <ServiceEventCard event={event} />
  </li>
))}
```

React uses a key to match an item with its previous place when a list is inserted, removed, or reordered. The [official list-rendering guide](https://react.dev/learn/rendering-lists#keeping-list-items-in-order-with-key) recommends stable IDs from the data rather than array indexes or values generated during rendering.

A key does not by itself skip the child function when the parent renders. Wrap the card in `memo`:

```tsx
import { memo } from 'react'

export const ServiceEventCard = memo(function ServiceEventCard({
  event,
}: ServiceEventCardProps) {
  const openPositions = Math.max(
    0,
    event.requiredPositions - event.assignedPositions,
  )

  return <article className="event-card">{/* ... */}</article>
})
```

By default, [`memo`](https://react.dev/reference/react/memo) compares each prop with `Object.is`. That means a newly parsed `{ ...event }` fails the comparison even when every field has the same value.

React also documents `memo` as a performance optimization rather than a semantic guarantee. The application must remain correct if React renders the component again. Avoid custom deep prop comparisons here; preserve the domain object reference before it reaches the component instead.

## Create a generic keyed reconciler

Add `src/core/data/reconcileByKey.ts`:

```typescript
export function reconcileByKey<T, TKey>(
  previous: readonly T[],
  incoming: readonly T[],
  keyOf: (item: T) => TKey,
  equal: (left: T, right: T) => boolean,
): readonly T[] {
  if (incoming.length === 0) {
    return previous.length === 0 ? previous : incoming
  }

  const previousByKey = new Map(
    previous.map((item) => [keyOf(item), item]),
  )
  let matchesPreviousArray = previous.length === incoming.length

  const reconciled = incoming.map((item, index) => {
    const existing = previousByKey.get(keyOf(item))
    const value =
      existing !== undefined && equal(existing, item)
        ? existing
        : item
    if (value !== previous[index]) matchesPreviousArray = false
    return value
  })

  return matchesPreviousArray ? previous : reconciled
}
```

The two callbacks keep domain policy outside the generic utility:

- `keyOf` finds the matching previous entity;
- `equal` decides whether its rendered values are unchanged.

Incoming order always wins. Unchanged entities reuse previous objects. Changed and new entities use incoming objects. If the same references already occupy every position, the previous array is reused too.

The algorithm is linear: one map construction and one pass over the incoming collection. A nested `find` would become quadratic as the list grows.

## Make event equality explicit

Define equality beside `ServiceEvent`:

```typescript
export function equalServiceEvent(
  left: ServiceEvent,
  right: ServiceEvent,
) {
  return (
    left.id === right.id &&
    left.title === right.title &&
    left.startsAt === right.startsAt &&
    left.endsAt === right.endsAt &&
    left.location === right.location &&
    left.requiredPositions === right.requiredPositions &&
    left.assignedPositions === right.assignedPositions &&
    left.version === right.version
  )
}
```

Listing the fields is deliberate. A generic deep comparison would obscure which values affect rendering and could become unexpectedly expensive. When a render-relevant field is added to `ServiceEvent`, review this equality function as part of that change.

## Reconcile through loading snapshots

A forced refresh sets the public snapshot to `loading` before its response arrives. The store therefore keeps the last reconciled event references separately:

```typescript
private previousEvents: readonly ServiceEvent[] = []

private applyEvents(
  events: readonly ServiceEvent[],
  retain = true,
) {
  const reconciled = reconcileByKey(
    this.previousEvents,
    events,
    (event) => event.id,
    equalServiceEvent,
  )
  this.previousEvents = reconciled
  if (retain) this.listCache.set('events', reconciled)
  this.cacheEvents(reconciled)
  this.setSnapshot({
    status: 'ready',
    events: reconciled,
    totalOpenPositions: countOpenPositions(reconciled),
  })
}
```

Do not derive the reconciliation base only from the current public snapshot. Once that snapshot becomes `loading`, the previous ready array is no longer available there.

The `retain` flag also preserves the fixed cache lifetime from Part 19. Applying a cache hit to the UI uses `retain = false`; writing that hit back would reset its timestamp on every read and silently create sliding expiration.

When `clear()` runs at feature teardown, it also resets `previousEvents`. References from one authenticated feature scope must not become the reconciliation base for another.

## Let stable references reach the memo boundary

On a refreshed two-event schedule, suppose only staffing for the second event changed. Reconciliation produces:

```text
before: [event A, event B]
after:  [event A, new event B]
```

The parent receives a new array because the collection changed. The first card receives the exact previous `event A` object, so `memo` can usually skip that card. The second card receives a new object and renders its new staffing value.

Stable object identity and `memo` must work together. Reconciliation alone does not stop React from calling an ordinary child component when its parent renders. `memo` alone cannot help if every refresh creates a new object prop.

## Replace repeated row scans with assignment indexes

The assignment panel renders one row per ministry position. Previously each row searched all assignments and filtered all volunteers:

```typescript
const assignment = board.assignments.find(
  (item) =>
    item.positionId === position.id && item.status !== 'declined',
)
const eligibleVolunteers = store.volunteersFor(position)
```

Build indexes in `AssignmentStore` when the corresponding source reference changes:

```typescript
private indexedAssignments?: readonly Assignment[]
private assignmentsByPosition = new Map<string, Assignment>()

assignmentFor(positionId: string): Assignment | undefined {
  if (this.snapshot.status !== 'ready') return undefined

  if (this.indexedAssignments !== this.snapshot.assignments) {
    this.indexedAssignments = this.snapshot.assignments
    this.assignmentsByPosition = new Map(
      this.snapshot.assignments
        .filter((assignment) => assignment.status !== 'declined')
        .map((assignment) => [assignment.positionId, assignment]),
    )
  }

  return this.assignmentsByPosition.get(positionId)
}
```

After the first lookup for a snapshot, every position lookup is a map read. A new optimistic assignment creates a new assignments array, so the next lookup rebuilds the index automatically.

Group volunteers by ministry in the same way:

```typescript
private indexedVolunteers?: readonly Volunteer[]
private volunteersByMinistry =
  new Map<string, readonly Volunteer[]>()

volunteersFor(position: MinistryPosition): readonly Volunteer[] {
  if (this.snapshot.status !== 'ready') return noVolunteers

  if (this.indexedVolunteers !== this.snapshot.volunteers) {
    this.indexedVolunteers = this.snapshot.volunteers
    const grouped = new Map<string, Volunteer[]>()

    for (const volunteer of this.snapshot.volunteers) {
      for (const ministry of volunteer.eligibleMinistries) {
        const eligible = grouped.get(ministry) ?? []
        eligible.push(volunteer)
        grouped.set(ministry, eligible)
      }
    }
    this.volunteersByMinistry = grouped
  }

  return this.volunteersByMinistry.get(position.ministry)
    ?? noVolunteers
}
```

`noVolunteers` is one module-level empty array. Returning `[]` for every miss would allocate a new reference and weaken the same stability guarantee this part is establishing.

The component now performs two direct store lookups:

```typescript
const assignment = store.assignmentFor(position.id)
const eligibleVolunteers = store.volunteersFor(position)
```

These indexes belong in the store because they derive from store-owned state and are shared by every rendered row. A component-level `useMemo` inside each row would repeat the derivation across component instances.

## Test referential behavior directly

Value equality does not prove reference stability. Test the reconciler with `toBe`:

```typescript
it('preserves unchanged items when another item changes', () => {
  const previous = [
    { id: 'one', value: '1' },
    { id: 'two', value: '2' },
  ]
  const incoming = [
    { id: 'one', value: '1' },
    { id: 'two', value: 'updated' },
  ]

  const result = reconcileByKey(
    previous,
    incoming,
    (item) => item.id,
    (left, right) => left.value === right.value,
  )

  expect(result).not.toBe(previous)
  expect(result[0]).toBe(previous[0])
  expect(result[1]).toBe(incoming[1])
})
```

Additional utility tests cover identical arrays, incoming reordering, and stable empty results. A store-level forced-refresh test proves unchanged `ServiceEvent` objects survive the real loading transition.

Assignment tests verify that accepted assignments are indexed, declined assignments are excluded, and two positions in the same ministry receive the same volunteer-array reference.

## Avoid heavier optimizations without evidence

Virtual scrolling adds focus behavior, scroll restoration, variable-height concerns, and accessibility testing. It is useful for a measured long list that must remain continuously scrollable. It is unnecessary for the small schedule currently returned by this application.

The schedule feature is already lazy-loaded, so its code is excluded from the initial route bundle. Deferring the assignment board inside the detail route would delay the primary task coordinators came to perform. Reserve deferred rendering for secondary content whose initialization cost is visible in a profile.

Do not add memoization everywhere either. It has comparison cost and makes reference behavior part of the performance design. Use it where profiling or repeated-list structure provides a concrete reason.

## Verify the application

Part 20 passes the architecture rule, linter, TypeScript check, production Vite build, 96 React tests, and 16 API tests. Statement coverage is 91.81 percent, and the core data utilities have complete line coverage.

Those checks protect behavior; they do not replace profiling. Performance measurements should use a production build, consistent hardware, a repeatable interaction, and enough data to exercise the suspected boundary.

## Result

ChurchServe now preserves unchanged event objects across refreshes, gives `memo` stable props at the card boundary, and replaces repeated assignment-row scans with store-owned indexes. The user interface remains the same, and the implementation adds no rendering dependency.

Part 21 will design error handling and resilience for failures that remain possible after careful implementation.
