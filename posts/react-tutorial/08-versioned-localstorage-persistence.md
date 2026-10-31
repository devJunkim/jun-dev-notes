---
title: "ChurchServe React Tutorial Part 8: Versioned localStorage Persistence"
excerpt: "Persist React service events across browser sessions with a versioned localStorage repository, runtime validation, immutable snapshots, and graceful failure handling."
category: "React Tutorial"

seo:
  focusKeyword: "React localStorage repository TypeScript"
  description: "Build a versioned localStorage repository in React and TypeScript with runtime validation, immutable snapshots, recovery, and focused tests."
  socialTitle: "React Tutorial: Versioned localStorage Persistence"
  socialDescription: "Persist React data safely with a typed repository, versioned JSON, runtime validation, immutable updates, and Web Storage failure handling."
---

# ChurchServe React Tutorial Part 8: Versioned localStorage Persistence

[Part 7](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-7-repository-interfaces-and-external-stores/) placed service-event operations behind a repository interface. The application still selected an in-memory implementation, so a newly created event disappeared after a page reload.

Part 8 adds browser persistence without changing schedule, creation, or detail pages. A new repository stores a versioned JSON document in `localStorage`, validates every value it restores, preserves stable immutable snapshots for React, and keeps the current session usable when browser storage is blocked or full.

> **Quick answer:** Treat browser storage as an untrusted external boundary. Store a versioned envelope, parse it into `unknown`, validate the complete runtime shape, and expose only validated records through the existing repository contract. Catch both storage access and write failures, then degrade to in-memory behavior rather than crashing the React tree.

## What Part 8 changes

This checkpoint adds or updates these files:

```text
src/
  App.tsx
  App.test.tsx
  features/schedule/
    inMemoryServiceEventRepository.ts
    localStorageServiceEventRepository.ts
    localStorageServiceEventRepository.test.ts
    seededServiceEvents.ts
README.md
```

The visible routes and form behavior remain unchanged. That is the benefit of the Part 7 boundary: both concrete repositories satisfy the same `ServiceEventRepository` contract.

## Understand the browser-storage boundary

`localStorage` persists string key-value pairs for an origin across browser sessions. It is synchronous, so reads and writes block the browser's JavaScript execution while they run. That makes it acceptable for this small tutorial snapshot, but not a general database replacement.

Several failure conditions matter in real applications:

- A user or browser policy can deny storage access.
- A write can exceed the origin's quota.
- Existing JSON can be malformed, manually edited, or left by older application code.
- TypeScript types disappear at runtime and cannot prove that parsed JSON is valid.
- Browser storage is readable by JavaScript running on the same origin, so sensitive data does not belong here.

The HTML standard allows `setItem` to fail when storage is disabled or its quota is exceeded. Accessing `window.localStorage` can also throw under browser security policies. Catching only `JSON.parse` is therefore incomplete.

## Keep seed data independent of storage adapters

Both repository implementations need the initial tutorial records. Do not make the local-storage adapter import them from the in-memory adapter because that creates a dependency between two peers.

Move the records to `seededServiceEvents.ts`:

```typescript
import type { ServiceEvent } from './serviceEvent'

export const seededServiceEvents: readonly ServiceEvent[] = [
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
```

`InMemoryServiceEventRepository` now imports this value. The new adapter does the same. Seed data belongs to neither persistence mechanism.

## Define a versioned storage document

Create `localStorageServiceEventRepository.ts` and describe the browser API through the smallest interface the repository needs:

```typescript
type StoragePort = Pick<Storage, 'getItem' | 'setItem'>

type StoredServiceEvents = {
  readonly version: 1
  readonly events: readonly ServiceEvent[]
}

export const serviceEventStorageKey = 'churchserve.service-events'
```

The `StoragePort` makes the external dependency injectable. Tests can supply a deterministic fake without replacing global browser state.

The envelope has an explicit schema version. Adding a version does not perform migration by itself, but it gives future code a reliable discriminator. When Part 8 sees another version, it refuses to pretend that the document matches today's model.

Keep the storage key stable across versions. A future migration needs to find the old document before it can transform it.

## Validate parsed JSON at runtime

`JSON.parse` returns data, not proof. A type assertion such as `JSON.parse(value) as StoredServiceEvents` would silence the compiler while trusting every stored property.

Start with small runtime guards:

```typescript
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) >= 0
}
```

Then validate the stored event's complete shape and important invariants:

```typescript
function isServiceEvent(value: unknown): value is ServiceEvent {
  if (!isRecord(value)) {
    return false
  }

  const startsAt = Date.parse(String(value.startsAt))
  const endsAt = Date.parse(String(value.endsAt))

  return (
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.title) &&
    isNonEmptyString(value.startsAt) &&
    isNonEmptyString(value.endsAt) &&
    isNonEmptyString(value.location) &&
    !Number.isNaN(startsAt) &&
    !Number.isNaN(endsAt) &&
    endsAt > startsAt &&
    isNonNegativeInteger(value.requiredPositions) &&
    isNonNegativeInteger(value.assignedPositions) &&
    value.assignedPositions <= value.requiredPositions
  )
}
```

Validation covers more than JavaScript types. It rejects invalid dates, reversed time ranges, negative staffing counts, and an assigned count greater than the required count.

Parse the envelope from `unknown` and copy each accepted record:

```typescript
function parseStoredEvents(value: string): readonly ServiceEvent[] | undefined {
  const parsed: unknown = JSON.parse(value)

  if (
    !isRecord(parsed) ||
    parsed.version !== 1 ||
    !Array.isArray(parsed.events) ||
    !parsed.events.every(isServiceEvent)
  ) {
    return undefined
  }

  return parsed.events.map((event) => ({ ...event }))
}
```

Returning `undefined` distinguishes an unsupported or invalid shape from a valid empty event array. The copied array also becomes the repository's own snapshot instead of retaining the parsed container directly.

## Implement the persistent repository

The class still implements the Part 7 interface:

```typescript
const createEventId = () => crypto.randomUUID()
const ignorePersistenceError = () => undefined

export class LocalStorageServiceEventRepository
  implements ServiceEventRepository
{
  private events: readonly ServiceEvent[]
  private readonly listeners = new Set<ServiceEventListener>()
  private readonly eventIdFactory: () => string
  private readonly onPersistenceError: (error: unknown) => void
  private storage: StoragePort | undefined

  constructor(
    storageFactory: () => StoragePort = () => window.localStorage,
    initialEvents: readonly ServiceEvent[] = seededServiceEvents,
    eventIdFactory: () => string = createEventId,
    onPersistenceError: (error: unknown) => void = ignorePersistenceError,
  ) {
    this.events = [...initialEvents]
    this.eventIdFactory = eventIdFactory
    this.onPersistenceError = onPersistenceError
    this.restore(storageFactory)
  }

  readonly list = () => this.events

  readonly findById = (eventId: string) =>
    this.events.find((event) => event.id === eventId)

  readonly subscribe = (listener: ServiceEventListener) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
}
```

The dependency is a factory rather than a `Storage` value because evaluating `window.localStorage` can itself throw. The constructor can now handle that access inside its own recovery boundary.

The no-op error callback keeps this checkpoint's repository contract unchanged. A production application should connect persistence failures to diagnostics and user-visible messaging rather than silently assuming durability.

## Restore, repair, or fall back

Restoration has three paths:

1. A valid version 1 document replaces the seeded snapshot.
2. Missing, malformed, or invalid data uses the seeds and attempts to write a valid document.
3. Inaccessible storage disables persistence while leaving the in-memory snapshot available.

The implementation separates browser access from parsing:

```typescript
private restore(storageFactory: () => StoragePort) {
  let storedValue: string | null

  try {
    this.storage = storageFactory()
    storedValue = this.storage.getItem(serviceEventStorageKey)
  } catch (error) {
    this.disablePersistence(error)
    return
  }

  if (storedValue === null) {
    this.persist(this.events)
    return
  }

  try {
    const storedEvents = parseStoredEvents(storedValue)

    if (!storedEvents) {
      this.onPersistenceError(
        new Error('Stored service events do not match schema version 1.'),
      )
      this.persist(this.events)
      return
    }

    this.events = storedEvents
  } catch (error) {
    this.onPersistenceError(error)
    this.persist(this.events)
  }
}
```

Malformed JSON throws and reaches the second `catch`. Structurally invalid JSON returns `undefined`. Both cases preserve only validated seed data and attempt to repair the stored value.

An unsupported version follows the invalid-shape path. A real version 2 release could replace that fallback with an explicit `migrateVersion1ToVersion2` function and tests for every supported transition.

## Persist before publishing a new snapshot

Creation keeps the immutable-snapshot behavior required by `useSyncExternalStore`:

```typescript
readonly create = (command: CreateServiceEventCommand) => {
  const event: ServiceEvent = {
    id: this.eventIdFactory(),
    ...command,
    requiredPositions: 0,
    assignedPositions: 0,
  }
  const nextEvents = [...this.events, event]

  this.persist(nextEvents)
  this.events = nextEvents
  this.listeners.forEach((listener) => listener())
  return event
}
```

