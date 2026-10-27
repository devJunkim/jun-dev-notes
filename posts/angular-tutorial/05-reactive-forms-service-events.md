---
title: "ChurchServe Angular Tutorial Part 5: Typed Reactive Forms and Validation"
excerpt: "Create ChurchServe service events with typed reactive forms, cross-field validation, accessible errors, normalized commands, and focused tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular typed reactive forms tutorial"
  description: "Build typed Angular reactive forms with field and cross-field validation, accessible messages, normalized commands, and focused tests."
  socialTitle: "ChurchServe Part 5: Typed Reactive Forms"
  socialDescription: "Create valid service events with typed controls, time-range rules, accessible errors, normalized commands, and tested submission behavior."
---

# ChurchServe Angular Tutorial Part 5: Typed Reactive Forms and Validation

ChurchServe can display its schedule and model volunteer assignments, but coordinators still cannot create a service event. That workflow needs a defined command boundary, field validation, a rule spanning two controls, accessible feedback, and a parent component that decides what a successful command does.

Part 5 adds that complete path. The form owns editing and validation. It emits a normalized command only when valid. The schedule receives that command and updates its in-memory signal state immutably. Persistence remains out of scope until the repository and API parts of the series.

> **Quick answer:** Build a non-nullable typed form, keep field and cross-field rules separate, reveal errors after interaction or submission, and translate valid values into a normalized command before application state changes.

## Add the Part 5 Files

Continue from Part 4 and add this structure:

```text
src/app/features/schedule/
  create-service-event-command.ts
  service-event-form/
    service-event-form.html
    service-event-form.scss
    service-event-form.spec.ts
    service-event-form.ts
```

The existing schedule remains the workflow owner. The child form never reaches into schedule state directly.

## Define the Creation Command

Create `src/app/features/schedule/create-service-event-command.ts`:

```typescript
export interface CreateServiceEventCommand {
  readonly title: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly location: string;
}
```

The command excludes server-generated identity and staffing summaries. It is also distinct from raw form data: its text is trimmed and its timestamps are normalized.

## Build the Complete Form Component

Create `src/app/features/schedule/service-event-form/service-event-form.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { CreateServiceEventCommand } from '../create-service-event-command';

@Component({
  selector: 'app-service-event-form',
  imports: [ReactiveFormsModule],
  templateUrl: './service-event-form.html',
  styleUrl: './service-event-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventForm {
  private readonly formBuilder = inject(FormBuilder);

  readonly submitted = output<CreateServiceEventCommand>();

  protected readonly deviceTimeZone =
    Intl.DateTimeFormat().resolvedOptions().timeZone || 'your device time zone';

  readonly form = this.formBuilder.nonNullable.group(
    {
      title: ['', [Validators.required, notBlank, Validators.maxLength(120)]],
      startsAt: ['', [Validators.required, validDateTime]],
      endsAt: ['', [Validators.required, validDateTime]],
      location: ['', [Validators.required, notBlank, Validators.maxLength(120)]],
    },
    { validators: [validTimeRange] },
  );

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    this.submitted.emit({
      title: value.title.trim(),
      startsAt: new Date(value.startsAt).toISOString(),
      endsAt: new Date(value.endsAt).toISOString(),
      location: value.location.trim(),
    });
  }
}

export function validTimeRange(control: AbstractControl): ValidationErrors | null {
  const start = Date.parse(control.get('startsAt')?.value ?? '');
  const end = Date.parse(control.get('endsAt')?.value ?? '');

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return null;
  }

  return end > start ? null : { timeRange: true };
}

function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  if (control.value.length === 0) {
    return null;
  }

  return /\S/.test(control.value) ? null : { blank: true };
}

function validDateTime(control: AbstractControl<string>): ValidationErrors | null {
  if (control.value.length === 0) {
    return null;
  }

  return Number.isNaN(Date.parse(control.value)) ? { dateTime: true } : null;
}
```

`nonNullable.group` gives each control a `string` value rather than `string | null`; it does not make the values valid. `required`, `notBlank`, `maxLength`, and `validDateTime` own individual controls. The group-level `validTimeRange` owns the relationship between the two date controls.

The whitespace validator matters because Angular's `required` validator accepts a non-empty string of spaces. Without it, submission could validate successfully and then trim the command field to an empty string.

The range validator returns `null` when either timestamp cannot be parsed. Field validators already report those errors. Once both values parse, the end must be strictly later than the start.

