---
title: "ChurchServe React Tutorial Part 2: Components, Props, and Callback Events"
excerpt: "Model church service events and build reusable React cards with typed props, callback events, clear state ownership, responsive styling, and behavior tests."
category: "React Tutorial"

seo:
  focusKeyword: "React props and callback events tutorial"
  description: "Build reusable React service-event cards with TypeScript props, callback events, parent-owned state, responsive CSS, and behavior tests."
  socialTitle: "ChurchServe Part 2: React Components and Props"
  socialDescription: "Turn the ChurchServe schedule into reusable, tested React components with explicit data and event ownership."
---

# ChurchServe React Tutorial Part 2: Components, Props, and Callback Events

[Part 1](https://dev.jun-kim.net/2026/10/28/churchserve-react-tutorial-part-1-create-the-application-shell/) gave ChurchServe a strict React and TypeScript workspace, shared application shell, and client-side routes. The `/schedule` route still shows placeholder copy. In this part, we will replace it with a useful schedule page built from typed data and reusable service-event cards.

Every new or changed file required for the Part 2 result is included below. Start from the completed Part 1 application on the `part_2` branch.

> **Quick answer:** Pass each immutable event into a focused card through typed props, pass a callback prop for user intent, and keep collection and selection state in the schedule page. Render cards with stable event IDs, then test the card contract and the page-level interaction through the DOM.

## What You Will Build

The completed schedule route displays two service-event cards:

- Sunday Morning Service has three open positions.
- Worship Team Rehearsal is fully staffed.
- Each card receives one event through props.
- Selecting a card calls the parent with only the event ID.
- The schedule page owns the selected-event state and displays a status message.

The new feature files are organized together:

```text
src/features/schedule/
├── SchedulePage.test.tsx
├── SchedulePage.tsx
├── ServiceEventCard.test.tsx
├── ServiceEventCard.tsx
├── schedule.css
└── serviceEvent.ts
```

This feature folder keeps the model, components, tests, and styles near the product behavior they support. Part 2 does not introduce an API or global state library; local data keeps the component boundary visible before later parts add persistence.

## Understand the React Communication Model

React component communication in this feature follows one direction at a time:

```text
SchedulePage
  ├── event prop ────────────────> ServiceEventCard
  └── onSelect callback <──────── ServiceEventCard button
```

Props are read-only values supplied by a parent. A function can also be a prop. The card calls that function when the user selects an event, but the page decides what selection means.

React does not have a separate custom-output declaration for function components. A callback such as `onSelect` is an ordinary prop with a function type. Naming it after user intent keeps the contract independent from the current button implementation.

For a deeper discussion of boundaries and ownership, see [React Component Composition](https://dev.jun-kim.net/2026/10/28/react-component-composition-build-features-from-focused-parts/).

## Model a Service Event

Create `src/features/schedule/serviceEvent.ts`:

```ts
export type ServiceEvent = {
  readonly id: string
  readonly title: string
  readonly startsAt: string
  readonly endsAt: string
  readonly location: string
  readonly requiredPositions: number
  readonly assignedPositions: number
}
```

The shared type gives the page and card one contract. Its fields are `readonly` because the card should not mutate an event owned by its parent.

The timestamps remain ISO strings rather than preformatted labels. Transport and storage formats should stay independent from presentation. The card can choose a locale and time zone when it renders the value.

The two position counts create the first useful business signal:

```text
open positions = required positions - assigned positions
```

These summary counts keep Part 2 focused on component communication. A later part can introduce position and assignment records without changing the card's public contract immediately.

## Create the Service-Event Card

Create `src/features/schedule/ServiceEventCard.tsx`:

```tsx
import type { ServiceEvent } from './serviceEvent'

type ServiceEventCardProps = {
  event: ServiceEvent
  onSelect: (eventId: string) => void
}

const serviceDateFormatter = new Intl.DateTimeFormat('en-CA', {
  dateStyle: 'full',
  timeStyle: 'short',
  timeZone: 'America/Toronto',
})

export function ServiceEventCard({ event, onSelect }: ServiceEventCardProps) {
  const openPositions = Math.max(
    0,
    event.requiredPositions - event.assignedPositions,
  )

  return (
    <article className="event-card">
      <div className="event-copy">
        <p className="event-date">
          <time dateTime={event.startsAt}>
            {serviceDateFormatter.format(new Date(event.startsAt))}
          </time>
        </p>
        <h2>{event.title}</h2>
        <p className="event-meta">
          <span>{event.location}</span>
        </p>
      </div>

      {openPositions === 0 ? (
        <p className="staffing complete">Fully staffed</p>
      ) : (
        <p className="staffing open">
          {openPositions} open {openPositions === 1 ? 'position' : 'positions'}
        </p>
      )}

      <button
        type="button"
        className="event-action"
        onClick={() => onSelect(event.id)}
        aria-label={`View ${event.title}`}
      >
        View schedule
      </button>
    </article>
  )
}
```

The prop type describes the complete card boundary:

1. `event` supplies the data to render.
2. `onSelect` accepts the selected event ID and returns no value.
3. The card owns presentation and the click interaction, not page state.

The card calculates `openPositions` during rendering. It does not need state because the value is fully determined by the current props. Storing the same number in state would create another value that could disagree with the event.

`Math.max(0, ...)` prevents temporarily inconsistent data from displaying a negative number. This is a presentation safeguard, not domain validation. A future API must still reject or report invalid staffing data.

The native `<article>`, `<time>`, heading, and `<button>` elements provide useful semantics without recreating keyboard behavior. The button's accessible name includes the event title, so multiple cards do not expose several controls all named only “View schedule.”

## Test the Card Contract

Create `src/features/schedule/ServiceEventCard.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ServiceEventCard } from './ServiceEventCard'
import type { ServiceEvent } from './serviceEvent'

const event: ServiceEvent = {
  id: 'event-1',
  title: 'Sunday Morning Service',
  startsAt: '2026-11-01T10:00:00-05:00',
  endsAt: '2026-11-01T11:30:00-05:00',
  location: 'Main Sanctuary',
  requiredPositions: 10,
  assignedPositions: 7,
}

describe('ServiceEventCard', () => {
  it('renders the event and open-position count', () => {
    render(<ServiceEventCard event={event} onSelect={() => undefined} />)

    expect(
      screen.getByRole('heading', { name: 'Sunday Morning Service' }),
    ).toBeVisible()
    expect(screen.getByText('Main Sanctuary')).toBeVisible()
    expect(screen.getByText('3 open positions')).toBeVisible()
  })

  it('reports the event ID when selected', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<ServiceEventCard event={event} onSelect={onSelect} />)

    await user.click(
      screen.getByRole('button', { name: 'View Sunday Morning Service' }),
    )

    expect(onSelect).toHaveBeenCalledOnce()
    expect(onSelect).toHaveBeenCalledWith('event-1')
  })

  it('shows when an event is fully staffed', () => {
    render(
      <ServiceEventCard
        event={{ ...event, assignedPositions: event.requiredPositions }}
        onSelect={() => undefined}
      />,
    )

    expect(screen.getByText('Fully staffed')).toBeVisible()
    expect(screen.queryByText(/open positions/)).not.toBeInTheDocument()
  })
})
```

These tests protect the public contract: the card displays supplied data, derives the staffing state, and reports the selected ID. They do not inspect local variables or call an internal handler directly.

`userEvent.setup()` simulates a complete user interaction rather than dispatching only one low-level click event. The test finds the button by its accessible role and name, just as a user or assistive technology identifies it.

The test deliberately avoids asserting the formatted date string. Locale data can vary across JavaScript runtimes, while the title, location, and staffing count are stable business output.

## Make the Schedule Page the State Owner

Create `src/features/schedule/SchedulePage.tsx`:

```tsx
import { useState } from 'react'
import { ServiceEventCard } from './ServiceEventCard'
import type { ServiceEvent } from './serviceEvent'
import './schedule.css'

const events: readonly ServiceEvent[] = [
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

export function SchedulePage() {
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null)
  const selectedEvent = events.find((event) => event.id === selectedEventId)

  return (
    <section className="schedule-page" aria-labelledby="schedule-heading">
      <p className="eyebrow">Schedule</p>
      <h1 id="schedule-heading">Upcoming services</h1>
      <p className="schedule-introduction">
        Review each event&apos;s time, location, and current volunteer coverage.
        Detailed assignments arrive in later parts of the tutorial.
      </p>

      <ul className="event-grid">
        {events.map((event) => (
          <li key={event.id}>
            <ServiceEventCard event={event} onSelect={setSelectedEventId} />
          </li>
        ))}
      </ul>

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

The page owns the event collection and the selected ID because it coordinates all cards and decides what selection changes. The card does not know about sibling cards or the confirmation message.

`selectedEvent` is derived from `selectedEventId` and `events` during rendering. It is not another state variable. This preserves one source of truth for the selection.

The `events.map(...)` call renders one card for each event. `key={event.id}` gives React stable identity when items are later inserted, removed, or reordered. The `key` is for React itself; the card receives the ID through its `event` prop.

The callback can be passed directly because the state setter accepts a string, matching `onSelect` exactly:

```tsx
<ServiceEventCard event={event} onSelect={setSelectedEventId} />
```

The selection result uses `role="status"`. Assistive technology can announce the new status without moving keyboard focus away from the button that caused it.

## Style the Schedule Feature

Create `src/features/schedule/schedule.css`:

```css
.schedule-page {
  max-width: 76rem;
  margin: 0 auto;
  padding: clamp(3.5rem, 8vw, 6rem) clamp(1.25rem, 5vw, 4rem);
}

.schedule-page h1 {
  margin: 0;
  color: #102a43;
  font-size: clamp(2.4rem, 6vw, 4.25rem);
  letter-spacing: -0.045em;
  line-height: 1;
}

.schedule-introduction {
  max-width: 44rem;
  margin: 1.25rem 0 0;
  color: #52667a;
  font-size: 1.1rem;
  line-height: 1.7;
}

.event-grid {
  display: grid;
  margin: 2.5rem 0 0;
  padding: 0;
  gap: 1rem;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
  list-style: none;
}

.event-grid > li {
  min-width: 0;
}

.event-card {
  display: grid;
  height: 100%;
  padding: 1.5rem;
  border: 1px solid #d9e2ec;
  border-radius: 1.1rem;
  background: #fff;
  box-shadow: 0 0.9rem 2.5rem rgb(16 42 67 / 7%);
  gap: 1.4rem;
  grid-template-rows: 1fr auto auto;
}

.event-date {
  margin: 0 0 0.55rem;
  color: #087f5b;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

.event-card h2 {
  margin: 0;
  color: #102a43;
  font-size: 1.35rem;
  line-height: 1.25;
}

.event-meta {
  margin: 0.8rem 0 0;
  color: #627d98;
  line-height: 1.5;
}

.staffing {
  width: fit-content;
  margin: 0;
  padding: 0.45rem 0.7rem;
  border-radius: 999px;
  font-size: 0.85rem;
  font-weight: 800;
}

.staffing.open {
  background: #fff3bf;
  color: #8f5b00;
}

.staffing.complete {
  background: #d3f9d8;
  color: #146c2e;
}

.event-action {
  width: 100%;
  padding: 0.75rem 1rem;
  border: 2px solid #087f5b;
  border-radius: 0.65rem;
  background: #fff;
  color: #087f5b;
  cursor: pointer;
  font: inherit;
  font-weight: 800;
}

.event-action:hover {
  background: #e6fcf5;
}

.event-action:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}

.selection-status {
  margin: 1.25rem 0 0;
  padding: 0.9rem 1rem;
  border-left: 4px solid #087f5b;
  background: #e6fcf5;
  color: #245448;
  line-height: 1.5;
}
```

The `auto-fit` grid places cards side by side when space allows and collapses to one column on a narrow screen. `min(100%, 18rem)` prevents the minimum width from forcing horizontal scrolling on a viewport narrower than 18 rem.

The card's three-row grid lets its heading area grow without moving the badge and button into inconsistent positions. Open and complete staffing states use both text and color, so their meaning does not depend on color perception alone.

## Test Page-Level Ownership

Create `src/features/schedule/SchedulePage.test.tsx`:

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
})
```

The card tests prove that the child reports an ID. This page test proves that the parent receives it and translates it into visible state. Together they protect the boundary without testing private implementation details.

## Replace the Schedule Placeholder Route

Import the real page in `src/router.tsx`:

```tsx
import { SchedulePage } from './features/schedule/SchedulePage'
```

Replace the existing `schedule` child route with:

```tsx
{
  path: 'schedule',
  Component: SchedulePage,
},
```

The route now owns only the URL-to-page mapping. The schedule page owns feature state, and the card owns focused presentation.

## Update the Existing Navigation Test

Part 1 expected the placeholder heading after navigation. In `src/App.test.tsx`, update that assertion to the real page heading:

```tsx
expect(
  await screen.findByRole('heading', {
    level: 1,
    name: 'Upcoming services',
  }),
).toBeVisible()
```

The rest of the test remains unchanged. It still verifies that navigating to the schedule replaces only the child route while preserving the application shell.

## Run the Part 2 Quality Gate

Run each repository check:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed Part 2 checkpoint has three test files and seven passing tests:

```text
Test Files  3 passed (3)
Tests       7 passed (7)
```

Start the development server for a browser check:

```bash
npm run dev
```

Open the local URL printed by Vite, normally `http://localhost:5173`, and select **Schedule**. If that port is occupied, Vite prints the alternate port it selected.

## Production Boundaries

Part 2 uses a module-level array intentionally, but production ownership will be different:

- An API will validate event timestamps and staffing counts.
- Server authorization will determine who may view or change schedule data.
- A data-loading boundary will own loading, error, and cancellation behavior.
- Dates will follow the organization's configured time zone rather than a hard-coded tutorial value.
- Selection may become navigation to a durable event URL.

The component contract can survive those changes. The card still needs an event and an `onSelect` callback; the page can replace local data without turning the card into an API client.

React's official documentation provides deeper background on [passing props](https://react.dev/learn/passing-props-to-a-component), [responding to events](https://react.dev/learn/responding-to-events), [sharing state](https://react.dev/learn/sharing-state-between-components), and [rendering lists with stable keys](https://react.dev/learn/rendering-lists).

## Part 2 Checkpoint

ChurchServe now renders typed service-event data through a reusable, controlled component. The schedule page owns the collection and selection state, while each card owns only presentation and the user interaction that reports an event ID.

Open `/schedule` and confirm that:

- Two responsive event cards are visible.
- Sunday Morning Service shows three open positions.
- Worship Team Rehearsal shows “Fully staffed.”
- Each button has a visible keyboard focus indicator.
- Selecting a card displays a status message naming the event.
- All seven tests and the production build pass.

Part 3 will build on this feature by modeling loading, error, empty, and populated schedule states with ordinary React conditional rendering.
