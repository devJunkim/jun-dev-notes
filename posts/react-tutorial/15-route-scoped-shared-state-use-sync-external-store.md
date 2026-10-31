---
title: "ChurchServe React Tutorial Part 15: Route-Scoped Shared State with useSyncExternalStore"
excerpt: "Coordinate schedule loading, retries, detail caching, creation, and derived summaries through a route-scoped React external store."
category: "React Tutorial"

seo:
  focusKeyword: "React useSyncExternalStore route scoped state"
  description: "Build route-scoped React state with useSyncExternalStore, immutable snapshots, request coordination, event caching, cleanup, and focused tests."
  socialTitle: "Route-Scoped React State with useSyncExternalStore"
  socialDescription: "Share schedule state across React routes without creating an application-global store or replacing the API as the source of truth."
---

# ChurchServe React Tutorial Part 15: Route-Scoped Shared State with useSyncExternalStore

[Part 14-2](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-14-2-route-loaders-and-unsaved-change-blocking/) moved detail fetching into React Router and made unfinished form work part of navigation. The schedule list, detail loader, and creation page still coordinate related data independently, though. The list owns request state, the loader requests a record, and creation knows nothing about the detail request that immediately follows it.

Part 15 gives that feature state one bounded home. A `ScheduleStore` owns list loading, retry policy, cancellation, an in-memory event cache, and a derived open-position total. React reads immutable store snapshots with `useSyncExternalStore`. A pathless route provider shares the store across schedule pages and resets it when navigation leaves the feature.

> **Quick answer:** Keep the repository responsible for HTTP and persistence, then place client request policy in a small external store. Return a stable immutable snapshot from `getSnapshot`, notify subscribers only after the snapshot changes, and connect React through `useSyncExternalStore`. Make the store available to loaders when route objects are created, but mount its provider only around schedule routes and dispose its data when that provider unmounts. Cache only confirmed repository results and derive summaries instead of synchronizing duplicate writable values.

## What changes in Part 15

This checkpoint changes client-side ownership rather than the server contract:

1. A schedule store coordinates list loading, duplicate suppression, forced reloads, cancellation, and retry feedback.
2. The list page subscribes to immutable store snapshots instead of owning its own effect state machine.
3. Loaded detail records and successful creations enter a feature cache.
4. The detail loader asks the store for a record and reuses a cached event when available.
5. A derived snapshot value reports the number of unfilled positions across the loaded schedule.
6. A pathless route provider shares one store across list, create, and detail routes.
7. Leaving the schedule route aborts active list work and clears both request state and cached records.

There are no ASP.NET Core, API, database, migration, or authentication changes. The API remains the durable source of truth. The store is a short-lived browser coordination layer.

## Why not put all state directly in context

React context is useful for distributing a dependency. It does not define request policy by itself.

Part 14 provided the repository through application-level context, while `SchedulePage` owned this state:

```typescript
const [state, setState] = useState<ScheduleState>({ status: 'loading' })
const [requestVersion, setRequestVersion] = useState(0)
```

That page also owned its controller, retry counter, success transition, failure transition, and stale-response guard. The detail loader could not reuse anything the page had loaded because a router loader executes outside the React component tree.

One option is to place a large changing state object into context. Every context value replacement would notify every consumer, and non-component loaders still could not call a hook. Instead, Part 15 uses context only to distribute a stable `ScheduleStore` object. Components subscribe to its snapshots through React's external-store API, while loaders call its methods directly.

This is deliberately small infrastructure, not an argument that every React application needs a custom store. Local `useState` and `useReducer` remain better defaults when state belongs to one component tree and router loaders do not need the same coordination boundary.

## Model immutable schedule snapshots

Create `scheduleStore.ts` and define the public render states:

```typescript
type ScheduleIdleSnapshot = {
  readonly status: 'idle'
}

type ScheduleLoadingSnapshot = {
  readonly status: 'loading'
}

type ScheduleErrorSnapshot = {
  readonly status: 'error'
  readonly message: string
}

type ScheduleReadySnapshot = {
  readonly status: 'ready'
  readonly events: readonly ServiceEvent[]
  readonly totalOpenPositions: number
}

export type ScheduleSnapshot =
  | ScheduleIdleSnapshot
  | ScheduleLoadingSnapshot
  | ScheduleErrorSnapshot
  | ScheduleReadySnapshot
```

The discriminant prevents the UI from reading events before they exist or treating an error as an empty schedule. The store starts with stable shared objects:

```typescript
const idleSnapshot: ScheduleIdleSnapshot = { status: 'idle' }
const loadingSnapshot: ScheduleLoadingSnapshot = { status: 'loading' }
```

React compares successive `getSnapshot()` results with `Object.is`. Returning a newly created object on every read would make React believe the store changed continuously. The store therefore keeps one snapshot reference until a real transition replaces it.

React's [`useSyncExternalStore` documentation](https://react.dev/reference/react/useSyncExternalStore) requires this cached snapshot behavior and recommends immutable snapshots when the underlying store contains mutable data.

## Implement the subscription contract

The store owns a repository, its current snapshot, subscribers, cached records, and an optional active list request:

```typescript
type Listener = () => void

export class ScheduleStore {
  private readonly repository: ServiceEventRepository
  private snapshot: ScheduleSnapshot = idleSnapshot
  private readonly listeners = new Set<Listener>()
  private readonly eventCache = new Map<string, ServiceEvent>()
  private activeListRequest?: {
    readonly controller: AbortController
    readonly promise: Promise<void>
  }

  constructor(repository: ServiceEventRepository) {
    this.repository = repository
  }

  readonly subscribe = (listener: Listener) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  readonly getSnapshot = () => this.snapshot
}
```

Both functions are arrow properties so their identity remains stable and they retain the store instance when passed directly to a hook.

`subscribe` returns cleanup that removes the exact listener. `getSnapshot` returns the current immutable object without deriving a fresh wrapper.

One private method publishes changes:

```typescript
private setSnapshot(snapshot: ScheduleSnapshot) {
  if (Object.is(this.snapshot, snapshot)) {
    return
  }

  this.snapshot = snapshot
  for (const listener of this.listeners) {
    listener()
  }
}
```

Update the snapshot before notifying. React then calls `getSnapshot` and receives the new reference. Consumers do not receive mutable internals or a public `setState` escape hatch.

## Coordinate list loading and duplicate requests

The list method handles normal entry, duplicate calls, and forced refresh:

```typescript
load(force = false): Promise<void> {
  if (!force && this.snapshot.status === 'ready') {
    return Promise.resolve()
  }

  if (!force && this.activeListRequest) {
    return this.activeListRequest.promise
  }

  this.activeListRequest?.controller.abort()
  const controller = new AbortController()
  this.setSnapshot(loadingSnapshot)

  const promise = this.repository
    .list(controller.signal)
    .then((events) => {
      if (
        controller.signal.aborted ||
        this.activeListRequest?.controller !== controller
      ) {
        return
      }

      this.cacheEvents(events)
      this.setSnapshot({
        status: 'ready',
        events,
        totalOpenPositions: countOpenPositions(events),
      })
    })
    .catch((error: unknown) => {
      if (
        controller.signal.aborted ||
        this.activeListRequest?.controller !== controller ||
        isAbortError(error)
      ) {
        return
      }

      this.setSnapshot({
        status: 'error',
        message: 'The scheduling service did not respond. Try again.',
      })
    })
    .finally(() => {
      if (this.activeListRequest?.controller === controller) {
        this.activeListRequest = undefined
      }
    })

  this.activeListRequest = { controller, promise }
  return promise
}
```

Repeated mounting or calls while a request is pending reuse its promise. A ready schedule skips another request. A forced load aborts its predecessor before starting fresh work.