`markAllAsTouched` reveals useful errors after an attempted submit. The early return prevents invalid output. Only the valid boundary trims text and converts browser-local values to ISO timestamps.

## Add the Accessible Template

Create `src/app/features/schedule/service-event-form/service-event-form.html`:

```html
<section class="event-form-panel" aria-labelledby="event-form-heading">
  <div class="form-heading">
    <p class="eyebrow">New event</p>
    <h2 id="event-form-heading">Create a service</h2>
    <p>Enter the service details now. Staffing positions can be added in a later step.</p>
  </div>

  <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
    <div class="field field-wide">
      <label for="service-title">Service name</label>
      <input
        id="service-title"
        type="text"
        formControlName="title"
        maxlength="120"
        autocomplete="off"
        aria-describedby="service-title-error"
        [attr.aria-invalid]="form.controls.title.touched && form.controls.title.invalid"
      />
      @if (form.controls.title.touched && form.controls.title.invalid) {
        <p id="service-title-error" class="field-error">Enter a service name.</p>
      }
    </div>

    <div class="field">
      <label for="service-start">Starts</label>
      <input
        id="service-start"
        type="datetime-local"
        formControlName="startsAt"
        aria-describedby="service-time-zone service-time-error"
        [attr.aria-invalid]="form.controls.startsAt.touched && form.controls.startsAt.invalid"
      />
      @if (form.controls.startsAt.touched && form.controls.startsAt.invalid) {
        <p class="field-error">Enter a valid start date and time.</p>
      }
    </div>

    <div class="field">
      <label for="service-end">Ends</label>
      <input
        id="service-end"
        type="datetime-local"
        formControlName="endsAt"
        aria-describedby="service-time-zone service-time-error"
        [attr.aria-invalid]="form.controls.endsAt.touched && form.controls.endsAt.invalid"
      />
      @if (form.controls.endsAt.touched && form.controls.endsAt.invalid) {
        <p class="field-error">Enter a valid end date and time.</p>
      }
    </div>

    <p id="service-time-zone" class="field-help field-wide">
      Times are interpreted in this device's time zone: {{ deviceTimeZone }}.
    </p>

    @if (
      (form.controls.startsAt.touched || form.controls.endsAt.touched) &&
      form.hasError('timeRange')
    ) {
      <p id="service-time-error" class="field-error field-wide" role="alert">
        End time must be after start time.
      </p>
    }

    <div class="field field-wide">
      <label for="service-location">Location</label>
      <input
        id="service-location"
        type="text"
        formControlName="location"
        maxlength="120"
        autocomplete="off"
        aria-describedby="service-location-error"
        [attr.aria-invalid]="form.controls.location.touched && form.controls.location.invalid"
      />
      @if (form.controls.location.touched && form.controls.location.invalid) {
        <p id="service-location-error" class="field-error">Enter a service location.</p>
      }
    </div>

    <div class="form-actions field-wide">
      <button type="submit">Add to schedule</button>
    </div>
  </form>
</section>
```

Every control has an explicit label. `aria-invalid` follows interaction and validity, while `aria-describedby` connects supporting content. The cross-field message uses `role="alert"` when it appears. Errors are absent on initial render and appear after interaction or submission.

The time-zone note is deliberately honest: this local stage interprets `datetime-local` through the device zone. A production API must receive the intended scheduling zone, revalidate the range, and persist unambiguous instants.

## Style the Form

Create `src/app/features/schedule/service-event-form/service-event-form.scss`:

```scss
:host {
  display: block;
  margin-top: 2.5rem;
}

.event-form-panel {
  display: grid;
  padding: clamp(1.25rem, 4vw, 2rem);
  border: 1px solid #c7ded7;
  border-radius: 1.1rem;
  background: #fff;
  box-shadow: 0 0.9rem 2.5rem rgba(16, 42, 67, 0.06);
  gap: 1.5rem 2rem;
  grid-template-columns: minmax(13rem, 0.7fr) minmax(18rem, 1.3fr);
}

.form-heading .eyebrow {
  margin: 0 0 0.45rem;
  color: #087f5b;
  font-size: 0.76rem;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
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

form {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.field {
  display: grid;
  align-content: start;
  gap: 0.4rem;
}

.field-wide {
  grid-column: 1 / -1;
}

label {
  color: #243b53;
  font-weight: 750;
}

input {
  width: 100%;
  min-height: 2.8rem;
  padding: 0.65rem 0.75rem;
  border: 1px solid #9fb3c8;
  border-radius: 0.55rem;
  color: #102a43;
  background: #fff;
}

input:focus-visible {
  border-color: #087f5b;
  outline: 3px solid #b2f2dc;
  outline-offset: 1px;
}

input[aria-invalid='true'] {
  border-color: #c92a2a;
}

.field-help,
.field-error {
  margin: 0;
  font-size: 0.88rem;
  line-height: 1.45;
}

.field-help {
  color: #627d98;
}

.field-error {
  color: #9b2c2c;
  font-weight: 650;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
}

button {
  padding: 0.75rem 1.1rem;
  border: 0;
  border-radius: 0.65rem;
  background: #087f5b;
  color: #fff;
  cursor: pointer;
  font-weight: 800;
}

button:hover {
  background: #066649;
}

button:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}

@media (max-width: 46rem) {
  .event-form-panel,
  form {
    grid-template-columns: 1fr;
  }

  .field-wide {
    grid-column: auto;
  }
}
```

