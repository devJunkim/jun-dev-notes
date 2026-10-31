---
title: "ChurchServe React Tutorial Part 5: Controlled Forms and Validation"
excerpt: "Create service events with typed controlled inputs, pure field and time-range validation, accessible errors, normalized commands, and immutable schedule updates."
category: "React Tutorial"

seo:
  focusKeyword: "React controlled form validation tutorial"
  description: "Build a typed React controlled form with field validation, cross-field time rules, accessible errors, normalized commands, and integration tests."
  socialTitle: "ChurchServe Part 5: React Forms and Validation"
  socialDescription: "Create valid service events with controlled React inputs, accessible validation, normalized commands, and tested parent state updates."
---

# ChurchServe React Tutorial Part 5: Controlled Forms and Validation

[Part 4](https://dev.jun-kim.net/2026/10/30/churchserve-react-tutorial-part-4-usereducer-and-volunteer-assignment-state/) established tested assignment state and domain rules. ChurchServe can now represent scheduling decisions, but a coordinator still cannot create a service event from the browser.

That workflow needs more than four inputs. It needs a typed editing model, required and length rules, a time-range rule spanning two fields, accessible feedback, normalization at the submission boundary, and a parent that decides how a valid command changes application state.

> **Quick answer:** Keep editing strings local to a controlled form, validate them with a pure parser, show errors only after an attempted submission, emit a normalized command when valid, and let the schedule page create the ID and update its event collection immutably.

## What You Will Build

Part 5 adds three files and updates the existing schedule page, tests, styles, and README:

```text
src/features/schedule/
├── createServiceEvent.ts
├── ServiceEventForm.test.tsx
├── ServiceEventForm.tsx
├── SchedulePage.test.tsx
├── SchedulePage.tsx
├── schedule.css
└── existing schedule files
```

The responsibilities remain deliberately separate:

```text
ServiceEventForm
  ├── owns editable strings and visible validation
  └── emits CreateServiceEventCommand

SchedulePage
  ├── creates the temporary client ID
  ├── appends the new ServiceEvent
  └── announces successful creation
```

The form never mutates the schedule and the page never reaches into the form's local editing state.

## Define the Form and Command Boundary

Create `src/features/schedule/createServiceEvent.ts`:

```ts
export type CreateServiceEventCommand = {
  readonly title: string
  readonly startsAt: string
  readonly endsAt: string
  readonly location: string
}

export type ServiceEventFormValues = {
  readonly title: string
  readonly startsAt: string
  readonly endsAt: string
  readonly location: string
}

export type ServiceEventFormErrors = Partial<
  Record<keyof ServiceEventFormValues | 'timeRange', string>
>

export type ServiceEventFormResult =
  | { valid: true; command: CreateServiceEventCommand }
  | { valid: false; errors: ServiceEventFormErrors }
```

`ServiceEventFormValues` describes browser editing state. Every field is a string because text and `datetime-local` controls edit strings. Keeping those values non-null from the first render also prevents a React input from switching between uncontrolled and controlled modes.

`CreateServiceEventCommand` describes the validated application request. It contains trimmed text and ISO timestamps, but it excludes the event ID and staffing counts owned by the parent workflow.

The result is a discriminated union. Code must prove `valid` before it can read either `command` or `errors`, so invalid form values cannot accidentally cross the command boundary.

## Parse and Validate the Form Values

Continue `createServiceEvent.ts` with the pure parser:

```ts
export function parseServiceEventForm(
  values: ServiceEventFormValues,
): ServiceEventFormResult {
  const errors: ServiceEventFormErrors = {}
  const title = values.title.trim()
  const location = values.location.trim()
  const start = Date.parse(values.startsAt)
  const end = Date.parse(values.endsAt)

  if (title.length === 0) {
    errors.title = 'Enter a service name.'
  } else if (title.length > 120) {
    errors.title = 'Service name must be 120 characters or fewer.'
  }

  if (values.startsAt.length === 0 || Number.isNaN(start)) {
    errors.startsAt = 'Enter a valid start date and time.'
  }

  if (values.endsAt.length === 0 || Number.isNaN(end)) {
    errors.endsAt = 'Enter a valid end date and time.'
  }

  if (!Number.isNaN(start) && !Number.isNaN(end) && end <= start) {
    errors.timeRange = 'End time must be after start time.'
  }

  if (location.length === 0) {
    errors.location = 'Enter a service location.'
  } else if (location.length > 120) {
    errors.location = 'Location must be 120 characters or fewer.'
  }

  if (Object.keys(errors).length > 0) {
    return { valid: false, errors }
  }

  return {
    valid: true,
    command: {
      title,
      startsAt: new Date(values.startsAt).toISOString(),
      endsAt: new Date(values.endsAt).toISOString(),
      location,
    },
  }
}
```

The field rules and relationship rule are distinct:

- Title and location must contain non-whitespace text and stay within 120 characters.
- Each timestamp must be present and parseable.
- Once both timestamps parse, the end must be strictly later than the start.

The range check does not replace field errors when a timestamp is missing or invalid. That avoids showing “end must be after start” when the real problem is that no usable start exists.

Normalization happens only after the error collection is empty. Trimming during every keystroke would alter the user's editing experience, while trimming after validation produces a clean command. `datetime-local` values do not contain a time-zone offset, so `new Date(...)` interprets them in the device's local zone before `toISOString()` produces an unambiguous instant.

## Build the Controlled Form

Create `src/features/schedule/ServiceEventForm.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import {
  parseServiceEventForm,
  type CreateServiceEventCommand,
  type ServiceEventFormErrors,
  type ServiceEventFormValues,
} from './createServiceEvent'

type ServiceEventFormProps = {
  onSubmit: (command: CreateServiceEventCommand) => void
}

const emptyValues: ServiceEventFormValues = {
  title: '',
  startsAt: '',
  endsAt: '',
  location: '',
}

const deviceTimeZone =
  Intl.DateTimeFormat().resolvedOptions().timeZone || 'your device time zone'

export function ServiceEventForm({ onSubmit }: ServiceEventFormProps) {
  const [values, setValues] = useState<ServiceEventFormValues>(emptyValues)
  const [errors, setErrors] = useState<ServiceEventFormErrors>({})
  const [hasSubmitted, setHasSubmitted] = useState(false)

  function updateField(field: keyof ServiceEventFormValues, value: string) {
    const nextValues = { ...values, [field]: value }
    setValues(nextValues)

    if (hasSubmitted) {
      const result = parseServiceEventForm(nextValues)
      setErrors(result.valid ? {} : result.errors)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setHasSubmitted(true)

    const result = parseServiceEventForm(values)
    if (!result.valid) {
      setErrors(result.errors)
      return
    }

    onSubmit(result.command)
    setValues(emptyValues)
    setErrors({})
    setHasSubmitted(false)
  }

  const hasErrors = Object.keys(errors).length > 0
  const timeRangeError = errors.timeRange

  return (
    <section className="event-form-panel" aria-labelledby="event-form-heading">
      <div className="form-heading">
        <p className="eyebrow">New event</p>
        <h2 id="event-form-heading">Create a service</h2>
        <p>
          Enter the service details now. Staffing positions can be added later.
        </p>
      </div>

      <form className="service-event-form" noValidate onSubmit={handleSubmit}>
        {hasSubmitted && hasErrors && (
          <p className="form-error-summary field-wide" role="alert">
            Review the highlighted fields and try again.
          </p>
        )}

        <div className="form-field field-wide">
          <label htmlFor="service-title">Service name</label>
          <input
            id="service-title"
            name="title"
            type="text"
            value={values.title}
            maxLength={120}
            autoComplete="off"
            aria-invalid={Boolean(errors.title)}
            aria-describedby={errors.title ? 'service-title-error' : undefined}
            onChange={(event) => updateField('title', event.target.value)}
          />
          {errors.title && (
            <p id="service-title-error" className="field-error">
              {errors.title}
            </p>
          )}
        </div>

        <div className="form-field">
          <label htmlFor="service-start">Starts</label>
          <input
            id="service-start"
            name="startsAt"
            type="datetime-local"
            value={values.startsAt}
            aria-invalid={Boolean(errors.startsAt || timeRangeError)}
            aria-describedby={descriptionIds(
              'service-time-zone',
              errors.startsAt ? 'service-start-error' : undefined,
              timeRangeError ? 'service-time-error' : undefined,
            )}
            onChange={(event) => updateField('startsAt', event.target.value)}
          />
          {errors.startsAt && (
            <p id="service-start-error" className="field-error">
              {errors.startsAt}
            </p>
          )}
        </div>

        <div className="form-field">
          <label htmlFor="service-end">Ends</label>
          <input
            id="service-end"
            name="endsAt"
            type="datetime-local"
            value={values.endsAt}
            aria-invalid={Boolean(errors.endsAt || timeRangeError)}
            aria-describedby={descriptionIds(
              'service-time-zone',
              errors.endsAt ? 'service-end-error' : undefined,
              timeRangeError ? 'service-time-error' : undefined,
            )}
            onChange={(event) => updateField('endsAt', event.target.value)}
          />
          {errors.endsAt && (
            <p id="service-end-error" className="field-error">
              {errors.endsAt}
            </p>
          )}
        </div>

        <p id="service-time-zone" className="field-help field-wide">
          Times use this device&apos;s time zone: {deviceTimeZone}.
        </p>

        {timeRangeError && (
          <p id="service-time-error" className="field-error field-wide">
            {timeRangeError}
          </p>
        )}

        <div className="form-field field-wide">
          <label htmlFor="service-location">Location</label>
          <input
            id="service-location"
            name="location"
            type="text"
            value={values.location}
            maxLength={120}
            autoComplete="off"
            aria-invalid={Boolean(errors.location)}
            aria-describedby={
              errors.location ? 'service-location-error' : undefined
            }
            onChange={(event) => updateField('location', event.target.value)}
          />
          {errors.location && (
            <p id="service-location-error" className="field-error">
              {errors.location}
            </p>
          )}
        </div>

        <div className="form-actions field-wide">
          <button type="submit">Add to schedule</button>
        </div>
      </form>
    </section>
  )
}

function descriptionIds(...ids: Array<string | undefined>): string {
  return ids.filter((id): id is string => Boolean(id)).join(' ')
}
```

Each input is controlled: `value` comes from React state and `onChange` synchronously writes the new DOM value back to that state. The form state is local, so typing does not re-render the event grid through the parent page.

`noValidate` disables the browser's submission blocking because this component owns when and how its messages appear. Native input types and `maxLength` still improve the editing experience, but the pure parser remains the application boundary and can validate values supplied outside normal typing.

`hasSubmitted` keeps errors quiet on the initial render. After the first attempt, changing any field re-runs the parser so corrected errors disappear without requiring another submission.

## Make Validation Accessible

The form uses several related HTML mechanisms:

- Every input has a visible `<label>` connected through `htmlFor` and `id`.
- `aria-invalid` reflects the application's current error state.
- `aria-describedby` associates a control with its exact error and the time-zone note.
- The error summary uses `role="alert"` when an invalid submission makes it appear.
- The submit control is a native `<button type="submit">`.

The cross-field error describes both time inputs. Marking both controls invalid is appropriate because the invalid relationship cannot be fixed without considering the pair.

The alert summary announces that submission failed without making every individual message an assertive live region. A user can then navigate the labeled controls and hear the associated detail.

## Integrate the Form With the Schedule

Add these imports to `SchedulePage.tsx`:

```tsx
import { ServiceEventForm } from './ServiceEventForm'
import type { CreateServiceEventCommand } from './createServiceEvent'
```

Extend the existing props and default ID factory:

```tsx
type SchedulePageProps = {
  initialState?: ScheduleState
  eventIdFactory?: () => string
}

const createEventId = () => crypto.randomUUID()
```

Receive the factory and add success-message state:

```tsx
export function SchedulePage({
  initialState = defaultState,
  eventIdFactory = createEventId,
}: SchedulePageProps) {
  const [state, setState] = useState<ScheduleState>(initialState)
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null)
  const [creationMessage, setCreationMessage] = useState('')
```

Add the creation handler beside `reloadSchedule`:

```tsx
function createServiceEvent(command: CreateServiceEventCommand) {
  if (state.status !== 'ready') {
    return
  }

  const event: ServiceEvent = {
    id: eventIdFactory(),
    ...command,
    requiredPositions: 0,
    assignedPositions: 0,
  }

  setState({ status: 'ready', events: [...state.events, event] })
  setCreationMessage(`${event.title} was added to the schedule.`)
}
```

The guard protects the application boundary even though the form is rendered only for ready state. The parent supplies the ID and default staffing counts because those values were not entered by the user.

The spread expression creates a new event array instead of pushing into the existing readonly collection. The event ID factory defaults to `crypto.randomUUID()` but can be replaced in a test, keeping the integration deterministic.

Clear stale creation feedback inside the existing reload function:

```tsx
function reloadSchedule() {
  setSelectedEventId(null)
  setCreationMessage('')
  setState(defaultState)
}
```

Replace the earlier ready-empty and ready-populated branches with one ready branch:

```tsx
{state.status === 'ready' && (
  <>
    <ServiceEventForm onSubmit={createServiceEvent} />

    {creationMessage && (
      <p className="creation-status" role="status">
        {creationMessage}
      </p>
    )}

    {events.length === 0 ? (
      <section className="state-panel empty-state">
        <h2>No upcoming services</h2>
        <p>Create an event when the next schedule is ready.</p>
      </section>
    ) : (
      <ul className="event-grid">
        {events.map((event) => (
          <li key={event.id}>
            <ServiceEventCard
              event={event}
              onSelect={setSelectedEventId}
            />
          </li>
        ))}
      </ul>
    )}
  </>
)}
```

Placing the form inside the ready branch prevents creation while the schedule is loading or unavailable. It also means an empty schedule is recoverable from the same screen: the coordinator can create the first event directly above the empty-state message.

The success message uses `role="status"`, so assistive technology can announce the update without moving focus. The new event enters the same keyed list as seeded events.

## Add the Form Styles

Add the form rules after `.schedule-introduction` in `schedule.css`:

```css
.event-form-panel {
  display: grid;
  margin-top: 2.5rem;
  padding: clamp(1.25rem, 4vw, 2rem);
  border: 1px solid #c7ded7;
  border-radius: 1.1rem;
  background: #fff;
  box-shadow: 0 0.9rem 2.5rem rgb(16 42 67 / 6%);
  gap: 1.5rem 2rem;
  grid-template-columns: minmax(13rem, 0.7fr) minmax(18rem, 1.3fr);
}

.form-heading .eyebrow {
  margin-bottom: 0.45rem;
}

.form-heading h2 {
  margin: 0;
  color: #102a43;
  font-size: 1.65rem;
}

.form-heading > p:last-child {
  margin: 0.75rem 0 0;
  color: #627d98;
  line-height: 1.6;
}

.service-event-form {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.form-field {
  display: grid;
  align-content: start;
  gap: 0.4rem;
}

.field-wide {
  grid-column: 1 / -1;
}

.form-field label {
  color: #243b53;
  font-weight: 750;
}

.form-field input {
  width: 100%;
  min-height: 2.8rem;
  padding: 0.65rem 0.75rem;
  border: 1px solid #9fb3c8;
  border-radius: 0.55rem;
  background: #fff;
  color: #102a43;
  font: inherit;
}

.form-field input:focus-visible {
  border-color: #087f5b;
  outline: 3px solid #b2f2dc;
  outline-offset: 1px;
}

.form-field input[aria-invalid='true'] {
  border-color: #c92a2a;
}

.field-help,
.field-error,
.form-error-summary {
  margin: 0;
  font-size: 0.88rem;
  line-height: 1.45;
}

.field-help {
  color: #627d98;
}

.field-error,
.form-error-summary {
  color: #9b2c2c;
  font-weight: 650;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
}

.form-actions button {
  padding: 0.75rem 1.1rem;
  border: 0;
  border-radius: 0.65rem;
  background: #087f5b;
  color: #fff;
  cursor: pointer;
  font: inherit;
  font-weight: 800;
}

.form-actions button:hover {
  background: #066649;
}

.form-actions button:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}

.creation-status {
  margin: 1rem 0 0;
  padding: 0.85rem 1rem;
  border-left: 4px solid #087f5b;
  background: #e6fcf5;
  color: #245448;
  line-height: 1.5;
}

@media (max-width: 46rem) {
  .event-form-panel,
  .service-event-form {
    grid-template-columns: 1fr;
  }
}
```

The wide layout separates the form introduction from its controls. Below 46 rem, both grids collapse to one column. Field errors use text as well as border color, and focus indicators remain visible on both inputs and the submit button.

## Test the Form Behavior

Create `src/features/schedule/ServiceEventForm.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ServiceEventForm } from './ServiceEventForm'

describe('ServiceEventForm', () => {
  it('reveals required errors without submitting an invalid form', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<ServiceEventForm onSubmit={onSubmit} />)

    await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Review the highlighted fields',
    )
    expect(screen.getByLabelText('Service name')).toHaveAttribute(
      'aria-invalid',
      'true',
    )
    expect(screen.getByText('Enter a service name.')).toBeVisible()
  })

  it('rejects whitespace-only names and locations', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<ServiceEventForm onSubmit={onSubmit} />)

    await user.type(screen.getByLabelText('Service name'), '   ')
    await user.type(screen.getByLabelText('Location'), '   ')
    await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Enter a service name.')).toBeVisible()
    expect(screen.getByText('Enter a service location.')).toBeVisible()
  })

  it('rejects an end time that is not after the start time', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<ServiceEventForm onSubmit={onSubmit} />)

    await fillForm(user, {
      startsAt: '2026-11-01T10:00',
      endsAt: '2026-11-01T09:00',
    })
    await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('End time must be after start time.')).toBeVisible()
    expect(screen.getByLabelText('Ends')).toHaveAttribute('aria-invalid', 'true')
  })

  it('trims text and normalizes timestamps before submitting', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<ServiceEventForm onSubmit={onSubmit} />)

    await fillForm(user)
    await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

    expect(onSubmit).toHaveBeenCalledOnce()
    expect(onSubmit).toHaveBeenCalledWith({
      title: 'Prayer Night',
      startsAt: new Date('2026-11-06T19:00').toISOString(),
      endsAt: new Date('2026-11-06T20:00').toISOString(),
      location: 'Chapel',
    })
    expect(screen.getByLabelText('Service name')).toHaveValue('')
  })
})

async function fillForm(
  user: ReturnType<typeof userEvent.setup>,
  times = {
    startsAt: '2026-11-06T19:00',
    endsAt: '2026-11-06T20:00',
  },
) {
  await user.type(screen.getByLabelText('Service name'), '  Prayer Night  ')
  await user.type(screen.getByLabelText('Starts'), times.startsAt)
  await user.type(screen.getByLabelText('Ends'), times.endsAt)
  await user.type(screen.getByLabelText('Location'), '  Chapel  ')
}
```

These tests use the form through labels, button roles, visible messages, and its callback contract. They do not call `handleSubmit` or inspect React state directly.

The valid test calculates expected ISO timestamps with the same local-date semantics as the browser control. It does not hard-code a UTC offset that would fail when the test runs in another time zone.

## Test the Parent Integration

Add one case to `SchedulePage.test.tsx`:

```tsx
it('adds a valid service event to the in-memory schedule', async () => {
  const user = userEvent.setup()
  render(<SchedulePage eventIdFactory={() => 'prayer-night'} />)

  await user.type(screen.getByLabelText('Service name'), '  Prayer Night  ')
  await user.type(screen.getByLabelText('Starts'), '2026-11-06T19:00')
  await user.type(screen.getByLabelText('Ends'), '2026-11-06T20:00')
  await user.type(screen.getByLabelText('Location'), '  Chapel  ')
  await user.click(screen.getByRole('button', { name: 'Add to schedule' }))

  expect(screen.getAllByRole('article')).toHaveLength(3)
  expect(
    screen.getByRole('heading', { name: 'Prayer Night' }),
  ).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent(
    'Prayer Night was added to the schedule.',
  )
})
```

The child tests establish validation and command normalization. This parent test proves the complete boundary once: the submitted command reaches the schedule, a third card renders, and the live-region message names the normalized title.

## Update the Project Notes

Add Part 5 to `README.md`:

```md
## Part 5

The `part_5` branch adds a typed controlled service-event form, pure field and
time-range validation, accessible error feedback, normalized creation commands,
immutable schedule updates, and child-to-parent integration tests.
```

## Run the Part 5 Quality Gate

Run all repository checks:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed Part 5 checkpoint has five test files and twenty-five passing tests:

```text
Test Files  5 passed (5)
Tests       25 passed (25)
```

Run the development server for a browser check:

```bash
npm run dev
```

Open `/schedule`, submit an empty form, correct the highlighted fields, and add a valid service. Confirm that the form clears, a new card appears, and the success status names the service.

## Production Boundaries

Part 5 stores created events only in memory. Refreshing the browser restores the seeded data. A production workflow still needs several server-owned guarantees:

- Repeat required-field, length, and time-range validation at the API boundary.
- Authorize the coordinator before creating an event.
- Define the church's scheduling time zone instead of assuming the device zone.
- Assign persistent IDs on the server and reconcile any temporary client ID.
- Prevent or intentionally handle duplicate submissions.
- Preserve entered values and map server validation failures back to the form.

React 19 supports form Actions, pending state, and server functions, but this checkpoint has no asynchronous server boundary. A conventional `onSubmit` handler keeps the local ownership explicit and works independently of a particular backend design. Later persistence work can introduce pending and failure states where they have real meaning.

React's official documentation explains [controlled inputs](https://react.dev/reference/react-dom/components/input), [form submission with `onSubmit`](https://react.dev/reference/react-dom/components/form), and [choosing one owner for shared state](https://react.dev/learn/sharing-state-between-components).

## Part 5 Checkpoint

ChurchServe now has a visible responsive form whose editing state, validation, and application command are separate concerns. Invalid values stay inside the form, valid values become normalized commands, and the schedule page performs one immutable state update.

Confirm that:

- Every input is labeled and remains controlled from its first render.
- Empty, whitespace-only, invalid date, and reversed time values cannot submit.
- Error messages are associated with their controls and an alert announces failure.
- Valid text is trimmed and local date-time strings become ISO timestamps.
- The form is available only when the schedule is ready.
- A valid submission creates a new card and announces success.
- All twenty-five tests and the production build pass.

Part 6 will add parameterized event-detail routes, navigation from schedule cards, and a not-found experience while reusing the event and form boundaries.