The controller identity check matters even when an adapter accepts `AbortSignal`. Cancellation is cooperative, so an obsolete fake, cache, or legacy adapter could still resolve. Only the currently registered controller may publish state.

Retry is an explicit forced load:

```typescript
retry() {
  return this.load(true)
}
```

The store owns this policy once. Components do not need request-version counters or duplicate cancellation logic.

## Derive open positions once per successful snapshot

The ready snapshot contains a summary derived from the same event array:

```typescript
function countOpenPositions(events: readonly ServiceEvent[]) {
  return events.reduce(
    (total, event) =>
      total + Math.max(
        0,
        event.requiredPositions - event.assignedPositions,
      ),
    0,
  )
}
```

`Math.max(0, ...)` prevents an inconsistent record from displaying negative open positions. It is a presentation invariant, not a replacement for server validation.

The value is created with the ready snapshot because both depend on one confirmed list response. It is not independently writable, so there is no second source of truth to synchronize.

This API currently returns the complete upcoming schedule rather than a page. The summary therefore describes the loaded collection. If the server later adds pagination, the UI must label the value as page-specific or request an aggregate from the API.

## Cache only confirmed repository results

List results populate a small map:

```typescript
private cacheEvents(events: readonly ServiceEvent[]) {
  for (const event of events) {
    this.eventCache.set(event.id, event)
  }
}
```

Detail lookup checks that map before using the repository:

```typescript
async findById(eventId: string, signal?: AbortSignal) {
  signal?.throwIfAborted()
  const cachedEvent = this.eventCache.get(eventId)
  if (cachedEvent) {
    return cachedEvent
  }

  const event = await this.repository.findById(eventId, signal)
  signal?.throwIfAborted()
  if (event) {
    this.cacheEvents([event])
  }
  return event
}
```

Checking the signal before a cache hit preserves the repository's abort-aware contract. The second check prevents an obsolete response from entering the cache after navigation cancellation.

Creation also caches only the confirmed result:

```typescript
async create(
  command: CreateServiceEventCommand,
  signal?: AbortSignal,
) {
  const event = await this.repository.create(command, signal)
  signal?.throwIfAborted()
  this.cacheEvents([event])
  return event
}
```

The store does not invent a temporary ID or treat optimistic client data as durable. When creation succeeds, the following detail loader can reuse the exact record returned by the API without an immediate second GET.

This cache has no time-to-live or background revalidation. Its deliberately short route lifetime bounds staleness for the current application. Future update and deletion operations must replace or invalidate entries using confirmed server outcomes.

## Connect React with context and useSyncExternalStore

Keep the context and hooks in `scheduleStoreContext.ts`:

```typescript
import { createContext, useContext, useSyncExternalStore } from 'react'

export const ScheduleStoreContext =
  createContext<ScheduleStore | null>(null)

export function useScheduleStore() {
  const store = useContext(ScheduleStoreContext)
  if (!store) {
    throw new Error(
      'Schedule store hooks must be used inside ScheduleStoreProvider.',
    )
  }
  return store
}

export function useScheduleSnapshot() {
  const store = useScheduleStore()
  return useSyncExternalStore(store.subscribe, store.getSnapshot)
}
```

Context distributes one stable store object. `useSyncExternalStore` owns the render subscription and cleanup. Components calling only `useScheduleStore` can send commands without subscribing to every snapshot; components that render state call `useScheduleSnapshot`.

The provider defines the active feature lifetime:

```tsx
export function ScheduleStoreProvider({
  children,
  store,
}: ScheduleStoreProviderProps) {
  useEffect(() => () => store.dispose(), [store])

  return (
    <ScheduleStoreContext value={store}>
      {children}
    </ScheduleStoreContext>
  )
}
```

On unmount, `dispose()` aborts list work, clears the cache, and restores the idle snapshot:

```typescript
dispose() {
  this.activeListRequest?.controller.abort()
  this.activeListRequest = undefined
  this.eventCache.clear()
  this.setSnapshot(idleSnapshot)
}
```