The two-column desktop layout becomes one column on smaller screens. Visible focus rings, text errors, and invalid borders provide complementary feedback.

## Connect the Form to the Schedule

Import the command and component in `schedule.ts`, then add the component to `imports`:

```typescript
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEventForm } from './service-event-form/service-event-form';

@Component({
  // Existing metadata remains unchanged.
  imports: [ServiceEventCard, ServiceEventForm],
})
```

Add the success signal and command handler to `Schedule`:

```typescript
protected readonly submissionMessage = signal('');

protected createEvent(command: CreateServiceEventCommand): void {
  const current = this.state();
  if (current.status !== 'ready') {
    return;
  }

  const event: ServiceEvent = {
    id: crypto.randomUUID(),
    ...command,
    requiredPositions: 0,
    assignedPositions: 0,
  };

  this.state.set({ status: 'ready', events: [...current.events, event] });
  this.submissionMessage.set(`${event.title} was added to the schedule.`);
}
```

Also clear `submissionMessage` inside the existing `replaceState` method. The handler receives a command rather than a form, adds the fields owned by the application, and creates a new event array. The ready-state guard protects the discriminated union even if another caller invokes the method later.

Inside the ready branch of `schedule.html`, render the form before the existing event grid:

```html
<app-service-event-form (submitted)="createEvent($event)" />

@if (submissionMessage()) {
  <p class="creation-status" role="status">{{ submissionMessage() }}</p>
}
```

Add this block to `schedule.scss`:

```scss
.creation-status {
  margin: 1rem 0 0;
  padding: 0.85rem 1rem;
  border-left: 4px solid #087f5b;
  background: #e6fcf5;
  color: #245448;
  line-height: 1.5;
}
```

Rendering only in the ready branch prevents submission while data is loading or unavailable. The status region announces success without moving focus, and the new event appears in the existing tracked grid. Refreshing still removes it; persistence arrives in later parts.

## Add the Complete Form Tests

Create `src/app/features/schedule/service-event-form/service-event-form.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { ServiceEventForm } from './service-event-form';

describe('ServiceEventForm', () => {
  it('rejects an end time before the start time', () => {
    const fixture = TestBed.createComponent(ServiceEventForm);
    const form = fixture.componentInstance.form;

    form.setValue({
      title: 'Sunday Service',
      startsAt: '2026-11-01T10:00',
      endsAt: '2026-11-01T09:00',
      location: 'Main Sanctuary',
    });

    expect(form.hasError('timeRange')).toBe(true);
  });

  it('does not emit an invalid command and reveals validation errors', () => {
    const fixture = TestBed.createComponent(ServiceEventForm);
    const emitted: CreateServiceEventCommand[] = [];
    fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.form.controls.title.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Enter a service name.');
  });

  it('treats whitespace-only names and locations as invalid', () => {
    const fixture = TestBed.createComponent(ServiceEventForm);
    const form = fixture.componentInstance.form;

    form.setValue({
      title: '   ',
      startsAt: '2026-11-01T10:00',
      endsAt: '2026-11-01T11:00',
      location: '\t',
    });

    expect(form.controls.title.hasError('blank')).toBe(true);
    expect(form.controls.location.hasError('blank')).toBe(true);
    expect(form.invalid).toBe(true);
  });

  it('trims text and normalizes timestamps before emitting a valid command', () => {
    const fixture = TestBed.createComponent(ServiceEventForm);
    const emitted: CreateServiceEventCommand[] = [];
    fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));
    fixture.componentInstance.form.setValue({
      title: '  Sunday Service  ',
      startsAt: '2026-11-01T10:00',
      endsAt: '2026-11-01T11:30',
      location: '  Main Sanctuary  ',
    });

    fixture.componentInstance.submit();

    expect(emitted).toEqual([
      {
        title: 'Sunday Service',
        startsAt: new Date('2026-11-01T10:00').toISOString(),
        endsAt: new Date('2026-11-01T11:30').toISOString(),
        location: 'Main Sanctuary',
      },
    ]);
  });
});
```

