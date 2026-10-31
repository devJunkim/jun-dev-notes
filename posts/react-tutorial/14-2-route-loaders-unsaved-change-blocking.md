---
title: "ChurchServe React Tutorial Part 14-2: Route Loaders and Unsaved-Change Blocking"
excerpt: "Move detail fetching into React Router loaders, model route errors explicitly, cancel obsolete work, and protect dirty forms from accidental navigation."
category: "React Tutorial"

seo:
  focusKeyword: "React Router loader useBlocker unsaved changes"
  description: "Use React Router loaders, error boundaries, AbortSignal, useBlocker, and useBeforeUnload to build resilient data routes and dirty-form protection."
  socialTitle: "React Router Loaders and Unsaved-Change Blocking"
  socialDescription: "Load route data before rendering, handle missing and failed records, cancel stale requests, and protect unfinished React forms."
---

# ChurchServe React Tutorial Part 14-2: Route Loaders and Unsaved-Change Blocking

Part 14-1 protected authenticated route branches and safely returned users to deep links after sign-in. Two navigation responsibilities remain inside page components: the detail page starts its own fetch after rendering, and the creation page allows a link click to discard entered fields without warning.

Part 14-2 moves record loading to React Router's data boundary and makes unfinished work part of the navigation contract. These changes use Data Mode, which the application already enables with `createBrowserRouter` and `RouterProvider`.

> **Quick answer:** Give the parameterized detail route a loader that receives the route parameter and `request.signal`, returns the record, and throws explicit `404` or `503` responses. Read successful data with `useLoaderData` and render failures through a route error boundary. Track whether the controlled creation form contains user input, use `useBlocker` for in-app navigation, use `useBeforeUnload` for refresh or tab closure, and present accessible Keep editing and Discard actions. Clear the dirty state before navigating after a successful save.

## Why move detail fetching to the route

The Part 9 detail page loaded data in an effect. It needed local state for loading, ready, not found, and failure, plus a request version for retries and cleanup logic for cancellation.

That design remains useful when data loading is truly component-owned. A detail record, however, is required by the route itself. Rendering the page without that record does not produce a meaningful screen.

React Router loaders provide a better ownership boundary:

- the URL parameter and request lifecycle arrive together;
- data resolves before the route component renders;
- navigation cancellation reaches the repository through `request.signal`;
- missing records and infrastructure failures reach an error boundary;
- retry is router revalidation rather than component request-version state;
- the successful page only renders successful data.

React Router's [Data Mode guide](https://reactrouter.com/start/data/custom) shows that `createBrowserRouter` route objects support loaders, actions, and error boundaries without requiring the full React Router framework.

## Create an injected detail loader

The application constructs its repository outside React components. Use a factory so the loader receives that same dependency:

```typescript
import type { LoaderFunction } from 'react-router'
import { isAbortError } from './requestError'
import type { ServiceEventRepository } from './serviceEventRepository'

export function createServiceEventDetailLoader(
  repository: ServiceEventRepository,
): LoaderFunction {
  return async ({ params, request }) => {
    const eventId = params.eventId

    if (!eventId) {
      throw new Response(null, {
        status: 400,
        statusText: 'A service event ID is required.',
      })
    }

    let event
    try {
      event = await repository.findById(eventId, request.signal)
    } catch (error) {
      if (request.signal.aborted || isAbortError(error)) {
        throw error
      }

      throw new Response(null, {
        status: 503,
        statusText: 'The scheduling service did not respond.',
      })
    }

    if (!event) {
      throw new Response(null, {
        status: 404,
        statusText: 'This service event was not found.',
      })
    }

    return event
  }
}
```

The factory keeps transport selection in the composition root. Tests can inject a controlled repository without mocking modules or network globals.

The loader preserves three distinct outcomes:

- a returned event is successful route data;
- `undefined` means the request succeeded but the ID does not exist;
- a rejected repository call means the application could not determine the record state.

Do not convert cancellation into `503`. Navigation made that work obsolete; it is not a service outage the user needs to see.

## Pass the router's cancellation signal through

Every loader receives a Fetch API `Request`. React Router aborts its signal when a navigation makes the loader obsolete:

```typescript
event = await repository.findById(eventId, request.signal)
```

The HTTP repository already forwards that signal to `fetch`. The cancellation path is therefore continuous:

```text
new browser navigation
  -> React Router aborts request.signal
  -> loader passes the signal to the repository
  -> repository passes the signal to fetch
  -> obsolete network work stops
```

This replaces the detail component's manual `AbortController`. Cancellation still remains cooperative. Every adapter between the router and transport must forward the signal, and completion logic must avoid turning an expected abort into visible failure.

React Router's route-object documentation confirms that loaders run before the route component and make data available through `useLoaderData`.

## Register data and error boundaries together

Attach the loader and error boundary to the parameterized route:

```tsx
{
  path: 'schedule/:eventId',
  loader: createServiceEventDetailLoader(repository),
  Component: ServiceEventDetailPage,
  ErrorBoundary: ServiceEventDetailErrorPage,
}
```

