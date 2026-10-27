---
title: "ChurchServe Angular Tutorial Part 3: Control Flow and Schedule States"
excerpt: "Use Angular @if, @for, @switch, and @empty to represent loading, error, empty, and populated ChurchServe schedules without ambiguous UI states."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular control flow tutorial"
  description: "Use Angular built-in control flow to model loading, error, empty, and populated schedule states with stable list tracking and focused tests."
  socialTitle: "ChurchServe Part 3: Angular Control Flow"
  socialDescription: "Build predictable schedule states with @if, @for, @switch, @empty, stable identity, and unit tests."
---

# ChurchServe Angular Tutorial Part 3: Control Flow and Schedule States

A schedule page needs more than a loop. It must distinguish loading from an empty schedule, report failures without pretending there are no events, and preserve event-card identity when filters or sorting change.

Part 2 left ChurchServe with two useful event cards, but the page assumes that its data is always available. Real schedule data will eventually arrive from an API. Before adding HTTP, this part gives the page an explicit request-state model and renders every outcome deliberately. Keeping the data local for one more part lets us focus on Angular control flow without mixing it with network behavior.

You will replace the always-present event array with a discriminated union, render loading, error, empty, and populated states, preserve the existing card-selection interaction, and add tests that prove the states cannot appear at the same time. You will also verify that `track event.id` preserves each card's DOM identity when the collection is reordered.

> **Quick answer:** Model the page state explicitly, use `@switch` for mutually exclusive request states, use `@for` with a stable event ID, and reserve `@empty` for a successfully loaded collection that genuinely contains no results.

## Continue From Part 2

Start with the completed Part 2 application. This part changes the existing schedule component, template, stylesheet, and test file:

```text
src/app/features/schedule/
  schedule.html
  schedule.scss
  schedule.spec.ts
  schedule.ts
```

No new package is required. The `ServiceEvent` model and `ServiceEventCard` component from Part 2 remain unchanged.

## Avoid Ambiguous Booleans

Two flags such as `loading` and `hasError` can produce impossible combinations. Represent the mutually exclusive state as a discriminated union:

```typescript
import { ServiceEvent } from './service-event';

export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; events: readonly ServiceEvent[] };
```

The type prevents a “loading with ready events and an error” state. A value is loading, failed, or ready; it cannot be all three. Later, an API service can produce the same union from an HTTP request.

## Replace the Schedule Component

Replace `src/app/features/schedule/schedule.ts` with the complete Part 3 component:

```typescript
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ServiceEventCard } from './service-event-card/service-event-card';
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
  imports: [ServiceEventCard],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Schedule {
  protected readonly state = signal<ScheduleState>({ status: 'loading' });

  private readonly selectedEventId = signal<string | null>(null);

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

  protected reload(): void {
    this.replaceState({ status: 'ready', events: scheduledEvents });
  }

  setStateForTest(state: ScheduleState): void {
    this.replaceState(state);
  }

  private replaceState(state: ScheduleState): void {
    this.selectedEventId.set(null);
    this.state.set(state);
  }
}
```

The `events` and `errorMessage` computed signals narrow the union in TypeScript before the template reads it. Angular's template checker does not preserve narrowing across repeated calls such as `state().status` and `state().events`, so these selectors keep the template strict and compilable.

`replaceState` also clears the selected event. Without that reset, a selection message from the previous ready state could remain visible after a reload or failure.

The constructor replaces the initial loading state immediately because Part 3 still uses local data. Do not add an artificial timer merely to make the loading message flash. The tests set loading and error states directly and deterministically. When a later part introduces a real API request, loading will remain active until that request resolves.

## Render Every State Intentionally

Replace `src/app/features/schedule/schedule.html`:

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

`@switch` owns the mutually exclusive request states. `@empty` belongs inside the ready branch because an empty successful response is different from a request that has not completed or has failed.