These tests cover ChurchServe behavior rather than Angular's validator implementation: range rules, no invalid emission, whitespace rejection, error visibility, trimming, and timestamp normalization.

Add one parent-boundary test to the existing `schedule.spec.ts`:

```typescript
it('adds a valid creation command to the in-memory schedule', () => {
  const fixture = TestBed.createComponent(Schedule);
  fixture.detectChanges();
  const form = fixture.debugElement.query(By.directive(ServiceEventForm))
    .componentInstance as ServiceEventForm;

  form.form.setValue({
    title: '  Prayer Night  ',
    startsAt: '2026-11-06T19:00',
    endsAt: '2026-11-06T20:00',
    location: '  Chapel  ',
  });
  form.submit();
  fixture.detectChanges();

  const element: HTMLElement = fixture.nativeElement;
  expect(element.querySelectorAll('app-service-event-card')).toHaveLength(3);
  expect(element.querySelector('.creation-status')?.textContent).toContain(
    'Prayer Night was added to the schedule.',
  );
});
```

Import `By` from `@angular/platform-browser` and `ServiceEventForm` in that spec. The component tests isolate form behavior; this test covers the output-to-parent integration once.

## Complete Final Schedule Files

The earlier snippets identify the Part 5 changes, but a tutorial should also give you a complete copyable result. These are the final schedule files after integrating the form.

### Complete Schedule Component

`src/app/features/schedule/schedule.ts` now imports the command and form, receives valid submissions, assigns the temporary client ID, and appends a new immutable event array:

```typescript
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEventCard } from './service-event-card/service-event-card';
import { ServiceEventForm } from './service-event-form/service-event-form';
import { ServiceEvent } from './service-event';

export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; events: readonly ServiceEvent[] };

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
];

@Component({
  selector: 'app-schedule',
  imports: [ServiceEventCard, ServiceEventForm],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Schedule {
  protected readonly state = signal<ScheduleState>({ status: 'loading' });

  private readonly selectedEventId = signal<string | null>(null);

  protected readonly submissionMessage = signal('');

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.status === 'error' ? current.message : '';
  });

  protected readonly events = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? current.events : [];
  });

  protected readonly selectedEvent = computed(() => {
    return this.events().find((event) => event.id === this.selectedEventId());
  });

  constructor() {
    this.reload();
  }

  protected selectEvent(id: string): void {
    this.selectedEventId.set(id);
  }

  protected createEvent(command: CreateServiceEventCommand): void {
    const current = this.state();
    if (current.status !== 'ready') {
      return;
    }

    const event: ServiceEvent = {
      id: crypto.randomUUID(),
      ...command,
      requiredPositions: 0,
      assignedPositions: 0,
    };

    this.state.set({ status: 'ready', events: [...current.events, event] });
    this.submissionMessage.set(`${event.title} was added to the schedule.`);
  }

  protected reload(): void {
    this.replaceState({ status: 'ready', events: scheduledEvents });
  }

  setStateForTest(state: ScheduleState): void {
    this.replaceState(state);
  }

  private replaceState(state: ScheduleState): void {
    this.selectedEventId.set(null);
    this.submissionMessage.set('');
    this.state.set(state);
  }
}
```

The child owns form mechanics; the parent owns application state. The spread expression produces a new event array so signal consumers observe the update without mutation. `replaceState` clears the success message because a reload, error, or test-state replacement makes an earlier creation announcement stale.

### Complete Schedule Template

`src/app/features/schedule/schedule.html` preserves the loading, error, ready, empty, and selection states from Part 3. The form exists only inside the ready branch:

```html
<section class="schedule-page" aria-labelledby="schedule-heading">
  <p class="eyebrow">Schedule</p>
  <h1 id="schedule-heading">Upcoming services</h1>
  <p class="introduction">
    Review each event's time, location, and current volunteer coverage. Detailed assignments arrive
    in later parts of the tutorial.
  </p>

  @switch (state().status) {
    @case ('loading') {
      <p class="state-panel" role="status">Loading service schedule…</p>
    }

    @case ('error') {
      <section class="state-panel error-state" role="alert">
        <h2>Schedule unavailable</h2>
        <p>{{ errorMessage() }}</p>
        <button type="button" (click)="reload()">Try again</button>
      </section>
    }

    @case ('ready') {
      <app-service-event-form (submitted)="createEvent($event)" />

      @if (submissionMessage()) {
        <p class="creation-status" role="status">{{ submissionMessage() }}</p>
      }

      <div class="event-grid">
        @for (event of events(); track event.id) {
          <app-service-event-card [event]="event" (selected)="selectEvent($event)" />
        } @empty {
          <section class="state-panel empty-state">
            <h2>No upcoming services</h2>
            <p>Create an event when the next schedule is ready.</p>
          </section>
        }
      </div>
    }
  }

  @if (selectedEvent(); as event) {
    <p class="selection-status" role="status">
      <strong>{{ event.title }}</strong> selected. Detailed scheduling will be added in a later
      part.
    </p>
  }
</section>
```

The ready-only placement is more than presentation. It prevents creation while the schedule is loading or unavailable. The success status is adjacent to the form, while the newly created card is rendered through the same keyed loop as seeded events.

### Complete Schedule Styles

`src/app/features/schedule/schedule.scss` adds the creation status without removing any earlier state styling:

```scss
:host {
  display: block;
}

.schedule-page {
  max-width: 76rem;
  margin: 0 auto;
  padding: clamp(3.5rem, 8vw, 6rem) clamp(1.25rem, 5vw, 4rem);
}

.eyebrow {
  margin: 0 0 0.8rem;
  color: #087f5b;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

h1 {
  margin: 0;
  color: #102a43;
  font-size: clamp(2.4rem, 6vw, 4.25rem);
  letter-spacing: -0.045em;
  line-height: 1;
}

.introduction {
  max-width: 44rem;
  margin: 1.25rem 0 0;
  color: #52667a;
  font-size: 1.1rem;
  line-height: 1.7;
}

.event-grid {
  display: grid;
  margin-top: 2.5rem;
  gap: 1rem;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
}

.creation-status {
  margin: 1rem 0 0;
  padding: 0.85rem 1rem;
  border-left: 4px solid #087f5b;
  background: #e6fcf5;
  color: #245448;
  line-height: 1.5;
}

.state-panel {
  margin: 2.5rem 0 0;
  padding: 1.25rem;
  border: 1px solid #bcccdc;
  border-radius: 0.85rem;
  background: #f6f8fa;
  color: #334e68;
  line-height: 1.6;
}

.state-panel h2,
.state-panel p {
  margin: 0;
}

.state-panel p + button {
  margin-top: 1rem;
}

.error-state {
  border-color: #e6a5a5;
  background: #fff5f5;
  color: #7f1d1d;
}

.error-state button {
  padding: 0.65rem 1rem;
  border: 0;
  border-radius: 0.55rem;
  background: #9b2c2c;
  color: #fff;
  cursor: pointer;
  font: inherit;
  font-weight: 700;
}

.error-state button:hover {
  background: #7f1d1d;
}

.error-state button:focus-visible {
  outline: 3px solid #fdba74;
  outline-offset: 3px;
}

.empty-state {
  grid-column: 1 / -1;
  text-align: center;
}

.empty-state h2 {
  color: #102a43;
}

.empty-state p {
  margin-top: 0.35rem;
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

The success treatment matches the existing selection treatment, so feedback feels like part of the same interface rather than a separate notification system.

### Complete Schedule Tests

`src/app/features/schedule/schedule.spec.ts` retains every earlier state test and adds the Part 5 integration case:

```typescript
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Schedule } from './schedule';
import { ServiceEvent } from './service-event';
import { ServiceEventForm } from './service-event-form/service-event-form';