`nextEvents` is written before it becomes the current snapshot. The repository then replaces the array reference and notifies subscribers. React reads the new stable reference and renders the created event.

The write helper catches quota and policy failures:

```typescript
private persist(events: readonly ServiceEvent[]) {
  if (!this.storage) {
    return
  }

  const payload: StoredServiceEvents = { version: 1, events }

  try {
    this.storage.setItem(serviceEventStorageKey, JSON.stringify(payload))
  } catch (error) {
    this.disablePersistence(error)
  }
}

private disablePersistence(error: unknown) {
  this.storage = undefined
  this.onPersistenceError(error)
}
```

After a failure, the repository stops retrying the same unavailable storage object during the current session. Creation still updates memory and notifies React, so the interface remains usable. The trade-off is explicit: the new record is session-only, and a production UI must tell the user that it was not saved durably.

## Select persistence at the composition root

Route components still import only repository hooks. Change `App.tsx`, where Part 7 deliberately placed the implementation decision:

```typescript
import { useState } from 'react'
import { LocalStorageServiceEventRepository } from './features/schedule/localStorageServiceEventRepository'

export function App() {
  const [eventRepository] = useState(
    () => new LocalStorageServiceEventRepository(),
  )

  return (
    <ServiceEventProvider repository={eventRepository}>
      {/* application shell and Outlet */}
    </ServiceEventProvider>
  )
}
```

The lazy initializer keeps one repository instance for the mounted shell. React development checks can invoke an initializer more than once, so construction must be safe to repeat. Here, the second construction reads the same versioned snapshot; it does not append duplicate seed records.

No schedule route changed. Persistence is a composition choice rather than a component responsibility.

## Isolate browser state in route tests

The application tests render the real `App`, so they now use jsdom's `localStorage`. Clear it before each test:

```typescript
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

beforeEach(() => localStorage.clear())
afterEach(() => vi.restoreAllMocks())
```

Without this reset, a test that creates an event can affect a later test. Test order should not decide application state.

Focused repository tests inject a small fake storage object:

```typescript
function createStorage(storedValue: string | null = null) {
  let value = storedValue

  return {
    getItem: vi.fn(() => value),
    setItem: vi.fn((_key: string, nextValue: string) => {
      value = nextValue
    }),
    read: () => value,
  }
}
```

The suite verifies:

- restoration of a valid versioned snapshot;
- initial seeding and the exact persisted creation document;
- immutable snapshot replacement and subscriber notification;
- recovery from malformed JSON, unsupported versions, and invalid event data;
- continued in-memory behavior when storage access throws `SecurityError`;
- continued in-memory behavior when writes throw `QuotaExceededError`.

Test behavior and stored output instead of private fields. The adapter can be refactored without weakening its contract.

## Validate the Part 8 checkpoint

Run every repository check:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed checkpoint passes lint and strict TypeScript checking, runs 38 tests across 7 files, and produces the Vite production bundle.

## Production boundaries and trade-offs

This adapter demonstrates a reliable browser-storage boundary, but it is intentionally not the final ChurchServe data architecture.

- `localStorage` is synchronous. Large documents can block interaction, and every creation serializes the full array.
- Browser persistence is not shared, authoritative, or protected from local modification.
- Do not store credentials, access tokens, private volunteer information, or other sensitive records in this document.
- The current callback records persistence failure only when a caller supplies one. A production UI needs an explicit degraded-storage state.
- This checkpoint does not synchronize other tabs. The browser's `storage` event is delivered to other documents, not the document that made the write, and reconciliation needs its own conflict rules.
- Repairing an unknown schema with seeds is suitable for tutorial data. Production migrations should preserve compatible user data and quarantine documents that cannot be migrated safely.
- An ASP.NET Core API must eventually own authorization, validation, durable identifiers, concurrency, and business rules.

Part 9 should change the repository contract deliberately for an asynchronous HTTP boundary. Loading, request failure, cancellation, server validation, and concurrent updates should become explicit rather than being hidden behind promises that mimic synchronous storage.

## What Part 8 established

ChurchServe now preserves created service events across reloads while its route components remain independent of the storage mechanism.

- A versioned envelope makes stored schema evolution explicit.
- Runtime guards treat parsed JSON as untrusted data.
- Validated immutable snapshots remain compatible with `useSyncExternalStore`.
- Storage access and quota failures degrade to usable in-memory behavior.
- Injected storage and ID factories keep failure paths deterministic in tests.
- The application root selects persistence without rewriting feature pages.

That boundary is small enough for browser storage today and clear enough to replace with an asynchronous API contract in the next stage.
