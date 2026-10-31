---
title: "ChurchServe React Tutorial Part 9: Async Repositories and Request Cancellation"
excerpt: "Model honest asynchronous data boundaries in React with abort-aware repository operations, explicit route request states, retries, and safe form submission feedback."
category: "React Tutorial"

seo:
  focusKeyword: "React async repository AbortController"
  description: "Build an abort-aware async repository in React with loading, error, retry, not-found, pending, and stale-response handling in TypeScript."
  socialTitle: "React Async Repositories and Request Cancellation"
  socialDescription: "Move from synchronous browser storage to honest async React request states with AbortController, retries, and safe submission feedback."
---

# ChurchServe React Tutorial Part 9: Async Repositories and Request Cancellation

[Part 8](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-8-versioned-localstorage-persistence/) persisted service events behind a synchronous repository. That contract matched Web Storage, but it cannot honestly describe an HTTP request. Network work has latency, can fail after a page renders, and can become obsolete when the user navigates elsewhere.

Part 9 changes the boundary deliberately. Repository operations now return promises and accept `AbortSignal`. Schedule and detail routes model loading, success, empty, missing, and failure states. Creation exposes pending and failure feedback without erasing entered values. Effect cleanup aborts obsolete requests, while completion handlers also ignore late results from adapters that do not cooperate with cancellation.

> **Quick answer:** Make asynchrony and cancellation part of the repository contract instead of hiding them inside components or returning already-resolved promises from a synchronous interface. Each route should own a discriminated request state, start work in an effect, abort it during cleanup, ignore stale completion, and render user-visible loading, retry, empty, or failure feedback.

## Why the synchronous contract must change

The Part 8 contract returned values immediately and exposed subscriptions:

```typescript
interface ServiceEventRepository {
  list: () => readonly ServiceEvent[]
  findById: (eventId: string) => ServiceEvent | undefined
  create: (command: CreateServiceEventCommand) => ServiceEvent
  subscribe: (listener: ServiceEventListener) => () => void
}
```

That model works for an external store already held in the browser. It creates the wrong expectations for HTTP:

- `list()` cannot return before a response arrives.
- absence and request failure are different outcomes;
- a detail request can become obsolete when the route parameter changes;
- a create request needs pending and failure feedback;
- remote data does not push a new immutable snapshot through a local subscription after every mutation.

Changing the contract is safer than pretending remote calls are synchronous or adding promises only inside individual pages.

## Define an abort-aware asynchronous repository

Update `serviceEventRepository.ts`:

```typescript
import type { CreateServiceEventCommand } from './createServiceEvent'
import type { ServiceEvent } from './serviceEvent'

export interface ServiceEventRepository {
  list: (signal?: AbortSignal) => Promise<readonly ServiceEvent[]>
  findById: (
    eventId: string,
    signal?: AbortSignal,
  ) => Promise<ServiceEvent | undefined>
  create: (
    command: CreateServiceEventCommand,
    signal?: AbortSignal,
  ) => Promise<ServiceEvent>
}
```

The promise makes latency explicit to every caller. The optional signal lets a route cancel work when its inputs change or it unmounts. Keeping the signal optional also makes simple repository tests concise.

The subscription disappears. Routes now request data and own the result state. A later caching layer may add invalidation or shared query state, but a synchronous external-store subscription is not the right abstraction for request lifecycle.

## Adapt local implementations without losing cancellation semantics

Part 9 remains runnable without a server, so the local repositories implement the new contract. Their operations still check the signal:

```typescript
readonly list = async (signal?: AbortSignal) => {
  signal?.throwIfAborted()
  return this.events
}

readonly findById = async (eventId: string, signal?: AbortSignal) => {
  signal?.throwIfAborted()
  return this.events.find((event) => event.id === eventId)
}
```

`AbortSignal.throwIfAborted()` throws the signal's reason when cancellation already happened. The local operation completes immediately, but it still honors the same observable contract as a future transport adapter.

Creation follows the same rule before changing storage:

```typescript
readonly create = async (
  command: CreateServiceEventCommand,
  signal?: AbortSignal,
) => {
  signal?.throwIfAborted()

  const event: ServiceEvent = {
    id: this.eventIdFactory(),
    ...command,
    requiredPositions: 0,
    assignedPositions: 0,
  }

  const nextEvents = [...this.events, event]
  this.persist(nextEvents)
  this.events = nextEvents
  return event
}
```