The class remains independent of React. The provider owns the React lifecycle decision.

## Scope the provider to schedule routes

The detail loader needs the store before React renders a provider. Create the store while composing route objects, then mount its provider only around schedule descendants:

```tsx
export function createAppRoutes(
  repository: ServiceEventRepository,
  sessionClient: SessionClient,
): RouteObject[] {
  const scheduleStore = new ScheduleStore(repository)

  return [
    {
      path: '/',
      element: <App sessionClient={sessionClient} />,
      children: [
        { index: true, Component: HomePage },
        { path: 'sign-in', Component: SignInPage },
        {
          Component: RequireAuthenticatedSession,
          children: [
            {
              element: (
                <ScheduleStoreProvider store={scheduleStore}>
                  <Outlet />
                </ScheduleStoreProvider>
              ),
              children: [
                { path: 'schedule', Component: SchedulePage },
                {
                  path: 'schedule/new',
                  Component: ServiceEventCreatePage,
                },
                {
                  path: 'schedule/:eventId',
                  loader: createServiceEventDetailLoader(scheduleStore),
                  Component: ServiceEventDetailPage,
                  ErrorBoundary: ServiceEventDetailErrorPage,
                },
              ],
            },
            { path: 'volunteers', Component: VolunteersPage },
          ],
        },
      ],
    },
  ]
}
```

The pathless route adds a provider boundary without changing URLs. Moving among `/schedule`, `/schedule/new`, and `/schedule/:eventId` keeps that boundary mounted and preserves the store. Moving to `/volunteers`, home, or sign-in unmounts it and disposes the data.

The JavaScript object itself was created with the router, so its allocation lifetime is longer than the provider. Its data lifetime is still feature-scoped because disposal clears every request and cached value when the route boundary leaves the match. This arrangement is intentional: data loaders run before route elements render and therefore cannot read React context.

Do not move the provider to `App` merely for convenience. A root store would retain schedule data across sign-out, identity changes, and unrelated features unless every transition gained explicit invalidation logic.

## Render the shared snapshot

`SchedulePage` no longer owns repository request state:

```tsx
export function SchedulePage() {
  const store = useScheduleStore()
  const state = useScheduleSnapshot()

  useEffect(() => {
    void store.load()
  }, [store])

  // render states
}
```

The effect expresses one command: ensure the schedule is loaded. The store decides whether that means start a request, reuse a pending promise, or keep an existing ready snapshot.

Error retry delegates to the same boundary:

```tsx
<button type="button" onClick={() => void store.retry()}>
  Try again
</button>
```

The ready UI shows derived summaries before the existing actions and cards:

```tsx
{state.status === 'ready' && (
  <>
    <section
      className="schedule-summary"
      aria-label="Current schedule summary"
    >
      <p>
        <strong>{state.events.length}</strong> upcoming services
      </p>
      <p>
        <strong>{state.totalOpenPositions}</strong> open positions
      </p>
    </section>

    {/* create action, empty state, and event cards */}
  </>
)}
```

The visible state remains discriminated. An empty ready array is successful data, not loading or failure.

## Reuse the store in creation and detail loading

The create page changes its dependency but retains its pending, cancellation, dirty-form, and failure behavior:

```typescript
const store = useScheduleStore()

const event = await store.create(command, controller.signal)
controller.signal.throwIfAborted()
hasSaved.current = true
setIsDirty(false)
await navigate(`/schedule/${event.id}`)
```

The detail loader receives only the method it needs:

```typescript
export function createServiceEventDetailLoader(
  store: Pick<ScheduleStore, 'findById'>,
): LoaderFunction {
  return async ({ params, request }) => {
    const eventId = params.eventId
    // parameter validation omitted

    const event = await store.findById(eventId, request.signal)
    // not-found and failure translation omitted
    return event
  }
}
```

The loader keeps its Part 14 responsibility for translating missing records and failures into route responses. The store adds lookup reuse; it does not absorb router error presentation.

