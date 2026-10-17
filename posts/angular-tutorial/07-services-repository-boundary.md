---
title: "ChurchServe Angular Tutorial Part 7: Services and a Repository Boundary"
excerpt: "Move ChurchServe data access behind an Angular repository contract and dependency injection without adding storage concerns too early."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular services dependency injection tutorial"
  description: "Use Angular services and dependency injection to build a focused, testable repository boundary for ChurchServe service events."
  socialTitle: "ChurchServe Part 7: Angular Services"
  socialDescription: "Separate components from event storage with a focused repository contract, an in-memory implementation, and isolated tests."
---

# ChurchServe Angular Tutorial Part 7: Services and a Repository Boundary

ChurchServe's schedule pages currently depend on `ServiceEventStore`, a concrete class that combines the operations components need with the fact that records live in memory. That works today, but Part 8 will add browser persistence and Part 9 will introduce HTTP. If components continue to depend on a storage-specific class, each storage change will spread through the UI.

> **Quick answer:** Define a small repository contract around the service-event use cases ChurchServe has now, implement it with the existing in-memory signal state, and let Angular dependency injection select that implementation. The UI keeps the same behavior while becoming independent of the storage mechanism.

## What Part 7 Changes

This part is an architectural refactoring, so the visible UI does not change. The important dependency changes from this:

```text
Schedule component -> ServiceEventStore
```

to this:

```text
Schedule component -> ServiceEventRepository <- InMemoryServiceEventRepository
```

The same boundary is used by the create and detail pages. Components know which operations are available, but they no longer know which concrete class stores the records.

Part 7 makes these code changes:

1. Replace `ServiceEventStore` with an abstract `ServiceEventRepository` contract.
2. Move the existing signal-backed behavior into `InMemoryServiceEventRepository`.
3. Register the implementation in the application providers.
4. Inject the contract into the schedule, create, and detail components.
5. Test repository behavior separately from component rendering.

## Design the Contract from Current Use Cases

Start with only the operations ChurchServe uses today. The schedule lists events, the detail route finds one event by ID, and the create route adds an event. There is no update screen yet, so adding an `update` method would be speculative.

Create `src/app/features/schedule/service-event-repository.ts`:

```typescript
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEvent } from './service-event';

export abstract class ServiceEventRepository {
  abstract list(): readonly ServiceEvent[];
  abstract findById(id: string): ServiceEvent | undefined;
  abstract create(command: CreateServiceEventCommand): ServiceEvent;
}
```

The return type from `list` is read-only so callers cannot add or remove items through the returned collection. `findById` explicitly models a missing record with `undefined`, which the detail page already turns into its “Service not found” state.

An abstract class is useful here because it serves two roles: TypeScript checks that implementations satisfy the contract, and Angular can use the class as a runtime dependency-injection token. An `InjectionToken` would also work, but it would add another symbol without improving this small feature.

The methods remain synchronous because the current implementation is local. Part 9 can deliberately change this contract when HTTP introduces latency and failures. Pretending that an in-memory operation is already a network request would make this step harder to understand without removing the later migration work.

## Move Existing State into the In-Memory Implementation

Create `src/app/features/schedule/in-memory-service-event-repository.ts`:

```typescript
import { Injectable, signal } from '@angular/core';
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEventRepository } from './service-event-repository';
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

@Injectable()
export class InMemoryServiceEventRepository extends ServiceEventRepository {
  private readonly eventsState = signal<readonly ServiceEvent[]>(initialEvents);

  list(): readonly ServiceEvent[] {
    return this.eventsState();
  }

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

The private signal still provides immutable collection replacement inside the repository. `create` uses a new array rather than mutating the existing array, and new events begin with zero required and assigned positions because position management is a separate use case.

The class uses `@Injectable()` but not `providedIn: 'root'`. The provider registration in the next section owns the decision about which implementation the application receives. This makes a later replacement explicit and keeps the contract independent of one concrete class.

## Select the Implementation with Dependency Injection

Update `src/app/app.config.ts`:

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { InMemoryServiceEventRepository } from './features/schedule/in-memory-service-event-repository';
import { ServiceEventRepository } from './features/schedule/service-event-repository';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    { provide: ServiceEventRepository, useClass: InMemoryServiceEventRepository },
  ],
};
```

The object provider tells Angular: when a class requests `ServiceEventRepository`, construct and return `InMemoryServiceEventRepository`. Because the provider is application-scoped, schedule, create, and detail routes share one repository instance while the application is running.

Part 8 can replace this one provider mapping with a persistent implementation. That is the practical value of the boundary: storage selection happens at the composition root instead of inside components.

## Make the Schedule Depend on the Contract

Update `src/app/features/schedule/schedule.ts`:

```typescript
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ServiceEventCard } from './service-event-card/service-event-card';
import { ServiceEventRepository } from './service-event-repository';
import { ServiceEvent } from './service-event';

export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; events: readonly ServiceEvent[] };

@Component({
  selector: 'app-schedule',
  imports: [RouterLink, ServiceEventCard],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Schedule {
  private readonly repository = inject(ServiceEventRepository);

  protected readonly state = signal<ScheduleState>({ status: 'loading' });

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.status === 'error' ? current.message : '';
  });

  protected readonly events = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? current.events : [];
  });

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.replaceState({ status: 'ready', events: this.repository.list() });
  }

  setStateForTest(state: ScheduleState): void {
    this.replaceState(state);
  }

  private replaceState(state: ScheduleState): void {
    this.state.set(state);
  }
}
```