describe('Schedule', () => {
  it('renders the event collection and owns card selection', () => {
    const fixture = TestBed.createComponent(Schedule);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelectorAll('app-service-event-card')).toHaveLength(2);

    element.querySelector<HTMLButtonElement>('app-service-event-card button')?.click();
    fixture.detectChanges();

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Sunday Morning Service selected',
    );
  });

  it('shows a loading status without an empty-state message', () => {
    const fixture = TestBed.createComponent(Schedule);
    fixture.componentInstance.setStateForTest({ status: 'loading' });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Loading service schedule',
    );
    expect(element.textContent).not.toContain('No upcoming services');
    expect(element.querySelector('app-service-event-card')).toBeNull();
  });

  it('shows an error and reloads the seeded schedule', () => {
    const fixture = TestBed.createComponent(Schedule);
    fixture.componentInstance.setStateForTest({
      status: 'error',
      message: 'Please try again.',
    });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Please try again.');
    expect(element.textContent).not.toContain('No upcoming services');

    element.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')).toBeNull();
    expect(element.querySelectorAll('app-service-event-card')).toHaveLength(2);
  });

  it('shows the ready empty state only after loading succeeds', () => {
    const fixture = TestBed.createComponent(Schedule);
    fixture.componentInstance.setStateForTest({ status: 'ready', events: [] });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('No upcoming services');
    expect(element.querySelector('[role="status"]')).toBeNull();
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps each card associated with its event when events are reordered', () => {
    const fixture = TestBed.createComponent(Schedule);
    const first: ServiceEvent = {
      id: 'event-1',
      title: 'First Service',
      startsAt: '2026-11-01T10:00:00-05:00',
      endsAt: '2026-11-01T11:00:00-05:00',
      location: 'Main Sanctuary',
      requiredPositions: 4,
      assignedPositions: 2,
    };
    const second: ServiceEvent = {
      ...first,
      id: 'event-2',
      title: 'Second Service',
    };

    fixture.componentInstance.setStateForTest({ status: 'ready', events: [first, second] });
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const cardsBefore = Array.from(element.querySelectorAll('app-service-event-card'));

    fixture.componentInstance.setStateForTest({ status: 'ready', events: [second, first] });
    fixture.detectChanges();
    const cardsAfter = Array.from(element.querySelectorAll('app-service-event-card'));

    expect(cardsAfter[0]).toBe(cardsBefore[1]);
    expect(cardsAfter[1]).toBe(cardsBefore[0]);
  });

  it('adds a valid creation command to the in-memory schedule', () => {
    const fixture = TestBed.createComponent(Schedule);
    fixture.detectChanges();
    const form = fixture.debugElement.query(By.directive(ServiceEventForm))
      .componentInstance as ServiceEventForm;

    form.form.setValue({
      title: '  Prayer Night  ',
      startsAt: '2026-11-06T19:00',
      endsAt: '2026-11-06T20:00',
      location: '  Chapel  ',
    });
    form.submit();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelectorAll('app-service-event-card')).toHaveLength(3);
    expect(element.querySelector('.creation-status')?.textContent).toContain(
      'Prayer Night was added to the schedule.',
    );
  });
});
```

The final case intentionally locates the real child component rather than calling the parent's protected handler. It proves the form output crosses the component boundary, the normalized title reaches the parent, the new card renders, and the live-region message is present. Earlier tests remain because Part 5 must not regress loading, failure, empty, selection, or keyed-list behavior.

## Update the Visual Studio Solution

Add the command file to the schedule folder and the four form files to a new solution folder:

```xml
<Folder Name="/src/app/features/schedule/">
  <File Path="src\app\features\schedule\create-service-event-command.ts" />
  <!-- Existing schedule files remain here. -->
</Folder>
<Folder Name="/src/app/features/schedule/service-event-form/">
  <File Path="src\app\features\schedule\service-event-form\service-event-form.html" />
  <File Path="src\app\features\schedule\service-event-form\service-event-form.scss" />
  <File Path="src\app\features\schedule\service-event-form\service-event-form.spec.ts" />
  <File Path="src\app\features\schedule\service-event-form\service-event-form.ts" />
</Folder>
```

The `.slnx` entries affect Visual Studio organization only. Angular discovers files through imports and TypeScript configuration.

## Run the Part 5 Checks

```bash
npm test -- --watch=false
npm run build
```

The completed repository has six test files and twenty-five passing tests. The production build verifies TypeScript, reactive-form bindings, control-flow templates, and the lazy schedule route together.

## Production Boundaries

This stage intentionally stores new events only in memory. A future API must repeat required-field and range validation, authorize the coordinator, assign persistent IDs, and define the church scheduling time zone. Client validation improves feedback but cannot enforce integrity against another client or a direct HTTP request.

Concurrent creation and server errors are also deferred. When persistence arrives, failures must leave the form recoverable, duplicate submissions need a policy, and server validation messages need an explicit mapping back to the page.

## Part 5 Checkpoint

ChurchServe now has a visible responsive form, non-nullable typed controls, field and cross-field validation, accessible feedback, normalized commands, immutable schedule integration, and tests across the child and parent boundaries.

Part 6 will add parameterized detail and edit routes, navigation, and a not-found experience while reusing these event and form boundaries.
