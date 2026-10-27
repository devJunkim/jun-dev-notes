---
title: "ChurchServe Angular Tutorial Part 20: Rendering Performance for Large Schedules"
excerpt: "Preserve stable event identity, replace repeated template scans with computed indexes, and choose rendering optimizations from measured constraints."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular rendering performance tutorial"
  description: "Improve Angular rendering performance with stable object identity, keyed reconciliation, computed indexes, OnPush, stable tracking, and tests."
  socialTitle: "ChurchServe Part 20: Faster Angular Rendering"
  socialDescription: "Reduce avoidable schedule rendering work without adding unnecessary virtualization or deferred primary UI."
---

# ChurchServe Angular Tutorial Part 20: Rendering Performance for Large Schedules

Part 19 reduced duplicate network work with bounded caches and request coordination. A cached response can still contain newly parsed objects, however, and a template method can still allocate a new filtered array every time Angular evaluates it.

ChurchServe already has several strong rendering boundaries: `OnPush` components, signal state, stable `track event.id` expressions, lazy feature routes, and server-side pagination. Part 20 preserves those decisions and targets two remaining costs with evidence from the code.

> **Quick answer:** Reconcile refreshed entities by stable ID so unchanged objects keep their references, and move repeated row lookups into computed indexes that recalculate only when source signals change. Keep `OnPush`, stable tracking, and server pagination. Do not add virtual scrolling or deferred primary content without a measured need.

## What Changes in Part 20

```text
src/app/
├── core/
│   └── data/
│       ├── reconcile-by-key.spec.ts
│       └── reconcile-by-key.ts
└── features/
    └── schedule/
        ├── assignment-store.spec.ts
        ├── assignment-store.ts
        ├── schedule-store.spec.ts
        ├── schedule-store.ts
        └── service-event.ts
```

The visible workflow does not change. The internal guarantees do:

1. A refreshed page reuses each unchanged `ServiceEvent` object.
2. A changed event receives a new object while unaffected events retain identity.
3. Reordering respects the server’s new order without recreating unchanged entities.
4. Assignment lookup becomes a map read instead of a scan for every position row.
5. Eligible-volunteer lists are grouped once per volunteer-state change instead of allocated repeatedly from the template.

## Measure a Specific Interaction First

Use Angular DevTools and the browser Performance panel to record one workflow, such as changing a ministry filter and waiting for the list to settle. Separate the time spent in:

- the HTTP request;
- JSON parsing and state updates;
- Angular change detection;
- DOM creation and reconciliation;
- layout and paint.

A smaller bundle does not fix an expensive row expression. Memoization does not fix a slow API. Virtual scrolling does not help a four-item server-paged result. Define the slow interaction before selecting a technique.

Part 20 protects structural assumptions with unit and component tests. It does not claim a frame-rate improvement without a repeatable browser baseline.

## Audit the Existing Rendering Boundaries

The schedule page already renders cards with a stable business key:

```html
@for (event of store.events(); track event.id) {
  <app-service-event-card [event]="event" />
}
```

`ServiceEventCard` already uses `ChangeDetectionStrategy.OnPush`, and its open-position count is a computed signal:

```typescript
@Component({
  selector: 'app-service-event-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventCard {
  readonly event = input.required<ServiceEvent>();

  protected readonly openPositions = computed(() =>
    Math.max(0, this.event().requiredPositions - this.event().assignedPositions),
  );
}
```

Tracking by `event.id` lets Angular move an existing card when order changes. Tracking by array index would associate DOM with a position rather than an event and is incorrect when events can be inserted, removed, or reordered.

Stable tracking is only part of the story. If every refresh constructs a new object for every event, each `OnPush` card still receives a changed input reference. We can preserve identity for records whose values did not change.

## Create a Generic Keyed Reconciler

Create `src/app/core/data/reconcile-by-key.ts`:

```typescript
export function reconcileByKey<T, TKey>(
  previous: readonly T[],
  incoming: readonly T[],
  keyOf: (item: T) => TKey,
  equal: (left: T, right: T) => boolean,
): readonly T[] {
  if (incoming.length === 0) {
    return previous.length === 0 ? previous : incoming;
  }

  const previousByKey = new Map(previous.map((item) => [keyOf(item), item]));
  let matchesPreviousArray = previous.length === incoming.length;

  const reconciled = incoming.map((item, index) => {
    const existing = previousByKey.get(keyOf(item));
    const value = existing && equal(existing, item) ? existing : item;
    if (value !== previous[index]) {
      matchesPreviousArray = false;
    }
    return value;
  });

  return matchesPreviousArray ? previous : reconciled;
}
```