This separation is important. The repository understands transport, the store understands client coordination and caching, the loader understands route entry, and the component understands rendering.

## Test the store through its public behavior

Focused store tests use an injected repository and inspect public snapshots and methods.

Loading and derivation:

```typescript
it('loads events and derives the total number of open positions', async () => {
  const store = new ScheduleStore(createRepository())

  await store.load()

  expect(store.getSnapshot()).toEqual({
    status: 'ready',
    events: seededServiceEvents,
    totalOpenPositions: 3,
  })
})
```

Duplicate suppression:

```typescript
const pending = deferred<readonly ServiceEvent[]>()
const list = vi.fn().mockReturnValue(pending.promise)
const store = new ScheduleStore(createRepository({ list }))

const firstLoad = store.load()
const duplicateLoad = store.load()

expect(list).toHaveBeenCalledOnce()
pending.resolve(seededServiceEvents)
await Promise.all([firstLoad, duplicateLoad])
await store.load()
expect(list).toHaveBeenCalledOnce()
```

Other focused cases verify that:

- a forced load prevents an obsolete response from publishing;
- failure enters the error snapshot and retry can recover;
- a listed record satisfies later detail lookup without repository access;
- a confirmed creation satisfies its immediate detail lookup;
- disposal aborts active work and restores idle state.

Application tests provide the real route configuration. One opens a schedule card and verifies the detail page appears without calling `repository.findById`. Another leaves for Volunteers, returns to Schedule, and verifies that list loading occurs again. Those tests prove both cache reuse inside the feature and disposal outside it.

## Run the complete Part 15 checkpoint

Run the React quality gate:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The finalized checkpoint passes lint without warnings, passes strict TypeScript checking, runs 74 React tests across 12 files, and builds the Vite production bundle.

The API contract did not change, but its existing verification still passes:

```bash
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj \
  --configuration Release
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The API builds with zero warnings and errors, all nine integration tests pass, and EF Core reports no pending model changes.

## Production boundaries

This store is intentionally bounded, but shared state still needs explicit policy:

- `useSyncExternalStore` snapshots must remain referentially stable between real changes. Never allocate a new wrapper on every `getSnapshot()` call.
- The cache has no freshness guarantee. Add revalidation, versioning, or invalidation when product requirements introduce concurrent edits.
- Cache keys may need tenant, user, locale, or permission context in a multi-tenant system.
- Route disposal prevents data from crossing sign-out in this application. Any future global store must clear sensitive data on identity and authorization changes.
- Cancellation prevents stale publication and may save transport work, but it cannot undo a server mutation that already committed.
- Duplicate suppression is appropriate for the current unfiltered list. Query parameters would need to become part of the store key before adding filters or pagination.
- Error messages shown to users remain bounded. Diagnostics should not record cookies, antiforgery tokens, credentials, or complete sensitive responses.
- A custom store should remain smaller than the problem it solves. A mature query library may be preferable when requirements include background refresh, deduplication across many resources, optimistic mutations, offline behavior, and cache eviction.
- Server rendering would require a compatible `getServerSnapshot` and hydration strategy. This tutorial checkpoint is client-rendered.

## What Part 15 established

ChurchServe now shares schedule state across related routes without turning it into application-global state:

- one external store owns schedule request policy and the feature cache;
- immutable discriminated snapshots describe renderable state;
- stable subscription functions connect React through `useSyncExternalStore`;
- duplicate loads are suppressed and obsolete forced loads cannot publish;
- retry behavior lives beside the request it restarts;
- open positions are derived from the confirmed event collection;
- list, detail, and creation results populate the same bounded cache;
- detail loaders can reuse data without calling React hooks;
- a pathless provider preserves state inside schedule routes;
- provider cleanup aborts work and clears data when the feature is left;
- focused and routed tests verify coordination, cache reuse, and disposal.

Part 16 can build on these public seams with an integrated testing strategy that exercises components, stores, repositories, HTTP behavior, and complete routed workflows without coupling tests to private implementation details.