The route now declares everything required to enter that screen: URL shape, data preparation, successful UI, and failure UI.

The detail component becomes much smaller:

```tsx
export function ServiceEventDetailPage() {
  const event = useLoaderData() as ServiceEvent
  const openPositions = Math.max(
    0,
    event.requiredPositions - event.assignedPositions,
  )

  return (
    <section aria-labelledby="detail-heading">
      <Link to="/schedule">← Back to schedule</Link>
      <p className="eyebrow">Service event</p>
      <h1 id="detail-heading">{event.title}</h1>
      {/* record details */}
    </section>
  )
}
```

There is no effect, request version, or local request union. The component's contract is now straightforward: if React Router renders it, a `ServiceEvent` is available.

In a React Router framework project, generated route types can avoid the assertion. This Vite application defines route objects manually, so the loader and component currently share the contract through the repository type and a local assertion.

## Render not-found and unavailable states separately

The error boundary inspects the route error:

```tsx
export function ServiceEventDetailErrorPage() {
  const error = useRouteError()
  const revalidator = useRevalidator()
  const isNotFound =
    isRouteErrorResponse(error) && error.status === 404

  return (
    <section aria-labelledby="detail-heading">
      <Link to="/schedule">← Back to schedule</Link>

      {isNotFound ? (
        <div className="missing-event" role="alert">
          <h1 id="detail-heading">Service not found</h1>
          <p>This event does not exist or is no longer available.</p>
          <Link to="/schedule">Return to the schedule</Link>
        </div>
      ) : (
        <div className="state-panel error-state" role="alert">
          <h1 id="detail-heading">We could not load this service</h1>
          <p>The scheduling service did not respond.</p>
          <button
            type="button"
            disabled={revalidator.state !== 'idle'}
            onClick={() => revalidator.revalidate()}
          >
            {revalidator.state === 'loading'
              ? 'Trying again…'
              : 'Try again'}
          </button>
        </div>
      )}
    </section>
  )
}
```

A missing record has no useful retry unless the user expects eventual creation. A service failure can recover, so `useRevalidator()` reruns the matched loader. The button disables and changes its label while the router is trying again.

This boundary intentionally displays a bounded message. Technical details belong in telemetry, not in an error page that might expose URLs, stack traces, or server internals.

## Define what makes the creation form dirty

The form already owns its controlled values. Add one optional callback:

```typescript
type ServiceEventFormProps = {
  isSubmitting?: boolean
  submissionError?: string
  onDirtyChange?: (isDirty: boolean) => void
  onSubmit: (command: CreateServiceEventCommand) => Promise<void>
}
```

Report dirty state whenever a field changes:

```typescript
function updateField(
  field: keyof ServiceEventFormValues,
  value: string,
) {
  const nextValues = { ...values, [field]: value }
  setValues(nextValues)
  onDirtyChange?.(
    Object.values(nextValues).some(
      (fieldValue) => fieldValue.length > 0,
    ),
  )

  // existing post-submit validation omitted
}
```

For this create form, dirty means at least one field contains any characters. Whitespace still counts because leaving would discard something the user entered, even though validation later rejects a whitespace-only name.

An edit form should usually compare normalized current values with its original server snapshot. A simple non-empty check would incorrectly mark pre-populated data as dirty.

After successful submission, the form reports clean while it resets:

```typescript
await onSubmit(result.command)
setValues(emptyValues)
onDirtyChange?.(false)
```

Failed submission preserves both values and dirty state.

## Block in-application navigation

The route owns the navigation consequence, so it owns the dirty boolean:

```tsx
const [isDirty, setIsDirty] = useState(false)
const hasSaved = useRef(false)

const blocker = useBlocker(
  useCallback(
    ({ currentLocation, nextLocation }) =>
      isDirty &&
      !hasSaved.current &&
      (currentLocation.pathname !== nextLocation.pathname ||
        currentLocation.search !== nextLocation.search),
    [isDirty],
  ),
)
```

The function ignores navigation that does not change path or query. A product may choose to include hash changes too if fragments switch meaningful views.

`useBlocker` protects navigation managed by the SPA. React Router returns a blocker with three relevant states:

- `unblocked` when no transition is waiting;
- `blocked` after a transition was intercepted;
- `proceeding` after the user accepts it.

The hook does not display a prompt. That is an application responsibility, which allows accessible, testable UI instead of a browser-dependent `window.confirm` call. React Router's [navigation blocking guide](https://reactrouter.com/how-to/navigation-blocking) documents the same state-driven pattern.

## Protect refresh and tab closure separately

An SPA blocker cannot stop a hard refresh, closing the tab, or navigating to another origin. Add a `beforeunload` callback for those browser-level transitions:

```typescript
useBeforeUnload(
  useCallback(
    (event) => {
      if (isDirty && !hasSaved.current) {
        event.preventDefault()
        event.returnValue = ''
      }
    },
    [isDirty],
  ),
)
```

