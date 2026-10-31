---
title: "ChurchServe React Tutorial Part 6: Parameterized Routes and Event Details"
excerpt: "Add bookmarkable service-event URLs, a dedicated creation route, semantic card links, shared context state, explicit missing-record handling, and route tests."
category: "React Tutorial"

seo:
  focusKeyword: "React Router parameterized routes tutorial"
  description: "Build React Router parameterized routes, shared context state, service-event detail and creation pages, missing states, links, and route tests."
  socialTitle: "ChurchServe Part 6: React Router Detail Pages"
  socialDescription: "Turn the schedule into bookmarkable creation and detail routes with shared state, semantic links, missing states, and integration tests."
---

# ChurchServe React Tutorial Part 6: Parameterized Routes and Event Details

[Part 5](https://dev.jun-kim.net/2026/10/30/churchserve-react-tutorial-part-5-controlled-forms-and-validation/) can create an event, but the form and result live inside the schedule page. Selecting a card does not produce a durable address, and a coordinator cannot copy a link to one event.

Part 6 makes event identity part of the URL. `/schedule/new` owns creation, `/schedule/:eventId` displays one event, and the schedule cards become normal links. A focused in-memory context lets those sibling route pages share events without turning the application shell into an unstructured global store.

> **Quick answer:** Put durable record IDs in parameterized URLs, use `<Link>` for user-driven navigation, keep shared in-memory records above the route outlet, use `useNavigate` only after successful creation, and render a record-specific missing state when a valid route points to unknown data.

## What You Will Build

Part 6 adds five files and updates the shell, route table, schedule, cards, styles, tests, and README:

```text
src/features/schedule/
├── ServiceEventCreatePage.tsx
├── ServiceEventDetailPage.tsx
├── serviceEventContext.ts
├── serviceEventStore.test.tsx
├── serviceEventStore.tsx
└── existing schedule files
```

The route contract becomes:

| URL | Page responsibility |
| --- | --- |
| `/schedule` | List events and link to creation or details |
| `/schedule/new` | Host the Part 5 form and create an event |
| `/schedule/:eventId` | Resolve and display one event |
| `/anything-unknown` | Render the generic application 404 |

An unknown event ID differs from an unknown application URL. `/schedule/missing` matches a real route shape, so it should explain that the event is unavailable. `/does-not-exist` does not match a known feature route, so it should use the generic page-not-found experience.

## Put Shared Event State Above the Outlet

The schedule, creation page, and detail page are siblings in the route tree. State kept inside one page disappears when that page unmounts, so move the event collection into a provider rendered by the shared application layout.

Keep the context and consumer hook in `src/features/schedule/serviceEventContext.ts`:

```ts
import { createContext, useContext } from 'react'
import type { CreateServiceEventCommand } from './createServiceEvent'
import type { ServiceEvent } from './serviceEvent'

export type ServiceEventStoreValue = {
  readonly events: readonly ServiceEvent[]
  findById: (eventId: string) => ServiceEvent | undefined
  create: (command: CreateServiceEventCommand) => ServiceEvent
}

export const ServiceEventContext =
  createContext<ServiceEventStoreValue | null>(null)

export function useServiceEvents() {
  const store = useContext(ServiceEventContext)

  if (!store) {
    throw new Error('useServiceEvents must be used inside ServiceEventProvider.')
  }

  return store
}
```

The nullable default makes a missing provider detectable. The custom hook converts that configuration mistake into a direct error instead of allowing every consumer to handle `null` independently.

This is a focused context: it carries only service-event records and commands required by these routes. Form editing state, schedule loading overrides, navigation state, and volunteer assignments remain with their existing owners.

The context module is separate from the provider component because the project's Fast Refresh lint rule expects component modules to export components only.

## Create the In-Memory Provider

Create `src/features/schedule/serviceEventStore.tsx`:

```tsx
import { useState, type ReactNode } from 'react'
import type { CreateServiceEventCommand } from './createServiceEvent'
import type { ServiceEvent } from './serviceEvent'
import { ServiceEventContext } from './serviceEventContext'

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

type ServiceEventProviderProps = {
  children: ReactNode
  initialEvents?: readonly ServiceEvent[]
  eventIdFactory?: () => string
}

const createEventId = () => crypto.randomUUID()

export function ServiceEventProvider({
  children,
  initialEvents = seededServiceEvents,
  eventIdFactory = createEventId,
}: ServiceEventProviderProps) {
  const [events, setEvents] = useState<readonly ServiceEvent[]>(() => [
    ...initialEvents,
  ])

  function findById(eventId: string) {
    return events.find((event) => event.id === eventId)
  }

  function create(command: CreateServiceEventCommand) {
    const event: ServiceEvent = {
      id: eventIdFactory(),
      ...command,
      requiredPositions: 0,
      assignedPositions: 0,
    }

    setEvents((current) => [...current, event])
    return event
  }

  return (
    <ServiceEventContext value={{ events, findById, create }}>
      {children}
    </ServiceEventContext>
  )
}
```

The lazy state initializer copies the caller's collection once. `create` constructs a new record, uses a functional state update to append it immutably, and returns the same event so the creation page knows its new URL.

The optional initial collection and ID factory are test seams, not alternate production behavior. Runtime code uses the seeded collection and `crypto.randomUUID()`.

`<ServiceEventContext value={...}>` uses the React 19 provider shorthand available in this project. A React 18 application would render `<ServiceEventContext.Provider value={...}>` instead.

## Keep the Provider Alive Across Child Routes

Wrap the existing application shell in `App.tsx`:

```tsx
import { ServiceEventProvider } from './features/schedule/serviceEventStore'
```

Then place the shell inside the provider:

```tsx
export function App() {
  return (
    <ServiceEventProvider>
      <div className="application-shell">
        {/* Existing skip link, header, navigation, main outlet, and footer */}
      </div>
    </ServiceEventProvider>
  )
}
```

`App` is the parent layout route, so navigating among its child pages replaces only `<Outlet />`. The provider stays mounted, and a newly created event remains available when navigation opens its detail page.

This placement does not make the state durable across a browser refresh. It makes the state durable across client-side route transitions within one application session.

## Add the Creation and Detail Routes

Import the two pages in `router.tsx`:

```tsx
import { ServiceEventCreatePage } from './features/schedule/ServiceEventCreatePage'
import { ServiceEventDetailPage } from './features/schedule/ServiceEventDetailPage'
```

Add the routes after the schedule route:

```tsx
{
  path: 'schedule/new',
  Component: ServiceEventCreatePage,
},
{
  path: 'schedule/:eventId',
  Component: ServiceEventDetailPage,
},
```

`:eventId` is a dynamic segment. A URL such as `/schedule/sunday-2026-11-01` provides `sunday-2026-11-01` to route APIs under the `eventId` key.

The literal `schedule/new` route appears beside and before the parameter route for readability. React Router ranks static segments above dynamic ones, so `new` resolves to the creation page rather than becoming an event ID.

The existing wildcard remains last as the application-level fallback:

```tsx
{ path: '*', Component: NotFoundPage }
```

## Move Creation to Its Own Page

Create `src/features/schedule/ServiceEventCreatePage.tsx`:

```tsx
import { Link, useNavigate } from 'react-router'
import { ServiceEventForm } from './ServiceEventForm'
import type { CreateServiceEventCommand } from './createServiceEvent'
import { useServiceEvents } from './serviceEventContext'
import './schedule.css'

export function ServiceEventCreatePage() {
  const navigate = useNavigate()
  const { create } = useServiceEvents()

  function createServiceEvent(command: CreateServiceEventCommand) {
    const event = create(command)
    void navigate(`/schedule/${event.id}`)
  }

  return (
    <section className="schedule-route-page" aria-labelledby="create-heading">
      <Link className="back-link" to="/schedule">
        ← Back to schedule
      </Link>
      <p className="eyebrow">Schedule</p>
      <h1 id="create-heading">Create a service event</h1>
      <p className="schedule-introduction">
        Add the service details. After validation succeeds, ChurchServe opens
        the new event&apos;s stable detail URL.
      </p>

      <ServiceEventForm onSubmit={createServiceEvent} />
    </section>
  )
}
```

The Part 5 form remains unchanged. It owns controlled inputs, validation, and normalization. The route page owns the application workflow: create the event, then navigate to its detail URL.

`useNavigate` is appropriate because navigation follows a successful application command. Ordinary navigation initiated directly by a user remains a link.

The data-router implementation can return a promise from `navigate`, so `void` explicitly states that this synchronous handler does not await the transition.

## Resolve the Parameterized Detail Page

Create `src/features/schedule/ServiceEventDetailPage.tsx`:

```tsx
import { Link, useParams } from 'react-router'
import { useServiceEvents } from './serviceEventContext'
import './schedule.css'

const detailDateFormatter = new Intl.DateTimeFormat('en-CA', {
  dateStyle: 'full',
  timeStyle: 'short',
  timeZone: 'America/Toronto',
})

export function ServiceEventDetailPage() {
  const { eventId } = useParams<'eventId'>()
  const { findById } = useServiceEvents()
  const event = eventId ? findById(eventId) : undefined

  if (!event) {
    return (
      <section className="schedule-route-page" aria-labelledby="detail-heading">
        <Link className="back-link" to="/schedule">
          ← Back to schedule
        </Link>
        <div className="missing-event" role="alert">
          <p className="eyebrow">Service event</p>
          <h1 id="detail-heading">Service not found</h1>
          <p>This event does not exist or is no longer available.</p>
          <Link to="/schedule">Return to the schedule</Link>
        </div>
      </section>
    )
  }

  const openPositions = Math.max(
    0,
    event.requiredPositions - event.assignedPositions,
  )

  return (
    <section className="schedule-route-page" aria-labelledby="detail-heading">
      <Link className="back-link" to="/schedule">
        ← Back to schedule
      </Link>
      <p className="eyebrow">Service event</p>
      <h1 id="detail-heading">{event.title}</h1>

      <dl className="event-details">
        <div>
          <dt>Starts</dt>
          <dd>
            <time dateTime={event.startsAt}>
              {detailDateFormatter.format(new Date(event.startsAt))}
            </time>
          </dd>
        </div>
        <div>
          <dt>Ends</dt>
          <dd>
            <time dateTime={event.endsAt}>
              {detailDateFormatter.format(new Date(event.endsAt))}
            </time>
          </dd>
        </div>
        <div>
          <dt>Location</dt>
          <dd>{event.location}</dd>
        </div>
        <div>
          <dt>Staffing</dt>
          <dd>{staffingLabel(event.requiredPositions, openPositions)}</dd>
        </div>
      </dl>
    </section>
  )
}

function staffingLabel(requiredPositions: number, openPositions: number) {
  if (requiredPositions === 0) {
    return 'No positions configured'
  }

  if (openPositions === 0) {
    return 'Fully staffed'
  }

  return `${openPositions} open ${
    openPositions === 1 ? 'position' : 'positions'
  }`
}
```

`useParams<'eventId'>()` documents the expected key, but the returned value can still be `undefined`. The URL is external input, and a syntactically valid ID does not prove that a matching record exists.

The missing branch stays on the requested URL and explains the record-specific problem. It does not redirect to the generic 404 because the application understood the route shape successfully.

The detail list uses `<dl>`, `<dt>`, and `<dd>` for labeled values. Dates retain machine-readable ISO strings in `dateTime`, while visible text uses the same tutorial time zone as the cards.

## Replace Card Callbacks With Links

Update `ServiceEventCard.tsx` to import `Link`, remove `onSelect`, and replace the button:

```tsx
import { Link } from 'react-router'

type ServiceEventCardProps = {
  event: ServiceEvent
}
```

```tsx
<Link
  className="event-action"
  to={`/schedule/${event.id}`}
  aria-label={`View ${event.title}`}
>
  View schedule
</Link>
```

A link is the semantic control because the action changes location. It supports copying the URL, opening a new tab, browser history, and standard keyboard behavior without a click handler that manually imitates navigation.

Update the card test to render inside `MemoryRouter` and assert the destination:

```tsx
it('links to the event detail route', () => {
  renderCard()

  expect(
    screen.getByRole('link', { name: 'View Sunday Morning Service' }),
  ).toHaveAttribute('href', '/schedule/event-1')
})
```

## Make the Schedule a Route Index

`SchedulePage` no longer owns creation or selection state. Import the context hook and `Link`:

```tsx
import { useState } from 'react'
import { Link } from 'react-router'
import { useServiceEvents } from './serviceEventContext'
```

Replace the module-level event array and creation state with the shared collection plus the existing test-state seam:

```tsx
type SchedulePageProps = {
  initialState?: ScheduleState
}

export function SchedulePage({ initialState }: SchedulePageProps) {
  const { events: storedEvents } = useServiceEvents()
  const [stateOverride, setStateOverride] = useState<ScheduleState | undefined>(
    initialState,
  )
  const state: ScheduleState = stateOverride ?? {
    status: 'ready',
    events: storedEvents,
  }
  const events = state.status === 'ready' ? state.events : []

  function reloadSchedule() {
    setStateOverride(undefined)
  }
```

Production rendering uses the shared store. Tests can still start the schedule in loading, error, or ready-empty state. Retry clears the override and returns to the provider's ready collection.

Inside the ready branch, replace the embedded form with a creation link and render cards without callbacks:

```tsx
<div className="schedule-actions">
  <Link className="primary-link" to="/schedule/new">
    Create service
  </Link>
</div>

{events.length === 0 ? (
  <section className="state-panel empty-state">
    <h2>No upcoming services</h2>
    <p>Create an event when the next schedule is ready.</p>
  </section>
) : (
  <ul className="event-grid">
    {events.map((event) => (
      <li key={event.id}>
        <ServiceEventCard event={event} />
      </li>
    ))}
  </ul>
)}
```

The schedule becomes an index and navigation page. Creation mechanics belong to `/schedule/new`; event presentation belongs to `/schedule/:eventId`.

## Add Route-Page Styles

Add route layout, link, detail, and missing-state styles to `schedule.css`:

```css
.schedule-route-page {
  max-width: 76rem;
  margin: 0 auto;
  padding: clamp(3.5rem, 8vw, 6rem) clamp(1.25rem, 5vw, 4rem);
}

.schedule-route-page h1 {
  margin: 0;
  color: #102a43;
  font-size: clamp(2.4rem, 6vw, 4.25rem);
  letter-spacing: -0.045em;
  line-height: 1;
}

.back-link {
  display: inline-block;
  margin-bottom: 2rem;
  color: #087f5b;
  font-weight: 800;
}

.back-link:focus-visible,
.primary-link:focus-visible,
.missing-event a:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}

.schedule-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 2rem;
}

.primary-link {
  padding: 0.75rem 1.1rem;
  border-radius: 0.65rem;
  background: #087f5b;
  color: #fff;
  font-weight: 800;
  text-decoration: none;
}

.primary-link:hover {
  background: #066649;
}

.event-details {
  display: grid;
  margin: 2.5rem 0 0;
  padding: 0;
  border: 1px solid #d9e2ec;
  border-radius: 1.1rem;
  background: #fff;
  box-shadow: 0 0.9rem 2.5rem rgb(16 42 67 / 7%);
  grid-template-columns: repeat(2, minmax(0, 1fr));
  overflow: hidden;
}

.event-details > div {
  padding: 1.25rem;
  border-bottom: 1px solid #d9e2ec;
}

.event-details > div:nth-child(odd) {
  border-right: 1px solid #d9e2ec;
}

.event-details dt {
  color: #627d98;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

.event-details dd {
  margin: 0.4rem 0 0;
  color: #102a43;
  font-weight: 700;
  line-height: 1.5;
}

.missing-event {
  padding: 1.5rem;
  border: 1px solid #ffa8a8;
  border-radius: 1rem;
  background: #fff5f5;
  color: #862e2e;
}

@media (max-width: 38rem) {
  .event-details {
    grid-template-columns: 1fr;
  }

  .event-details > div:nth-child(odd) {
    border-right: 0;
  }
}
```

Keep the existing `.event-action` visual treatment but add `text-decoration: none`. The control is now an anchor, while its visible styling remains consistent with earlier cards.

## Test the Shared Store

Create `src/features/schedule/serviceEventStore.test.tsx` with two focused cases:

```tsx
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import type { ServiceEvent } from './serviceEvent'
import { useServiceEvents } from './serviceEventContext'
import { ServiceEventProvider } from './serviceEventStore'

describe('service event store', () => {
  it('finds a seeded event by its stable ID', () => {
    const { result } = renderHook(() => useServiceEvents(), {
      wrapper: ServiceEventProvider,
    })

    expect(result.current.findById('sunday-2026-11-01')?.title).toBe(
      'Sunday Morning Service',
    )
  })

  it('creates an event without mutating the seeded collection', () => {
    const initialEvents: readonly ServiceEvent[] = [
      {
        id: 'event-1',
        title: 'Sunday Service',
        startsAt: '2026-11-01T10:00:00-05:00',
        endsAt: '2026-11-01T11:00:00-05:00',
        location: 'Main Sanctuary',
        requiredPositions: 4,
        assignedPositions: 2,
      },
    ]
    const originalEvents = [...initialEvents]
    const wrapper = ({ children }: { children: ReactNode }) => (
      <ServiceEventProvider
        initialEvents={initialEvents}
        eventIdFactory={() => 'prayer-night'}
      >
        {children}
      </ServiceEventProvider>
    )
    const { result } = renderHook(() => useServiceEvents(), { wrapper })

    act(() => {
      result.current.create({
        title: 'Prayer Night',
        startsAt: '2026-11-06T19:00:00.000Z',
        endsAt: '2026-11-06T20:00:00.000Z',
        location: 'Chapel',
      })
    })

    expect(initialEvents).toEqual(originalEvents)
    expect(result.current.events).toHaveLength(2)
    expect(result.current.findById('prayer-night')).toEqual(
      expect.objectContaining({
        title: 'Prayer Night',
        requiredPositions: 0,
        assignedPositions: 0,
      }),
    )
  })
})
```

The first case protects seeded lookup. The second supplies a deterministic ID, verifies that the caller's collection remains unchanged, and observes the new event through the public context contract.

## Test the Real Route Table

The existing `App.test.tsx` already renders `appRoutes` with `createMemoryRouter`. Return the router from the helper so tests can verify location:

```tsx
function renderRoute(path = '/') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  render(<RouterProvider router={router} />)
  return router
}
```

Add these route cases:

```tsx
it('navigates from a schedule card to its parameterized detail route', async () => {
  const user = userEvent.setup()
  const router = renderRoute('/schedule')

  await user.click(
    screen.getByRole('link', { name: 'View Sunday Morning Service' }),
  )

  expect(
    await screen.findByRole('heading', { name: 'Sunday Morning Service' }),
  ).toBeVisible()
  expect(router.state.location.pathname).toBe(
    '/schedule/sunday-2026-11-01',
  )
})

it('opens the dedicated service creation route', () => {
  renderRoute('/schedule/new')

  expect(
    screen.getByRole('heading', { name: 'Create a service event' }),
  ).toBeVisible()
})

it('shows a record-specific state for an unknown event ID', () => {
  renderRoute('/schedule/missing')

  expect(screen.getByRole('alert')).toHaveTextContent('Service not found')
  expect(
    screen.getByRole('link', { name: 'Return to the schedule' }),
  ).toHaveAttribute('href', '/schedule')
})
```

Add one end-to-end creation case. Mock only ID generation; use the real route table, provider, form, store, and detail page:

```tsx
it('creates an event and navigates to its detail URL', async () => {
  const user = userEvent.setup()
  vi.spyOn(crypto, 'randomUUID').mockReturnValue(
    '00000000-0000-4000-8000-000000000006',
  )
  const router = renderRoute('/schedule/new')

  await user.type(screen.getByLabelText('Service name'), '  Prayer Night  ')
  await user.type(screen.getByLabelText('Starts'), '2026-11-06T19:00')
  await user.type(screen.getByLabelText('Ends'), '2026-11-06T20:00')
  await user.type(screen.getByLabelText('Location'), '  Chapel  ')
  await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

  expect(
    await screen.findByRole('heading', { name: 'Prayer Night' }),
  ).toBeVisible()
  expect(screen.getByText('Chapel')).toBeVisible()
  expect(router.state.location.pathname).toBe(
    '/schedule/00000000-0000-4000-8000-000000000006',
  )
})
```

Call `vi.restoreAllMocks()` after each test so the UUID stub cannot leak into another case.

These tests exercise meaningful user behavior rather than calling route components directly. They prove link navigation, parameter resolution, missing-record handling, creation, shared state, and programmatic post-submit navigation as one integrated route system.

## Update the Project Notes

Add Part 6 to `README.md`:

```md
## Part 6

The `part_6` branch adds shared in-memory event state, dedicated creation and
parameterized detail routes, semantic card links, record-specific missing
content, programmatic post-submit navigation, and route-level tests.
```

## Run the Part 6 Quality Gate

Run all repository checks:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed Part 6 checkpoint has six test files and thirty-one passing tests:

```text
Test Files  6 passed (6)
Tests       31 passed (31)
```

Start the development server:

```bash
npm run dev
```

Verify the following browser flow:

1. Open `/schedule` and follow a card link.
2. Copy or refresh a seeded event's detail URL.
3. Open `/schedule/new`, create an event, and confirm navigation to its detail URL.
4. Open `/schedule/missing` and confirm the record-specific message.
5. Open an unrelated URL and confirm the generic 404 page.

## Production Boundaries

Part 6 makes URLs durable, but it does not make newly created data durable. Seeded event URLs survive refresh because the provider reconstructs those records. A newly created event exists only in memory; refreshing its detail URL produces the valid record-missing state.

That limitation is explicit and testable. Later persistence work should replace the concrete provider boundary without rewriting the route contract.

Additional production concerns remain:

- Treat route parameters as untrusted input at the API and storage boundaries.
- Load detail data asynchronously and distinguish pending, missing, forbidden, and failed responses.
- Authorize access independently from knowledge of a shareable URL.
- Define route-level error handling for failed creation and navigation.
- Use the organization's configured time zone rather than the tutorial formatter.
- Consider route loaders once data access becomes asynchronous and URL-driven.

React Router's official documentation covers [dynamic route segments and links](https://reactrouter.com/start/declarative/routing), [reading URL parameters](https://reactrouter.com/start/declarative/url-values), [programmatic navigation](https://reactrouter.com/api/hooks/useNavigate), and [memory routers for non-browser tests](https://reactrouter.com/api/data-routers/createMemoryRouter).

## Part 6 Checkpoint

ChurchServe now has bookmarkable seeded-event URLs, a dedicated creation page, semantic card links, shared event state across sibling routes, and separate experiences for an unknown record and an unknown application URL.

Confirm that:

- `/schedule/new` renders the existing typed form.
- `/schedule/:eventId` resolves the ID with `useParams`.
- Cards use links rather than navigation buttons.
- Successful creation navigates to the generated detail URL.
- Unknown event IDs stay on their URL and show “Service not found.”
- Unmatched URLs still render the generic application 404.
- The store appends immutably and preserves caller-owned seed arrays.
- All thirty-one tests and the production build pass.

Part 7 will put a repository interface in front of the concrete in-memory store so future persistence implementations can change without rewriting route pages.