The loading text uses `role="status"`, while the failure uses `role="alert"`. Alerts should be reserved for information requiring timely attention; using them for ordinary loading creates noisy assistive-technology output. The retry button calls the same `reload` method used during initialization.

## Style the State Panels

Keep the Part 2 styles in `src/app/features/schedule/schedule.scss`, then add these rules after `.event-grid`:

```scss
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
```

The empty panel spans the grid so it remains centered instead of occupying one card-sized column. The retry button keeps a visible keyboard focus indicator rather than relying on its hover treatment.

## Understand Tracking Identity

`track event.id` tells Angular that the same ID represents the same logical event across updates. When events are reordered or one is inserted at the beginning, Angular can move existing card instances instead of associating their DOM and local state with different records.

Avoid `$index` for this schedule. Events will be sorted, filtered, added, and removed. Index tracking can associate a stateful child control or keyboard focus with a different event after a reorder.

IDs must be stable and unique among siblings. If a backend later returns duplicate IDs, reject that invalid response in the data-mapping layer instead of allowing unpredictable rendering.

## Test the User-Visible States

Replace `src/app/features/schedule/schedule.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { Schedule } from './schedule';
import { ServiceEvent } from './service-event';

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
});
```

The first four cases test user-visible behavior rather than Angular's syntax. They prove that loading, error, empty, and populated content do not overlap, and that retry returns to the seeded collection.

`setStateForTest` is a narrow testing seam while the schedule still owns local data. Production components should not normally expose arbitrary state mutation. A later data source can provide these states through a replaceable dependency, at which point the test seam can be removed.

The reorder test compares the actual card host elements before and after reversing the collection. It will fail if the loop tracks by `$index`, even though a simpler text assertion might still pass.

## Optional Extension: Filtered Empty Results

Part 3 deliberately stops at the unfiltered schedule. When a later screen adds filters, distinguish “there are no upcoming events” from “there are events, but none match this filter.” Derive the filtered list without overwriting the source collection:

```typescript
protected readonly selectedLocation = signal<string>('all');

protected readonly visibleEvents = computed(() => {
  const location = this.selectedLocation();
  return location === 'all'
    ? this.events()
    : this.events().filter((event) => event.location === location);
});
```

The template can then loop over `visibleEvents()` and give its filtered-empty state a specific message. Keeping this optional prevents filtering concerns from obscuring the control-flow checkpoint.

## Keep the Development URL Predictable

The completed repository runs the Angular development server on port 4201. Under the `serve` target in `angular.json`, add the `options` property if you want the same default:

```json
"serve": {
  "builder": "@angular/build:dev-server",
  "options": {
    "port": 4201
  },
  "configurations": {
    "production": {
      "buildTarget": "churchserve:build:production"
    },
    "development": {
      "buildTarget": "churchserve:build:development"
    }
  },
  "defaultConfiguration": "development"
}
```

This is a workspace convenience rather than part of the state model. If you keep Angular's default port, use the local URL printed by `npm start` instead.

## Run the Tests and Production Build

Run the complete unit suite once:

```bash
npm test -- --watch=false
```

The completed Part 3 repository has four test files and eleven tests. The exact count may be higher if you added your own coverage.

Then verify strict TypeScript, Angular templates, component styles, and the optimized bundles:

```bash
npm run build
```

Finally, start the development server:

```bash
npm start
```

Open `http://localhost:4201/schedule`, or use the URL printed by Angular if you did not configure port 4201. The default local data immediately renders the two Part 2 cards. Loading, failure, and empty responses are exercised deterministically by the tests until a later part introduces a real asynchronous data source.

## Part 3 Checkpoint

ChurchServe now represents schedule request state honestly, renders each outcome with accessible semantics, preserves the Part 2 selection behavior, and keeps card identity stable across collection changes.

Part 4 will introduce volunteer assignments and use signals to derive coverage totals, open positions, and scheduling conflicts.