Modern browsers control the wording and conditions of the native prompt. The application cannot depend on a custom message. This is a last line of defense, not an autosave system, and browser behavior may vary by platform and user interaction history.

Avoid installing `beforeunload` globally. Register it only while meaningful unsaved work exists because it can affect performance and browser page-cache behavior.

## Present an accessible discard decision

Render confirmation UI when the blocker reaches `blocked`:

```tsx
{blocker.state === 'blocked' && (
  <section
    role="alertdialog"
    aria-labelledby="discard-heading"
    aria-describedby="discard-description"
  >
    <h2 id="discard-heading">Discard this service event?</h2>
    <p id="discard-description">
      Your unsaved entries will be lost if you leave this page.
    </p>
    <button type="button" onClick={() => blocker.reset()}>
      Keep editing
    </button>
    <button type="button" onClick={() => blocker.proceed()}>
      Discard and leave
    </button>
  </section>
)}
```

`reset()` cancels the pending transition and preserves form state. `proceed()` completes the exact transition React Router intercepted.

This tutorial keeps the confirmation inline so its behavior is visible. A production modal dialog should also manage initial focus, keep keyboard focus inside while open, support Escape according to product policy, and return focus appropriately after cancellation. ARIA roles do not implement those interaction details by themselves.

## Do not block navigation after a successful save

Creation legitimately navigates to the new detail URL. Mark the save before making that transition:

```typescript
const event = await repository.create(command, controller.signal)
controller.signal.throwIfAborted()
hasSaved.current = true
setIsDirty(false)
await navigate(`/schedule/${event.id}`)
```

The ref changes synchronously, so the blocker sees that the transition follows a confirmed save even before React processes the state update. Setting the dirty state to false keeps rendered state consistent.

Do not clear either value before the repository resolves. If creation fails, the route shows its existing error and the user retains both entries and navigation protection.

## Test with a data router

`useLoaderData` and `useBlocker` require a data router. Component tests using only `<MemoryRouter>` are no longer representative. Build a memory data router instead:

```tsx
const router = createMemoryRouter(
  [
    {
      path: '/',
      element: (
        <ServiceEventProvider repository={repository}>
          <Outlet />
        </ServiceEventProvider>
      ),
      children: [
        {
          path: 'schedule/:eventId',
          loader: createServiceEventDetailLoader(repository),
          Component: ServiceEventDetailPage,
          ErrorBoundary: ServiceEventDetailErrorPage,
        },
        {
          path: 'schedule/new',
          Component: ServiceEventCreatePage,
        },
        { path: 'other', element: <h1>Other route</h1> },
      ],
    },
  ],
  { initialEntries: [path] },
)

render(<RouterProvider router={router} />)
```

The loader tests verify that:

- the first failure renders retry feedback and revalidation succeeds;
- navigating away aborts the loader's repository signal;
- an unknown ID renders the not-found boundary;
- resolved data renders the detail route.

The dirty-form test enters a value and starts navigation programmatically. It verifies that the location does not change, Keep editing retains the value, and a second attempt followed by Discard and leave reaches the destination.

These tests exercise router behavior rather than calling blocker or loader functions in isolation.

## Run the complete Part 14 checkpoint

Run the React quality gate:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The finalized checkpoint passes lint and strict TypeScript checking, runs 66 React tests across 11 files, and produces the Vite production bundle.

The API did not change in Part 14, but its existing verification still passes:

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

Route data and navigation protection need deliberate policies:

- A component route guard does not prevent all matched loaders from starting. The API must reject unauthorized requests, and sensitive applications may also enforce authentication in router loaders or middleware.
- Translate known HTTP statuses deliberately. A future `401` should update session state, while `403`, `404`, validation failures, and infrastructure failures need distinct handling.
- Error boundaries should log bounded diagnostic context without exposing response bodies, cookies, tokens, or stack traces to users.
- Cancellation saves work but does not reverse a mutation the server already committed.
- Unsaved-change prompts reduce accidental loss; they do not provide recovery after a crash. Important workflows may need local drafts or server-side autosave.
- Dirty comparison should reflect domain meaning. Normalize values carefully without hiding meaningful edits.
- Browser `beforeunload` behavior is constrained and inconsistent. Test the supported production browsers.
- A modal confirmation needs complete focus management, not only an ARIA role.

## What Part 14-2 established

ChurchServe now treats route data and unfinished work as navigation concerns:

- the detail route owns its required loader;
- route parameters and cancellation arrive through the loader request;
- the repository receives React Router's abort signal;
- successful pages consume `useLoaderData` without local fetch effects;
- missing records and service failures render through an error boundary;
- revalidation retries failed loaders;
- controlled form values report meaningful dirty state;
- `useBlocker` intercepts internal navigation;
- `useBeforeUnload` covers refresh and tab closure;
- accessible actions let users keep editing or discard;
- successful creation bypasses the blocker only after persistence succeeds;
- tests run through a real memory data router.

Part 14 completes the first authenticated navigation boundary. The next tutorial can build on these routes without reintroducing page-local fetching or accidental form loss.
