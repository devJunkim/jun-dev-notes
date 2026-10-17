---
title: "ChurchServe Angular Tutorial Part 6: Routing and Service Detail Pages"
excerpt: "Add bookmarkable ChurchServe create and detail routes, router-link navigation, explicit missing states, shared in-memory event state, and route-level tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular routing tutorial"
  description: "Build Angular parameterized routes, create and detail pages, router links, not-found behavior, shared in-memory state, and routing tests."
  socialTitle: "ChurchServe Part 6: Angular Routing"
  socialDescription: "Turn ChurchServe into a navigable application with tested create and detail routes and predictable missing-record behavior."
---

# ChurchServe Angular Tutorial Part 6: Routing and Service Detail Pages

Part 5 can create an event, but the form and result live inside the schedule page. Selecting a service does not produce a durable address, refreshing loses the selected view, and a coordinator cannot share a link to one event.

Part 6 moves durable navigation state into URLs. `/schedule/new` owns creation, `/schedule/:eventId` displays one event, and unknown addresses reach a dedicated not-found page. A small concrete store lets lazy route components share in-memory events; Part 7 will place an interface in front of that implementation.

> **Quick answer:** Put durable identifiers in parameterized routes, use router links for user-driven navigation, keep route pages responsible for workflow orchestration, distinguish a missing record from an unknown URL, and test the real route table with `RouterTestingHarness`.

## What Part 6 Adds

Add these files and folders:

```text
src/app/
  app.routes.spec.ts
  features/
    not-found/
      not-found.html
      not-found.scss
      not-found.ts
    schedule/
      service-event-store.spec.ts
      service-event-store.ts
      service-event-create/
        service-event-create.html
        service-event-create.scss
        service-event-create.spec.ts
        service-event-create.ts
      service-event-detail/
        service-event-detail.html
        service-event-detail.scss
        service-event-detail.ts
```

The existing schedule and event-card components change from output-driven selection to normal links. The Part 5 form remains reusable: the new creation page hosts it and decides what a valid command does.

## Define the Route Contract

Replace `src/app/app.routes.ts` with:

```typescript
import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/home/home').then((module) => module.Home),
    title: 'ChurchServe | Ministry scheduling',
  },
  {
    path: 'schedule',
    loadComponent: () => import('./features/schedule/schedule').then((module) => module.Schedule),
    title: 'Schedule | ChurchServe',
  },
  {
    path: 'schedule/new',
    loadComponent: () =>
      import('./features/schedule/service-event-create/service-event-create').then(
        (module) => module.ServiceEventCreate,
      ),
    title: 'Create service | ChurchServe',
  },
  {
    path: 'schedule/:eventId',
    loadComponent: () =>
      import('./features/schedule/service-event-detail/service-event-detail').then(
        (module) => module.ServiceEventDetail,
      ),
    title: 'Service details | ChurchServe',
  },
  {
    path: 'volunteers',
    loadComponent: () =>
      import('./features/volunteers/volunteers').then((module) => module.Volunteers),
    title: 'Volunteers | ChurchServe',
  },
  {
    path: 'not-found',
    loadComponent: () =>
      import('./features/not-found/not-found').then((module) => module.NotFound),
    title: 'Page not found | ChurchServe',
  },
  { path: '**', redirectTo: 'not-found' },
];
```

`schedule/new` must appear before `schedule/:eventId`; otherwise the literal word `new` can satisfy the parameter route. The wildcard comes last because it accepts anything not matched earlier.

Each page remains lazy. Static titles give every route a meaningful browser-tab label without requiring a resolver.

## Share Events Between Lazy Pages

Create `src/app/features/schedule/service-event-store.ts`:

```typescript
import { Injectable, signal } from '@angular/core';
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEvent } from './service-event';

const initialEvents: readonly ServiceEvent[] = [
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

@Injectable({ providedIn: 'root' })
export class ServiceEventStore {
  private readonly eventsState = signal<readonly ServiceEvent[]>(initialEvents);

  readonly events = this.eventsState.asReadonly();

  findById(id: string): ServiceEvent | undefined {
    return this.eventsState().find((event) => event.id === id);
  }

  create(command: CreateServiceEventCommand): ServiceEvent {
    const event: ServiceEvent = {
      id: crypto.randomUUID(),
      ...command,
      requiredPositions: 0,
      assignedPositions: 0,
    };

    this.eventsState.update((events) => [...events, event]);
    return event;
  }
}
```