The function separates identity policy from domain equality:

- `keyOf` finds the matching previous entity.
- `equal` decides whether its values are unchanged.
- unchanged entities reuse the previous object;
- changed or new entities use the incoming object;
- incoming order always wins;
- if every position already contains the same reference, the previous array is reused too.

The function is linear in the collection size: one map construction and one pass over the incoming array. A nested search would turn reconciliation into quadratic work as a page grows.

## Define Service-Event Equality Explicitly

Add the equality function next to the domain interface in `service-event.ts`:

```typescript
export interface ServiceEvent {
  readonly id: string;
  readonly title: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly location: string;
  readonly requiredPositions: number;
  readonly assignedPositions: number;
  readonly version: number;
}

export function equalServiceEvent(left: ServiceEvent, right: ServiceEvent): boolean {
  return (
    left.id === right.id &&
    left.title === right.title &&
    left.startsAt === right.startsAt &&
    left.endsAt === right.endsAt &&
    left.location === right.location &&
    left.requiredPositions === right.requiredPositions &&
    left.assignedPositions === right.assignedPositions &&
    left.version === right.version
  );
}
```

Listing fields is intentionally explicit. A generic deep-equality operation would do more work, obscure the contract, and make it easy to compare irrelevant properties later.

Whenever a property is added to `ServiceEvent`, review this function. Omitting a render-relevant field could incorrectly reuse a stale object.

## Reconcile Each Loaded Page

Update the successful branch of `ScheduleStore.load`:

```typescript
next: (page) => {
  const items = reconcileByKey(
    this.pageValue()?.items ?? [],
    page.items,
    (event) => event.id,
    equalServiceEvent,
  );
  const reconciledPage = items === page.items ? page : { ...page, items };
  this.pageValue.set(reconciledPage);
  this.cacheEvents(items);
  this.loadStateValue.set('loaded');
},
```

This works for network responses and cached pages. If an assignment count changes on one event, only that event gets a new reference. Cards for unchanged events keep the same input objects and can avoid unnecessary work.

The assignment-summary update introduced in Part 19 already follows the same principle:

```typescript
items: page.items.map((event) =>
  event.id === eventId ? { ...event, assignedPositions, version } : event,
),
```

Only the matching event is copied. Every other element is returned as the original object.

## Replace Repeated Assignment Scans with Computed Indexes

The service-event detail template renders one row per position. It calls `assignmentFor(position.id)` and `volunteersFor(position)` for each row.

Previously, `assignmentFor` scanned all assignments, and `volunteersFor` called `.filter()`, allocating a new array on every invocation. Move both transformations into computed signals inside `AssignmentStore`:

```typescript
const NO_VOLUNTEERS: readonly Volunteer[] = [];

private readonly assignmentsByPosition = computed(() => {
  const result = new Map<string, Assignment>();
  for (const assignment of this.assignmentsState()) {
    if (assignment.status !== 'declined') {
      result.set(assignment.positionId, assignment);
    }
  }
  return result;
});

private readonly volunteersByMinistry = computed(() => {
  const result = new Map<string, Volunteer[]>();
  for (const volunteer of this.volunteersState()) {
    for (const ministry of volunteer.eligibleMinistries) {
      const volunteers = result.get(ministry) ?? [];
      volunteers.push(volunteer);
      result.set(ministry, volunteers);
    }
  }
  return result;
});
```

Angular recomputes each index only when its source signal changes. Reading the same position during later template checks returns data from the existing map.

Update the public lookup methods:

```typescript
assignmentFor(positionId: string): Assignment | undefined {
  return this.assignmentsByPosition().get(positionId);
}

volunteersFor(position: MinistryPosition): readonly Volunteer[] {
  return this.volunteersByMinistry().get(position.ministry) ?? NO_VOLUNTEERS;
}
```