The schedule still owns loading, error, and ready presentation states. Those are UI concerns, not stored service-event records. Keeping them out of the repository prevents a reusable data service from becoming a global container for every screen preference and request indicator.

## Update the Create Page

Update `src/app/features/schedule/service-event-create/service-event-create.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { ServiceEventForm } from '../service-event-form/service-event-form';
import { ServiceEventRepository } from '../service-event-repository';

@Component({
  selector: 'app-service-event-create',
  imports: [RouterLink, ServiceEventForm],
  templateUrl: './service-event-create.html',
  styleUrl: './service-event-create.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventCreate {
  private readonly router = inject(Router);
  private readonly repository = inject(ServiceEventRepository);

  protected createEvent(command: CreateServiceEventCommand): void {
    const event = this.repository.create(command);
    void this.router.navigate(['/schedule', event.id]);
  }
}
```

The form continues to emit a `CreateServiceEventCommand`. The page asks the repository to create the entity, then navigates with the returned ID. It does not generate IDs or update arrays itself.

## Update the Detail Page

Update `src/app/features/schedule/service-event-detail/service-event-detail.ts`:

```typescript
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ServiceEventRepository } from '../service-event-repository';

@Component({
  selector: 'app-service-event-detail',
  imports: [DatePipe, RouterLink],
  templateUrl: './service-event-detail.html',
  styleUrl: './service-event-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly repository = inject(ServiceEventRepository);

  private readonly eventId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('eventId'))),
    { initialValue: this.route.snapshot.paramMap.get('eventId') },
  );

  protected readonly event = computed(() => {
    const id = this.eventId();
    return id ? this.repository.findById(id) : undefined;
  });

  protected readonly openPositions = computed(() => {
    const event = this.event();
    return event ? Math.max(0, event.requiredPositions - event.assignedPositions) : 0;
  });
}
```

Routing remains a component concern: the page reads `eventId` from `ActivatedRoute`. Record lookup belongs to the repository. An unknown ID still produces `undefined`, so the existing template keeps rendering the specific missing-event state.

## Test Repository Behavior Directly

Replace the old store test with `src/app/features/schedule/in-memory-service-event-repository.spec.ts`:

```typescript
import { InMemoryServiceEventRepository } from './in-memory-service-event-repository';

describe('InMemoryServiceEventRepository', () => {
  let repository: InMemoryServiceEventRepository;

  beforeEach(() => {
    repository = new InMemoryServiceEventRepository();
  });

  it('finds a seeded event by its route ID', () => {
    expect(repository.findById('sunday-2026-11-01')?.title).toBe('Sunday Morning Service');
    expect(repository.findById('missing')).toBeUndefined();
  });

  it('creates an event with a client ID and immutable collection update', () => {
    const previousEvents = repository.list();

    const created = repository.create({
      title: 'Prayer Night',
      startsAt: '2026-11-06T19:00:00.000Z',
      endsAt: '2026-11-06T20:00:00.000Z',
      location: 'Chapel',
    });

    expect(created.id).toBeTruthy();
    expect(created.requiredPositions).toBe(0);
    expect(created.assignedPositions).toBe(0);
    expect(repository.list()).not.toBe(previousEvents);
    expect(repository.findById(created.id)).toEqual(created);
  });
});
```

These tests instantiate the concrete implementation directly because they are testing its behavior, not Angular's injector. The first test protects seeded lookup and the missing-record contract. The second protects identity generation, default staffing counts, immutable array replacement, and retrieval of the newly created record.

## Provide the Boundary in Component Tests

Tests that create a component must provide the repository token just as the real application does. For example, the schedule test setup becomes:

```typescript
beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ServiceEventRepository, useClass: InMemoryServiceEventRepository },
    ],
  });
});
```

The create-page test reads through the contract rather than reaching into implementation state:

```typescript
const repository = TestBed.inject(ServiceEventRepository);

// Submit the form...

const created = repository.list().at(-1);
expect(created?.title).toBe('Prayer Night');
expect(navigate).toHaveBeenCalledWith(['/schedule', created?.id]);
```

For a test that needs unusual behavior, the same token can receive a small fake instead of the in-memory class. That makes failure and empty-data tests possible without modifying production code.

## What Does Not Belong in the Repository

The repository owns service-event records and operations on those records. It does not own the schedule's loading banner, selected filters, sorting choice, open dialog, or route parameters. Those values describe how one screen presents or navigates data.

The repository also does not persist anything yet. Refreshing the browser still resets created events. That limitation is intentional: Part 7 establishes the replaceable boundary, and Part 8 will add versioned browser persistence behind it.

## Part 7 Checkpoint

ChurchServe has the same visible schedule, create, and detail behavior, but those components now depend on `ServiceEventRepository` rather than a concrete store. Angular selects `InMemoryServiceEventRepository` at the application boundary, and repository behavior has focused unit tests.

Part 8 can now add browser storage by replacing the implementation and provider mapping instead of rewriting the UI.