The adapter no longer notifies React listeners. The caller awaits the mutation and decides what the interface should do next.

## Keep repository access in context

Context still distributes the selected dependency, but `useSyncExternalStore` is no longer involved:

```typescript
import { createContext, useContext } from 'react'
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
```

Context answers one question: which repository does this application tree use? Request state remains close to the route that presents it.

## Load the schedule with an effect

The schedule already has a useful discriminated union:

```typescript
export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; events: readonly ServiceEvent[] }
```

It now represents a real operation rather than test-injected presentation state:

```typescript
export function SchedulePage() {
  const repository = useServiceEventRepository()
  const [state, setState] = useState<ScheduleState>({ status: 'loading' })
  const [requestVersion, setRequestVersion] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    void repository
      .list(controller.signal)
      .then((loadedEvents) => {
        if (!controller.signal.aborted) {
          setState({ status: 'ready', events: loadedEvents })
        }
      })
      .catch((error: unknown) => {
        if (!isAbortError(error)) {
          setState({
            status: 'error',
            message: 'The scheduling service did not respond. Try again.',
          })
        }
      })

    return () => controller.abort()
  }, [repository, requestVersion])
```

React runs effect cleanup before rerunning an effect with changed dependencies and when the component unmounts. Aborting during cleanup tells a cooperative adapter to stop unnecessary work.

The `aborted` check after resolution provides a second defense. A fake, legacy adapter, or non-fetch operation might accept a signal but still resolve after cancellation. That stale result must not replace state for a newer request.

## Retry by starting a new request

Retry is not a special repository method. It moves the UI to loading and changes a request key:

```typescript
function reloadSchedule() {
  setState({ status: 'loading' })
  setRequestVersion((version) => version + 1)
}
```

The effect cleans up its previous controller and starts another `list` operation. The error panel remains accessible:

```tsx
{state.status === 'error' && (
  <section className="state-panel error-state" role="alert">
    <h2>We could not load the schedule</h2>
    <p>{state.message}</p>
    <button className="retry-action" type="button" onClick={reloadSchedule}>
      Try again
    </button>
  </section>
)}
```

The ready state can then distinguish an empty array from a failed request. An empty schedule is successful data, not an error.

## Model detail loading, missing, and failure separately

A detail route needs four states:

```typescript
const [state, setState] = useState<
  | { status: 'loading'; eventId: string | undefined }
  | { status: 'error'; eventId: string }
  | { status: 'not-found'; eventId: string }
  | { status: 'ready'; eventId: string; event: ServiceEvent }
>({ status: 'loading', eventId })
```

Including `eventId` prevents a previous record from flashing when navigation changes the parameter before the next effect runs. Render loading whenever the stored result belongs to another ID:

```typescript
const currentState =
  state.eventId === eventId
    ? state
    : { status: 'loading' as const, eventId }
```

The effect awaits the selected record and distinguishes explicit absence:

```typescript
void repository
  .findById(eventId, controller.signal)
  .then((event) => {
    if (!controller.signal.aborted) {
      setState(
        event
          ? { status: 'ready', eventId, event }
          : { status: 'not-found', eventId },
      )
    }
  })
  .catch((error: unknown) => {
    if (!isAbortError(error)) {
      setState({ status: 'error', eventId })
    }
  })
```

`undefined` means the request completed and the record does not exist. A rejected promise means the application cannot currently determine whether it exists. Those outcomes need different messages and different recovery actions.

## Detect cancellation without hiding real failures

Keep the cancellation check small:

```typescript
export function isAbortError(error: unknown) {
  return error instanceof Error && error.name === 'AbortError'
}
```

An aborted request should not show an alarming error when the user simply navigated away. Other failures transition to error state. Do not catch every rejection and label it cancellation.

## Make event creation honestly asynchronous

The creation route owns its request controller and feedback:

```typescript
const activeRequest = useRef<AbortController | null>(null)
const [isSubmitting, setIsSubmitting] = useState(false)
const [submissionError, setSubmissionError] = useState<string>()

useEffect(
  () => () => {
    activeRequest.current?.abort()
    activeRequest.current = null
  },
  [],
)
```

The submit callback awaits the repository before navigating:

```typescript
async function createServiceEvent(command: CreateServiceEventCommand) {
  const controller = new AbortController()
  activeRequest.current = controller
  setIsSubmitting(true)
  setSubmissionError(undefined)

  try {
    const event = await repository.create(command, controller.signal)
    controller.signal.throwIfAborted()
    await navigate(`/schedule/${event.id}`)
  } catch (error) {
    if (!isAbortError(error)) {
      setSubmissionError(
        'We could not create the service event. Your entries are still here.',
      )
    }
    throw error
  } finally {
    if (activeRequest.current === controller) {
      activeRequest.current = null
      setIsSubmitting(false)
    }
  }
}
```

The second abort check matters when an adapter finishes despite cancellation. Navigation should not occur after the user has already left the form.

The route rethrows so the form knows submission did not succeed. The form catches that rejection after the route has set user-facing feedback and deliberately preserves its controlled values.

## Expose pending state in the form

Change the form contract to return a promise:

```typescript
type ServiceEventFormProps = {
  isSubmitting?: boolean
  submissionError?: string
  onSubmit: (command: CreateServiceEventCommand) => Promise<void>
}
```

After local validation succeeds, await the callback before clearing fields:

```typescript
try {
  await onSubmit(result.command)
  setValues(emptyValues)
  setErrors({})
  setHasSubmitted(false)
} catch {
  // The route owns request feedback and preserves these form values.
}
```

Use `aria-busy`, an alert, and a disabled submit action:

```tsx
<form aria-busy={isSubmitting} onSubmit={handleSubmit}>
  {submissionError && (
    <p className="form-error-summary field-wide" role="alert">
      {submissionError}
    </p>
  )}

  {/* controlled fields */}

  <button type="submit" disabled={isSubmitting}>
    {isSubmitting ? 'Adding to schedule…' : 'Add to schedule'}
  </button>
</form>
```

Disabling the action prevents obvious duplicate submissions. The API must still provide idempotency or duplicate detection because clients, retries, and network failures can produce ambiguous outcomes.

## Test behavior at the asynchronous boundary

Part 9 replaces synchronous assertions with user-observable waits. A successful schedule test uses `findAllByRole` because ready content appears after the promise resolves.

Error and retry tests configure sequential results:

```typescript
const list = vi
  .fn<ServiceEventRepository['list']>()
  .mockRejectedValueOnce(new Error('Unavailable'))
  .mockImplementation((signal) => seededRepository.list(signal))
```

Focused route tests verify that:

- a failed detail request renders retry feedback and the second request succeeds;
- unmounting aborts the active detail signal;
- a failed creation preserves entered form values;
- a pending form disables its submit action;
- local repositories reject already-aborted work;
- loading and empty states do not masquerade as each other.

These tests assert the contract users experience rather than implementation timing such as a specific number of microtasks.

## Validate the Part 9 checkpoint

Run the complete checkpoint:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The finalized branch passes lint and strict TypeScript checking, runs 43 tests across 8 files, and produces the Vite production bundle.

## Production boundaries

Part 9 establishes an honest asynchronous interface, but its selected implementation still uses browser storage.

- Cancellation is cooperative. Pass the signal through every future transport layer.
- An abort can race with completion, so routes both abort work and ignore stale results.
- User-facing messages remain intentionally generic; diagnostics should record technical causes without exposing sensitive details.
- Disabling a button does not guarantee server-side idempotency.
- Route-owned fetching can duplicate requests when several routes need the same data. Add caching only when that requirement is real.
- Client validation improves usability, but an API must repeat authorization and business validation.
- Loading data in effects is appropriate for this client-only checkpoint. A later router-data or query-cache design may move ownership while retaining the same state distinctions.

Part 10 can implement the HTTP adapter behind this contract. It should pass `AbortSignal` to `fetch`, validate response bodies at runtime, distinguish `404` from transport failure, and translate non-success responses into typed repository errors.

## What Part 9 established

ChurchServe now models data work as requests instead of immediate local reads.

- Repository methods return promises and accept cancellation signals.
- Schedule and detail routes own explicit request states.
- Retry starts a fresh operation rather than mutating an old error.
- Missing data remains distinct from failed data.
- Effect cleanup aborts obsolete work and completion guards reject stale results.
- Creation waits for success, exposes pending state, and preserves input after failure.
- Tests cover resolution, rejection, retry, cancellation, and submission behavior.

The UI is now ready for a real transport adapter without rewriting its visible request lifecycle.