The root-provided instance survives navigation between lazy pages. `events` is readonly to consumers, `findById` supports route lookup, and `create` keeps ID generation plus immutable updates in one place.

This is intentionally a concrete in-memory service, not the final persistence contract. Refreshing resets it. Part 7 will introduce a repository interface; Parts 8 and 9 will provide local-storage and HTTP implementations.

## Move Creation to Its Own Route Page

Create `service-event-create/service-event-create.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { ServiceEventForm } from '../service-event-form/service-event-form';
import { ServiceEventStore } from '../service-event-store';

@Component({
  selector: 'app-service-event-create',
  imports: [RouterLink, ServiceEventForm],
  templateUrl: './service-event-create.html',
  styleUrl: './service-event-create.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventCreate {
  private readonly router = inject(Router);
  private readonly store = inject(ServiceEventStore);

  protected createEvent(command: CreateServiceEventCommand): void {
    const event = this.store.create(command);
    void this.router.navigate(['/schedule', event.id]);
  }
}
```

Create `service-event-create/service-event-create.html`:

```html
<main class="create-page" aria-labelledby="create-heading">
  <a routerLink="/schedule" class="back-link">← Back to schedule</a>
  <p class="eyebrow">Schedule</p>
  <h1 id="create-heading">Create a service event</h1>
  <p class="introduction">
    Add the service details. After validation succeeds, ChurchServe opens the new event's stable
    detail URL.
  </p>

  <app-service-event-form (submitted)="createEvent($event)" />
</main>
```

The form still owns controls, validation, and command normalization. The route page owns the application command: store the event, then navigate to its new detail URL. This separation keeps the form reusable and makes routing a page-level concern.

The accompanying SCSS uses the same maximum width, heading scale, green link treatment, and visible focus outline as the schedule page. It does not change the form's encapsulated styles.

## Read the Event ID Reactively

Create `service-event-detail/service-event-detail.ts`:

```typescript
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ServiceEventStore } from '../service-event-store';

@Component({
  selector: 'app-service-event-detail',
  imports: [DatePipe, RouterLink],
  templateUrl: './service-event-detail.html',
  styleUrl: './service-event-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(ServiceEventStore);

  private readonly eventId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('eventId'))),
    { initialValue: this.route.snapshot.paramMap.get('eventId') },
  );

  protected readonly event = computed(() => {
    const id = this.eventId();
    return id ? this.store.findById(id) : undefined;
  });

  protected readonly openPositions = computed(() => {
    const event = this.event();
    return event ? Math.max(0, event.requiredPositions - event.assignedPositions) : 0;
  });
}
```

`paramMap` is an observable because Angular can reuse a routed component while parameters change. `toSignal` keeps that stream reactive without manual subscription cleanup. The snapshot supplies the initial value so the first render can resolve the event immediately.

The lookup returns `undefined` rather than using a non-null assertion. A syntactically valid ID does not guarantee that a record exists.

Create `service-event-detail/service-event-detail.html`:

```html
<main class="detail-page" aria-labelledby="detail-heading">
  <a routerLink="/schedule" class="back-link">← Back to schedule</a>

  @if (event(); as serviceEvent) {
    <p class="eyebrow">Service event</p>
    <h1 id="detail-heading">{{ serviceEvent.title }}</h1>

    <dl class="event-details">
      <div>
        <dt>Starts</dt>
        <dd>
          <time [attr.datetime]="serviceEvent.startsAt">
            {{ serviceEvent.startsAt | date: 'full' }}
          </time>
        </dd>
      </div>
      <div>
        <dt>Ends</dt>
        <dd>
          <time [attr.datetime]="serviceEvent.endsAt">
            {{ serviceEvent.endsAt | date: 'full' }}
          </time>
        </dd>
      </div>
      <div>
        <dt>Location</dt>
        <dd>{{ serviceEvent.location }}</dd>
      </div>
      <div>
        <dt>Staffing</dt>
        <dd>
          @if (serviceEvent.requiredPositions === 0) {
            No positions configured
          } @else if (openPositions() === 0) {
            Fully staffed
          } @else {
            {{ openPositions() }} open
            {{ openPositions() === 1 ? 'position' : 'positions' }}
          }
        </dd>
      </div>
    </dl>
  } @else {
    <section class="missing-event" role="alert">
      <p class="eyebrow">Service event</p>
      <h1 id="detail-heading">Service not found</h1>
      <p>This event does not exist or is no longer available.</p>
      <a routerLink="/schedule">Return to the schedule</a>
    </section>
  }
</main>
```