`NO_VOLUNTEERS` is a stable fallback. Returning `[]` directly would create a new array on every missing lookup and undermine the reference guarantee.

This is not premature memoization of arbitrary calculations. These methods sit inside a repeated template, and one of them previously allocated on every call. The computed indexes make the cost proportional to state changes rather than template evaluations.

## Test Referential Stability Directly

Create `reconcile-by-key.spec.ts`. When values and order are unchanged, retain the array and its objects:

```typescript
it('returns the previous array when order and values are unchanged', () => {
  const previous: readonly Item[] = [
    { id: 'one', value: '1' },
    { id: 'two', value: '2' },
  ];
  const incoming = previous.map((item) => ({ ...item }));

  const result = reconcileByKey(previous, incoming, (item) => item.id, equalItem);

  expect(result).toBe(previous);
});
```

If one value changes, reuse only the unchanged entity:

```typescript
it('preserves unchanged items when another item changes', () => {
  const previous: readonly Item[] = [
    { id: 'one', value: '1' },
    { id: 'two', value: '2' },
  ];
  const incoming: readonly Item[] = [
    { id: 'one', value: '1' },
    { id: 'two', value: 'updated' },
  ];

  const result = reconcileByKey(previous, incoming, (item) => item.id, equalItem);

  expect(result).not.toBe(previous);
  expect(result[0]).toBe(previous[0]);
  expect(result[1]).toBe(incoming[1]);
});
```

Additional tests cover reordering and the stable empty-array case.

## Test the Store-Level Behavior

A utility test is not enough; prove that `ScheduleStore` uses it during a forced refresh:

```typescript
it('preserves unchanged event objects when a refreshed page changes one event', () => {
  const second = { ...event, id: 'event-2', title: 'Evening Service' };
  repository.list
    .mockReturnValueOnce(of(page([event, second])))
    .mockReturnValueOnce(
      of(page([{ ...event }, { ...second, assignedPositions: 8, version: 2 }])),
    );
  store.load(query);
  const before = store.events();

  store.load(query, true);
  const after = store.events();

  expect(after).not.toBe(before);
  expect(after[0]).toBe(before[0]);
  expect(after[1]).not.toBe(before[1]);
});
```

The existing component test also reorders events and verifies Angular moves the same card DOM elements rather than creating replacements.

For eligible volunteers, assert reference stability rather than only equal contents:

```typescript
const firstRead = store.volunteersFor(positions[0]);
const secondRead = store.volunteersFor(positions[0]);

expect(firstRead.map((item) => item.id)).toEqual(['alex']);
expect(secondRead).toBe(firstRead);
```

A separate test confirms the assignment index returns an accepted assignment and ignores a declined one.

## Why We Do Not Add Virtual Scrolling

ChurchServe already requests a server-paged schedule with four events per page. Virtual scrolling would add dependency weight, focus-management complexity, scroll-position behavior, and accessibility testing while rendering fewer DOM nodes than a typical viewport can comfortably handle.

Virtualization becomes reasonable for a measured, long, mostly uniform flat list that users must scan continuously. A calendar or grouped schedule with variable-height content may be better served by server pagination or incremental disclosure.

The existing pagination is both a data-transfer boundary and a rendering boundary. Keep it until evidence shows that the user workflow needs a different interaction.

## Why We Do Not Defer the Primary Assignment Board

Angular `@defer` is useful for secondary work such as an audit-history panel below the main workflow. The assignment board is the primary action on the service detail page. Deferring it would delay the content coordinators came to use and require additional placeholder, loading, error, keyboard, and screen-reader behavior.

Lazy routes already delay downloading feature code until navigation. Deferred views solve a different problem inside an active route. Use them only when a secondary section has enough initialization or rendering cost to justify the extra state.

## Result

Part 20 improves rendering behavior without changing the user interface or adding a large dependency. Refreshed pages retain unchanged event objects, stable `track` keys preserve card DOM identity, and assignment rows use computed indexes instead of repeated scans and allocations.

The tests protect those assumptions, but they do not substitute for browser profiling. If a real schedule interaction becomes slow, record it on consistent hardware, identify whether JavaScript, change detection, layout, or paint dominates, and establish a performance budget only after the baseline is reliable.

Part 21 will design error handling and resilience for failures that remain possible even after careful implementation.
