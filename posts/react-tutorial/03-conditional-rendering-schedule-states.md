---
title: "ChurchServe React Tutorial Part 3: Conditional Rendering and Schedule States"
excerpt: "Model loading, error, empty, and populated schedules with a TypeScript discriminated union, accessible React conditional rendering, retry behavior, and focused tests."
category: "React Tutorial"

seo:
  focusKeyword: "React conditional rendering tutorial"
  description: "Use React conditional rendering and a TypeScript discriminated union to build loading, error, empty, and populated schedule states with tests."
  socialTitle: "ChurchServe Part 3: React Conditional Rendering"
  socialDescription: "Build predictable loading, error, empty, and populated schedule views with React, TypeScript, accessible HTML, and behavior tests."
---

# ChurchServe React Tutorial Part 3: Conditional Rendering and Schedule States

[Part 2](https://dev.jun-kim.net/2026/10/30/churchserve-react-tutorial-part-2-components-props-and-callback-events/) built reusable service-event cards and made the schedule page responsible for the event collection and selected event. The page still assumes that usable data is always available.

Real data does not arrive in one step. A request can be in progress, fail, return no events, or return a populated collection. This part models those possibilities explicitly and renders one matching experience at a time.

Start from the completed Part 2 application on the `part_3` branch.

> **Quick answer:** Represent mutually exclusive request outcomes with a TypeScript discriminated union instead of independent booleans. Narrow that union with `state.status`, render a distinct loading, error, empty, or populated branch, reset dependent selection state when retrying, and test the DOM behavior users can observe.

## What You Will Build

The `/schedule` route will support four user-visible outcomes:

- A loading message while the schedule is unavailable.
- An error message with a retry button.
- An empty result after a successful request.
- The existing event-card grid when events are available.

Part 3 adds one source file and updates the page, page tests, feature styles, and project notes:

```text
src/features/schedule/
├── SchedulePage.test.tsx
├── SchedulePage.tsx
├── ServiceEventCard.test.tsx
├── ServiceEventCard.tsx
├── schedule.css
├── scheduleState.ts
└── serviceEvent.ts
```

The tutorial still uses seeded local data. The state boundary is useful now because a later API can replace the simulated outcomes without forcing the card component to understand network requests.

## Avoid Ambiguous Boolean State

A first attempt might add several independent values:

```tsx
const [isLoading, setIsLoading] = useState(false)
const [hasError, setHasError] = useState(false)
const [events, setEvents] = useState<ServiceEvent[]>([])
```

Those values allow contradictory combinations. The page could be loading and failing simultaneously, or it could have events while still displaying an error. An empty array is also ambiguous: it may mean that a request has not completed or that the server successfully returned no events.

The business states are mutually exclusive, so the type should express that invariant.

Create `src/features/schedule/scheduleState.ts`:

```ts
import type { ServiceEvent } from './serviceEvent'

export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; events: readonly ServiceEvent[] }
```

`status` is the discriminant. Once code proves that `status === 'error'`, TypeScript knows that `message` exists. Once it proves that `status === 'ready'`, TypeScript makes `events` available.

The type does not permit an error state with stale events or a loading state with an error message. That does not eliminate every possible bug, but it prevents a useful class of invalid combinations before the application runs.

The empty state is not a separate request outcome. It is a successful `ready` state whose collection has zero elements. This distinction matters because “no upcoming services” is valid data, not a transport failure.

## Replace the Schedule Page

Update `src/features/schedule/SchedulePage.tsx`:

```tsx
import { useState } from 'react'
import { ServiceEventCard } from './ServiceEventCard'
import type { ServiceEvent } from './serviceEvent'
import type { ScheduleState } from './scheduleState'
import './schedule.css'

const scheduledEvents: readonly ServiceEvent[] = [
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

type SchedulePageProps = {
  initialState?: ScheduleState
}

const defaultState: ScheduleState = {
  status: 'ready',
  events: scheduledEvents,
}

export function SchedulePage({
  initialState = defaultState,
}: SchedulePageProps) {
  const [state, setState] = useState<ScheduleState>(initialState)
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null)
  const events = state.status === 'ready' ? state.events : []
  const selectedEvent = events.find((event) => event.id === selectedEventId)

  function reloadSchedule() {
    setSelectedEventId(null)
    setState(defaultState)
  }

  return (
    <section className="schedule-page" aria-labelledby="schedule-heading">
      <p className="eyebrow">Schedule</p>
      <h1 id="schedule-heading">Upcoming services</h1>
      <p className="schedule-introduction">
        Review each event&apos;s time, location, and current volunteer coverage.
        Detailed assignments arrive in later parts of the tutorial.
      </p>

      {state.status === 'loading' && (
        <p className="state-panel" role="status">
          Loading service schedule…
        </p>
      )}

      {state.status === 'error' && (
        <section className="state-panel error-state" role="alert">
          <h2>We could not load the schedule</h2>
          <p>{state.message}</p>
          <button
            className="retry-action"
            type="button"
            onClick={reloadSchedule}
          >
            Try again
          </button>
        </section>
      )}

      {state.status === 'ready' && events.length === 0 && (
        <section className="state-panel empty-state">
          <h2>No upcoming services</h2>
          <p>New service events will appear here when they are scheduled.</p>
        </section>
      )}

      {state.status === 'ready' && events.length > 0 && (
        <ul className="event-grid">
          {events.map((event) => (
            <li key={event.id}>
              <ServiceEventCard event={event} onSelect={setSelectedEventId} />
            </li>
          ))}
        </ul>
      )}

      {selectedEvent && (
        <p className="selection-status" role="status">
          <strong>{selectedEvent.title}</strong> selected. Detailed scheduling
          will be added in a later part.
        </p>
      )}
    </section>
  )
}
```

The page keeps `ScheduleState` and `selectedEventId` separate because they represent different concerns. The first describes the collection request; the second describes an interaction within the currently visible collection.

The `events` expression narrows the union before code searches or maps the collection:

```tsx
const events = state.status === 'ready' ? state.events : []
```

The fallback array lets the selection lookup remain straightforward. It also means that `selectedEvent` becomes `undefined` whenever the page is not ready, so the old selection message cannot appear beside a loading or error state.

`reloadSchedule` clears the selected ID as well as replacing the request state. This tutorial retry completes immediately with seeded events. A production retry would normally set `loading`, call a data client, then transition to either `ready` or `error`. Clearing dependent state at the boundary prevents an event selected from an earlier response from leaking into the next result.

The optional `initialState` prop is a narrow test seam. The router can continue rendering `<SchedulePage />`, while a test can start the page in each valid state without mocking React internals or introducing timing delays.

## Render One State at a Time

React uses ordinary JavaScript expressions for conditional rendering. Each branch checks the discriminant plus, for a ready response, the collection length.

The four conditions cannot overlap:

| State | Additional condition | Visible result |
| --- | --- | --- |
| `loading` | None | Progress message |
| `error` | None | Error details and retry button |
| `ready` | `events.length === 0` | Empty-state guidance |
| `ready` | `events.length > 0` | Event-card list |

This structure is deliberately explicit. A compact nested conditional may save lines, but named checks make the state-to-interface mapping easier to review and extend.

The loading message uses `role="status"`, a polite live region suitable for a progress update. The error container uses `role="alert"` because the failure needs prompt attention. Do not add `role="alert"` to ordinary empty content; an empty schedule is a successful result, not an urgent error.

The retry control remains a native `<button>`. It receives keyboard interaction, focus behavior, and button semantics without custom event handling.

## Preserve Stable List Identity

The populated branch retains the event ID as its React key:

```tsx
{events.map((event) => (
  <li key={event.id}>
    <ServiceEventCard event={event} onSelect={setSelectedEventId} />
  </li>
))}
```

Keys identify siblings across renders. A stable domain ID lets React match the same event when the collection is inserted, removed, or reordered. An array index describes the current position instead, so it can associate component state or DOM state with the wrong event after the order changes.

The `key` does not become a component prop. `ServiceEventCard` already receives the event, including its ID, through the explicit `event` prop.

## Add State Styles

Add these rules after `.schedule-introduction` in `src/features/schedule/schedule.css`:

```css
.state-panel {
  margin: 2.5rem 0 0;
  padding: 1.4rem;
  border: 1px solid #bcccdc;
  border-radius: 0.9rem;
  background: #f0f4f8;
  color: #334e68;
  line-height: 1.6;
}

.state-panel h2,
.state-panel p {
  margin: 0;
}

.state-panel p + .retry-action {
  margin-top: 1rem;
}

.error-state {
  border-color: #ffa8a8;
  background: #fff5f5;
  color: #862e2e;
}

.retry-action {
  padding: 0.7rem 1rem;
  border: 0;
  border-radius: 0.65rem;
  background: #862e2e;
  color: #fff;
  cursor: pointer;
  font: inherit;
  font-weight: 800;
}

.retry-action:hover {
  background: #6f2424;
}

.retry-action:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}

.empty-state {
  border-style: dashed;
  text-align: center;
}

.empty-state p {
  margin-top: 0.35rem;
}
```

The neutral panel provides a shared visual boundary, while the error state changes both color and content. The failure is still understandable without color because it has a specific heading, message, and retry action.

The focus rule remains visible against the dark retry button. Hover styling is supplementary; keyboard users do not need a mouse-only cue to find the active control.

## Test Every User-Visible Branch

Replace `src/features/schedule/SchedulePage.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { SchedulePage } from './SchedulePage'

describe('SchedulePage', () => {
  it('renders the event collection and owns card selection', async () => {
    const user = userEvent.setup()
    render(<SchedulePage />)

    expect(screen.getAllByRole('article')).toHaveLength(2)

    await user.click(
      screen.getByRole('button', { name: 'View Sunday Morning Service' }),
    )

    expect(screen.getByRole('status')).toHaveTextContent(
      'Sunday Morning Service selected',
    )
  })

  it('shows a loading message without rendering an empty state', () => {
    render(<SchedulePage initialState={{ status: 'loading' }} />)

    expect(screen.getByRole('status')).toHaveTextContent(
      'Loading the service schedule',
    )
    expect(screen.queryByText('No upcoming services')).not.toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('shows an error and reloads the seeded schedule', async () => {
    const user = userEvent.setup()
    render(
      <SchedulePage
        initialState={{
          status: 'error',
          message: 'The scheduling service did not respond.',
        }}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The scheduling service did not respond.',
    )

    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getAllByRole('article')).toHaveLength(2)
  })

  it('shows an empty state only after an empty schedule is ready', () => {
    render(<SchedulePage initialState={{ status: 'ready', events: [] }} />)

    expect(
      screen.getByRole('heading', { name: 'No upcoming services' }),
    ).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
```

These tests assert what a user can observe:

- Loading content does not masquerade as an empty result.
- An error exposes its message through an alert.
- Retry removes the error and restores the populated schedule.
- A successful empty response has neither a loading status nor an error alert.
- The existing card selection behavior still works.

`queryBy...` is appropriate when proving that an element is absent because it returns `null` instead of throwing. `getBy...` is appropriate when the element must already exist. The test does not inspect `state.status` directly; it verifies the interface produced by that state.

## Update the Project Notes

Add a Part 3 entry to `README.md`:

```md
## Part 3

The `part_3` branch models loading, error, and ready schedule states with a
TypeScript discriminated union. The schedule page renders accessible loading,
retry, empty, and populated experiences, with behavior-focused tests for each
user-visible branch.
```

The README records what is complete at this checkpoint without duplicating the tutorial implementation.

## Run the Part 3 Quality Gate

Run the full project checks:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed checkpoint has three test files and ten passing tests:

```text
Test Files  3 passed (3)
Tests       10 passed (10)
```

Start the development server for a browser check:

```bash
npm run dev
```

Open the URL printed by Vite, normally `http://localhost:5173`, and navigate to `/schedule`. The default tutorial route displays the populated result. The other outcomes are supplied through `initialState` in tests; Part 4 and later data-loading work can replace that seam with application behavior.

## Production Boundaries

Part 3 models request outcomes but does not make a network request. In production, keep several responsibilities outside the rendering branches:

- A data client should own HTTP details, cancellation, response parsing, and authentication headers.
- Runtime validation should check external JSON before the application treats it as `ServiceEvent` data. A TypeScript type does not validate a server response.
- Retry should start a new request and handle rapid repeated attempts, cancellation, and component unmounting.
- The server must enforce authorization and domain rules; hiding a button in React is not access control.
- Operational logging should record failures without displaying sensitive server details to users.
- If old data remains visible during refresh, model that intentionally instead of overloading the initial `loading` state.

The discriminated union can evolve with those requirements. For example, a later design might distinguish an initial load from background refresh or carry a retryable error code. Add states because the user experience differs, not merely because an implementation has another boolean.

For additional background, see React's official guides to [conditional rendering](https://react.dev/learn/conditional-rendering) and [rendering lists](https://react.dev/learn/rendering-lists), TypeScript's documentation on [discriminated unions and narrowing](https://www.typescriptlang.org/docs/handbook/2/narrowing.html#discriminated-unions), and Testing Library's guidance on [queries](https://testing-library.com/docs/queries/about/).

## Part 3 Checkpoint

ChurchServe now represents the schedule request as a small state machine rather than a collection plus ambiguous flags. The page renders one accessible outcome at a time, preserves stable event identity, resets dependent selection during retry, and proves each visible branch through behavior tests.

Confirm that:

- `ScheduleState` permits only loading, error, or ready data.
- The ready state distinguishes an empty collection from a populated one.
- Loading, error, empty, and event-card content do not overlap.
- Retry clears the error and restores the seeded events.
- The retry button has a visible keyboard focus indicator.
- All ten tests and the production build pass.

Part 4 will build on this state boundary by introducing volunteer assignment state and the first scheduling rules.