An unknown event ID stays on its meaningful URL and renders a record-specific missing state. That differs from an unknown application URL, which reaches the generic 404 page. The distinction helps users understand whether the address shape is valid but its data is gone.

## Replace Selection Outputs With Links

Import `RouterLink` in `ServiceEventCard`, add it to the component imports, and remove the `selected` output plus `selectEvent` method. Replace the old button with:

```html
<a
  class="card-link"
  [routerLink]="['/schedule', event().id]"
  [attr.aria-label]="'View ' + event().title"
>
  View schedule
</a>
```

A link is the correct semantic control because navigation is the action. It supports copying the address, opening a new tab, keyboard activation, and browser history without custom click logic.

Update the schedule page to import `RouterLink`, remove form submission and selected-event state, read seeded events from `ServiceEventStore`, and add the creation link:

```html
<div class="schedule-actions">
  <a routerLink="/schedule/new">Create service</a>
</div>
```

The schedule remains responsible for loading, error, ready, and empty rendering. Creation and detail presentation now belong to their own route pages.

## Add a Generic Not-Found Page

Create `features/not-found/not-found.ts`:

```typescript
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  templateUrl: './not-found.html',
  styleUrl: './not-found.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFound {}
```

Create `not-found.html`:

```html
<main class="not-found-page" aria-labelledby="not-found-heading">
  <p class="eyebrow">404</p>
  <h1 id="not-found-heading">Page not found</h1>
  <p>The address may be incorrect, or the page may have moved.</p>
  <a routerLink="/">Return home</a>
</main>
```

The wildcard redirects here only after every known route fails. The explicit route gives the page its own title and makes `/not-found` navigable during testing.

## Test the Real Route Table

Create `src/app/app.routes.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NotFound } from './features/not-found/not-found';
import { ServiceEventCreate } from './features/schedule/service-event-create/service-event-create';
import { ServiceEventDetail } from './features/schedule/service-event-detail/service-event-detail';
import { routes } from './app.routes';

describe('application routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
  });

  it('opens a service detail route with its ID', async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(
      '/schedule/sunday-2026-11-01',
      ServiceEventDetail,
    );

    expect(component).toBeTruthy();
    expect(harness.routeNativeElement?.textContent).toContain('Sunday Morning Service');
  });

  it('opens the dedicated service creation route', async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/schedule/new', ServiceEventCreate);

    expect(component).toBeTruthy();
    expect(harness.routeNativeElement?.textContent).toContain('Create a service event');
  });

  it('shows a specific missing-event state for an unknown event ID', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/schedule/missing', ServiceEventDetail);

    expect(harness.routeNativeElement?.textContent).toContain('Service not found');
  });

  it('redirects an unknown URL to the not-found page', async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/does-not-exist', NotFound);

    expect(component).toBeTruthy();
    expect(harness.routeNativeElement?.textContent).toContain('Page not found');
  });
});
```

These tests use the production route array and lazy components. They assert both the activated component and meaningful content, so a URL change cannot hide the wrong page rendering.

The create-page test also submits the real Part 5 form, verifies that the concrete store receives the normalized command, and confirms navigation to the generated event ID. Store tests separately cover seeded lookup and immutable creation.

## Run the Part 6 Checks

```bash
npm test -- --watch=false
npm run build
```

The completed Part 6 repository has nine test files and thirty-one passing tests. The build produces separate lazy chunks for schedule, creation, detail, not-found, home, and volunteers.

Also parse `ChurchServe.slnx` and verify every listed file exists after adding the new route pages, store, and tests.

## Production Boundaries

The URL is durable; the data is not yet durable. Seeded event URLs survive refresh because the store reconstructs those records. A newly created event exists only for the current application session, so refreshing its detail URL produces the missing-event state.

That limitation is explicit rather than hidden. Part 7 will define the repository contract, Part 8 will persist locally with versioned data, and Part 9 will introduce the REST API. Authentication and authorization arrive later; a shareable URL is not permission to view an event.

Route parameters are also untrusted input. This part treats an unknown ID as a normal absence instead of asserting that a record exists or interpolating the value into unsafe markup.

## Part 6 Checkpoint

ChurchServe now has bookmarkable schedule detail URLs, a dedicated creation route, semantic router links, explicit record-missing and URL-missing experiences, shared in-memory event state, and route-level tests against the real configuration.

Part 7 will formalize repository interfaces so page components stop depending directly on `ServiceEventStore` and future persistence implementations can be swapped without rewriting routing components.
