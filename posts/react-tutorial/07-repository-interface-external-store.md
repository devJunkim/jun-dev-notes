---
title: "ChurchServe React Tutorial Part 7: Repository Interfaces and External Stores"
excerpt: "Decouple React route pages from in-memory storage with a repository contract, composition-root selection, useSyncExternalStore, and focused implementation tests."
category: "React Tutorial"

seo:
  focusKeyword: "React repository pattern TypeScript tutorial"
  description: "Use a TypeScript repository interface, React context, useSyncExternalStore, and an in-memory implementation to decouple components from storage."
  socialTitle: "ChurchServe Part 7: React Repository Boundaries"
  socialDescription: "Separate React route pages from storage with a typed repository contract, external-store subscriptions, composition, and isolated tests."
---

# ChurchServe React Tutorial Part 7: Repository Interfaces and External Stores

[Part 6](https://dev.jun-kim.net/2026/10/30/churchserve-react-tutorial-part-6-parameterized-routes-and-event-details/) introduced shared in-memory events, but route pages still consume a context shaped around that specific store. Part 8 will add browser persistence, and later parts will introduce HTTP. If components know how the current store works, each storage change will spread through the UI.

Part 7 makes storage a composition decision. Route pages depend on a small `ServiceEventRepository` interface. `InMemoryServiceEventRepository` implements that contract, and the application root selects it. A custom hook uses `useSyncExternalStore` so repository mutations still trigger correct React renders.

> **Quick answer:** Define the repository from the use cases the UI already has, make immutable snapshots and subscriptions part of the React-facing contract, inject one implementation through context, and select that implementation at the application root. Components keep the same behavior without knowing where records are stored.

## What Part 7 Changes

This checkpoint is an architectural refactoring. The visible schedule, creation flow, detail page, and URLs do not change.

Add these files:

```text
src/features/schedule/
├── inMemoryServiceEventRepository.test.ts
├── inMemoryServiceEventRepository.ts
├── ServiceEventProvider.tsx
└── ServiceEventRepository.ts
```

Update the context, application root, schedule, create page, detail page, schedule tests, and README. Remove the Part 6 `serviceEventStore.tsx` and `serviceEventStore.test.tsx` after their behavior moves into the concrete repository and its focused tests.

The dependency changes from this:

```text
Route pages -> concrete context store
```

to this:

```text
Route pages -> ServiceEventRepository <- InMemoryServiceEventRepository
                         ^
                         |
               selected by App composition
```

The interface describes the operations and reactivity guarantees consumers need. It does not expose arrays for mutation or require components to know whether the implementation uses memory, browser storage, or a future remote source.

## Design the Contract From Current Use Cases

Create `src/features/schedule/serviceEventRepository.ts`:

```ts
import type { CreateServiceEventCommand } from './createServiceEvent'
import type { ServiceEvent } from './serviceEvent'

export type ServiceEventListener = () => void

export interface ServiceEventRepository {
  list: () => readonly ServiceEvent[]
  findById: (eventId: string) => ServiceEvent | undefined
  create: (command: CreateServiceEventCommand) => ServiceEvent
  subscribe: (listener: ServiceEventListener) => () => void
}
```

The three data operations come directly from existing pages:

- The schedule lists events.
- The detail route finds one event by ID.
- The creation route creates one event and needs the resulting ID.

There is no update or delete workflow yet, so the interface does not speculate about them.

The fourth operation is a React integration requirement. A repository stores records outside component state, so React needs a way to learn that a snapshot changed. `subscribe` registers a listener and returns cleanup logic.

The methods are function properties instead of prototype-method signatures because `useSyncExternalStore` receives `subscribe` and `list` as standalone callbacks. Arrow-function implementations keep the repository instance bound without wrappers or manual `bind` calls.

The interface is erased when TypeScript compiles. Context carries the actual repository object at runtime while TypeScript verifies that the selected implementation satisfies this contract.

## Implement the In-Memory Repository

Create `src/features/schedule/inMemoryServiceEventRepository.ts`:

```ts
import type { CreateServiceEventCommand } from './createServiceEvent'
import type { ServiceEvent } from './serviceEvent'
import type {
  ServiceEventListener,
  ServiceEventRepository,
} from './serviceEventRepository'

const seededServiceEvents: readonly ServiceEvent[] = [
  {
    id: 'sunday-2026-11-01',
    title: 'Sunday Morning Service',
    startsAt: '2026-11-01T10:00:00-05:00',
    endsAt: '2026-11-01T11:30:00-05:00',
    location: 'Main Sanctuary',
    requiredPositions: 10,
    assignedPositions: 7,
  },
  {
    id: 'rehearsal-2026-11-05',
    title: 'Worship Team Rehearsal',
    startsAt: '2026-11-05T19:00:00-05:00',
    endsAt: '2026-11-05T20:30:00-05:00',
    location: 'Music Room',
    requiredPositions: 6,
    assignedPositions: 6,
  },
]

const createEventId = () => crypto.randomUUID()

export class InMemoryServiceEventRepository
  implements ServiceEventRepository
{
  private events: readonly ServiceEvent[]
  private readonly listeners = new Set<ServiceEventListener>()
  private readonly eventIdFactory: () => string

  constructor(
    initialEvents: readonly ServiceEvent[] = seededServiceEvents,
    eventIdFactory: () => string = createEventId,
  ) {
    this.events = [...initialEvents]
    this.eventIdFactory = eventIdFactory
  }

  readonly list = () => this.events

  readonly findById = (eventId: string) =>
    this.events.find((event) => event.id === eventId)

  readonly create = (command: CreateServiceEventCommand) => {
    const event: ServiceEvent = {
      id: this.eventIdFactory(),
      ...command,
      requiredPositions: 0,
      assignedPositions: 0,
    }

    this.events = [...this.events, event]
    this.listeners.forEach((listener) => listener())
    return event
  }

  readonly subscribe = (listener: ServiceEventListener) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
}
```

The constructor copies its input array so callers cannot add or remove repository records through a retained mutable reference. It also accepts an ID factory for deterministic tests while production uses `crypto.randomUUID()`.

`list` returns the current immutable array reference. Repeated calls return the same reference until data changes. That stability is required by `useSyncExternalStore`, which compares snapshots with `Object.is`.

`create` replaces the collection with a new array before notifying subscribers. React can then read a different snapshot reference and render the update. Mutating the old array would break both readonly ownership and snapshot comparison.

The repository notifies after the new snapshot is ready. Each subscriber receives no record payload; it only learns that it should read the repository again. Unsubscription removes the listener and prevents callbacks after a component unmounts.

The explicit `eventIdFactory` field also respects this project's `erasableSyntaxOnly` setting. TypeScript constructor parameter properties require transformation and are intentionally unavailable here.

## Make Context Carry the Contract

Replace the store-specific shape in `serviceEventContext.ts`:

```ts
import { createContext, useContext, useSyncExternalStore } from 'react'
import type { ServiceEventRepository } from './serviceEventRepository'

export const ServiceEventContext =
  createContext<ServiceEventRepository | null>(null)

export function useServiceEventRepository() {
  const repository = useContext(ServiceEventContext)

  if (!repository) {
    throw new Error(
      'Service event hooks must be used inside ServiceEventProvider.',
    )
  }

  return repository
}

export function useServiceEvents() {
  const repository = useServiceEventRepository()
  return useSyncExternalStore(
    repository.subscribe,
    repository.list,
    repository.list,
  )
}
```

`useServiceEventRepository` gives command-oriented pages the contract selected by the nearest provider. It also fails immediately when composition is missing.

`useServiceEvents` is the reactive read hook. `useSyncExternalStore` receives:

1. The repository subscription function.
2. The client snapshot reader.
3. The server snapshot reader.

This Vite application renders on the client, but supplying the third function keeps the hook's contract explicit for server rendering. A real server-rendered application must ensure that the server snapshot and initial client snapshot contain equivalent transferred data.

Do not return `[...repository.list()]` from the snapshot reader. That would create a different array on every call even when nothing changed and can cause repeated rendering. The repository already creates a new immutable array only when its contents change.

## Create a Provider That Does Not Choose Storage

Create `src/features/schedule/ServiceEventProvider.tsx`:

```tsx
import type { ReactNode } from 'react'
import { ServiceEventContext } from './serviceEventContext'
import type { ServiceEventRepository } from './serviceEventRepository'

type ServiceEventProviderProps = {
  children: ReactNode
  repository: ServiceEventRepository
}

export function ServiceEventProvider({
  children,
  repository,
}: ServiceEventProviderProps) {
  return (
    <ServiceEventContext value={repository}>
      {children}
    </ServiceEventContext>
  )
}
```

The provider distributes a repository but does not construct one. That distinction keeps implementation selection at the composition root and makes tests able to provide a focused fake or an isolated in-memory instance.

The provider module exports only a component, satisfying the project's Fast Refresh lint boundary. Context declarations and hooks remain in a non-component module.

## Select the Implementation in App

Update `App.tsx` imports:

```tsx
import { useState } from 'react'
import { InMemoryServiceEventRepository } from './features/schedule/inMemoryServiceEventRepository'
import { ServiceEventProvider } from './features/schedule/ServiceEventProvider'
```

Construct one repository instance and provide it above the outlet:

```tsx
export function App() {
  const [eventRepository] = useState(
    () => new InMemoryServiceEventRepository(),
  )

  return (
    <ServiceEventProvider repository={eventRepository}>
      <div className="application-shell">
        {/* Existing application shell and route outlet */}
      </div>
    </ServiceEventProvider>
  )
}
```

`App` is now the composition root for service-event storage. Part 8 can select another implementation here without changing route pages.

The lazy `useState` initializer creates one repository for the lifetime of this mounted application shell. Constructing it directly during every render would discard created records and subscriptions whenever `App` re-rendered.

## Make Each Page Depend on the Right Hook

The schedule needs a reactive list. Change its store read to:

```tsx
const storedEvents = useServiceEvents()
```

Its existing loading, error, and ready overrides remain presentation state. They do not belong in the repository because they describe one page's current UI rather than stored event records.

The create page needs commands but does not render the collection. Use the repository hook:

```tsx
const repository = useServiceEventRepository()

function createServiceEvent(command: CreateServiceEventCommand) {
  const event = repository.create(command)
  void navigate(`/schedule/${event.id}`)
}
```

The detail page also depends on the lookup contract:

```tsx
const { eventId } = useParams<'eventId'>()
const repository = useServiceEventRepository()
const event = eventId ? repository.findById(eventId) : undefined
```

These pages no longer know that the current implementation stores an array in memory. They only know the use cases exposed by `ServiceEventRepository`.

The detail page does not subscribe because Part 7 has no update operation that can change the current event while it remains mounted. When update or asynchronous loading arrives, the read model can deliberately evolve rather than adding unused reactivity now.

## Update Component Test Composition

Tests that render `SchedulePage` must provide a repository explicitly:

```tsx
function renderSchedule(
  initialState?: ComponentProps<typeof SchedulePage>['initialState'],
) {
  const repository = new InMemoryServiceEventRepository()
  render(
    <MemoryRouter>
      <ServiceEventProvider repository={repository}>
        <SchedulePage initialState={initialState} />
      </ServiceEventProvider>
    </MemoryRouter>,
  )
}
```

The test now mirrors production dependency composition while receiving a fresh repository for every case. It does not share records across tests or reach into module-level state.

The route tests need no manual changes. They render the real `App` route, and `App` composes a fresh in-memory repository for each rendered router.

## Test the Concrete Repository in Isolation

Create `src/features/schedule/inMemoryServiceEventRepository.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'
import { InMemoryServiceEventRepository } from './inMemoryServiceEventRepository'

describe('InMemoryServiceEventRepository', () => {
  it('finds a seeded event and represents a missing record', () => {
    const repository = new InMemoryServiceEventRepository()

    expect(repository.findById('sunday-2026-11-01')?.title).toBe(
      'Sunday Morning Service',
    )
    expect(repository.findById('missing')).toBeUndefined()
  })

  it('creates an event with an immutable snapshot and notifies subscribers', () => {
    let nextId = 0
    const repository = new InMemoryServiceEventRepository(
      [],
      () => `event-${++nextId}`,
    )
    const previousEvents = repository.list()
    const listener = vi.fn()
    const unsubscribe = repository.subscribe(listener)

    const created = repository.create({
      title: 'Prayer Night',
      startsAt: '2026-11-06T19:00:00.000Z',
      endsAt: '2026-11-06T20:00:00.000Z',
      location: 'Chapel',
    })

    expect(repository.list()).not.toBe(previousEvents)
    expect(repository.findById(created.id)).toEqual(created)
    expect(created).toEqual(
      expect.objectContaining({
        id: 'event-1',
        requiredPositions: 0,
        assignedPositions: 0,
      }),
    )
    expect(listener).toHaveBeenCalledOnce()

    unsubscribe()
    repository.create({
      title: 'Second Event',
      startsAt: '2026-11-07T19:00:00.000Z',
      endsAt: '2026-11-07T20:00:00.000Z',
      location: 'Chapel',
    })
    expect(listener).toHaveBeenCalledOnce()
  })
})
```

These are repository tests, so they instantiate the concrete class directly without React rendering. The first case protects lookup and explicit absence. The second protects immutable snapshot replacement, generated identity, staffing defaults, subscriber notification, and cleanup.

The application route tests still protect the integration among context, `useSyncExternalStore`, route pages, and navigation. Repository tests do not replace user-visible behavior tests; each level owns a different contract.

## Remove the Part 6 Concrete Store

Delete these files after all imports move to the new boundary:

```text
src/features/schedule/serviceEventStore.tsx
src/features/schedule/serviceEventStore.test.tsx
```

Leaving both approaches would create two competing sources of truth and make future persistence work ambiguous.

## Update the Project Notes

Add Part 7 to `README.md`:

```md
## Part 7

The `part_7` branch puts service-event operations behind a repository contract,
selects an in-memory implementation at the application boundary, bridges its
immutable snapshots into React, and tests the concrete repository in isolation.
```

## Run the Part 7 Quality Gate

Run every repository check:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed Part 7 checkpoint retains six test files and thirty-one passing tests:

```text
Test Files  6 passed (6)
Tests       31 passed (31)
```

The unchanged count is meaningful: two concrete-store tests were replaced by two repository tests, while the existing route and component suites prove that the refactoring preserved behavior.

The browser should look and behave exactly like Part 6. Open the schedule, create an event, follow its generated detail route, and verify seeded and missing event URLs.

## Production Boundaries

The repository contract is replaceable, but it remains synchronous. That matches the current in-memory implementation. Do not disguise future network latency by returning already-resolved promises today; Part 9 should change the contract deliberately when loading, cancellation, failures, and concurrency become real concerns.

Part 8 can add browser persistence behind the same synchronous interface because Web Storage is synchronous. That implementation must validate parsed JSON, version its schema, handle unavailable or full storage, and preserve immutable snapshots.

Other boundaries remain important:

- The repository owns records and record operations, not route parameters or presentation state.
- The UI must not mutate arrays returned by `list`.
- IDs still need server authority once remote persistence exists.
- Context selects and distributes the dependency; it is not the repository itself.
- `useSyncExternalStore` is for observing this external mutable source, not a reason to move ordinary local form state outside React.

React's official documentation describes [subscribing to external stores with `useSyncExternalStore`](https://react.dev/reference/react/useSyncExternalStore), [creating and providing context](https://react.dev/reference/react/createContext), and [separating context wiring from consumers](https://react.dev/learn/scaling-up-with-reducer-and-context).

## Part 7 Checkpoint

ChurchServe has the same visible routes and workflows, but route components no longer depend on one storage implementation. The repository contract describes current use cases and React subscription guarantees, the in-memory class owns records, context distributes the contract, and `App` selects the implementation.

Confirm that:

- Route pages import repository hooks rather than the in-memory class.
- `list` returns a stable snapshot until a mutation replaces it.
- `create` appends immutably before notifying subscribers.
- Unsubscription prevents later notifications.
- `useSyncExternalStore` bridges repository snapshots into schedule rendering.
- The composition root creates one repository per mounted application.
- The obsolete Part 6 concrete-store files are removed.
- All thirty-one tests and the production build pass.

Part 8 will add versioned browser persistence by implementing this repository contract with local storage and changing composition rather than route pages.
