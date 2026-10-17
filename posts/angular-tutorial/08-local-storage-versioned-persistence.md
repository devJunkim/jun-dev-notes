---
title: "ChurchServe Angular Tutorial Part 8: Safe Local Storage and Versioned Data"
excerpt: "Persist the beginner ChurchServe schedule in localStorage with schema versioning, runtime validation, migration, recovery, and storage-focused unit tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular localStorage tutorial"
  description: "Persist Angular application data safely with a storage adapter, schema versions, runtime validation, migration, recovery, and tests."
  socialTitle: "ChurchServe Part 8: Versioned Browser Storage"
  socialDescription: "Complete the beginner application with tested local persistence that can recover from invalid or outdated browser data."
---

# ChurchServe Angular Tutorial Part 8: Safe Local Storage and Versioned Data

The beginner application should survive a refresh, but `localStorage` is an untrusted persistence boundary. Users, browser extensions, older application versions, or partial writes can leave values that do not match current TypeScript interfaces.

Part 7 moved ChurchServe's schedule pages behind `ServiceEventRepository`. Part 8 uses that boundary as intended: replace the in-memory implementation with a versioned browser-storage implementation without rewriting the schedule, create, or detail workflows.

> **Quick answer:** Hide browser storage behind an adapter, save a versioned document, validate parsed values at runtime, migrate known older versions, and recover explicitly when data is malformed instead of casting JSON into trusted application state.

## Files Added and Changed

Part 8 replaces the Part 7 in-memory implementation with a persistence folder and updates the application composition root, integration tests, and create-page failure UI:

```text
src/app/
  app.config.ts                                      updated
  app.routes.spec.ts                                updated
  features/
    schedule/
      persistence/                                  new
        key-value-storage.ts                        new
        local-data.ts                               new
        local-data.spec.ts                          new
        local-storage-service-event-repository.ts   new
        local-storage-service-event-repository.spec.ts new
        provide-local-persistence.ts                new
      schedule.spec.ts                              updated
      service-event-create/
        service-event-create.ts                     updated
        service-event-create.html                   updated
        service-event-create.scss                   updated
        service-event-create.spec.ts                updated
      in-memory-service-event-repository.ts          removed
      in-memory-service-event-repository.spec.ts     removed
      service-event-repository.ts                    unchanged
```

`ChurchServe.slnx` and `README.md` also change so Visual Studio and the repository documentation show the Part 8 structure. The persistence files remain inside the schedule feature because they implement that feature's repository boundary; they are not general-purpose application utilities.

## Start from the Part 7 Repository Boundary

The Part 7 contract already contains exactly the operations this implementation needs, so it remains unchanged:

```typescript
import { CreateServiceEventCommand } from './create-service-event-command';
import { ServiceEvent } from './service-event';

export abstract class ServiceEventRepository {
  abstract list(): readonly ServiceEvent[];
  abstract findById(id: string): ServiceEvent | undefined;
  abstract create(command: CreateServiceEventCommand): ServiceEvent;
}
```

The contract contains only operations ChurchServe currently uses. It does not expose `localStorage`, add an update method before an edit workflow exists, or publish an unused signal. `list`, `findById`, and `create` describe application use cases rather than a storage technology.

The schedule, creation, and detail pages already inject this abstract class. Part 8 therefore changes the implementation and dependency-injection registration, not those consumers.

## Store a Versioned Document

```typescript
export interface ChurchServeLocalDataV1 {
  readonly version: 1;
  readonly events: readonly ServiceEvent[];
  readonly positions: readonly MinistryPosition[];
  readonly volunteers: readonly Volunteer[];
  readonly assignments: readonly Assignment[];
}
```

The version belongs inside the stored document. Changing a key name alone does not explain which schema produced the value or how to migrate it. The actual validator also checks every record shape, allowed assignment status, unique IDs, and relationships from positions to events and assignments to positions and volunteers.

Do not store authentication tokens, pastoral notes, children's details, or other sensitive records in this tutorial. Browser storage is accessible to scripts running in the same origin and is not a secure vault.

## Wrap the Browser API

```typescript
import { Injectable } from '@angular/core';

export abstract class KeyValueStorage {
  abstract get(key: string): string | null;
  abstract set(key: string, value: string): void;
  abstract remove(key: string): void;
}

@Injectable()
export class BrowserKeyValueStorage extends KeyValueStorage {
  get(key: string): string | null { return localStorage.getItem(key); }
  set(key: string, value: string): void { localStorage.setItem(key, value); }
  remove(key: string): void { localStorage.removeItem(key); }
}
```

The adapter makes quota failures, unavailable storage, and tests manageable without replacing the global `localStorage` object.

## Parse Before Trusting

```typescript
export const churchServeStorageKey = 'churchserve:schedule';

export function readLocalData(storage: KeyValueStorage): ChurchServeLocalDataV1 {
  let raw: string | null;
  try {
    raw = storage.get(churchServeStorageKey);
  } catch {
    return createInitialLocalData();
  }

  if (raw === null) return createInitialLocalData();

  try {
    const candidate: unknown = JSON.parse(raw);
    if (isLocalDataV1(candidate)) return candidate;
    if (isLocalDataV0(candidate)) {
      const migrated = migrateV0ToV1(candidate);
      try {
        storage.set(churchServeStorageKey, JSON.stringify(migrated));
      } catch {
        // Migration is still usable in memory for this session.
      }
      return migrated;
    }
  } catch {
    // Recover below without logging stored names or schedule data.
  }

  try {
    storage.remove(churchServeStorageKey);
  } catch {
    // Recovery can continue even if invalid data cannot be removed.
  }
  return createInitialLocalData();
}
```

`JSON.parse(raw) as ChurchServeLocalDataV1` suppresses the exact check the boundary needs. `isLocalDataV1` confirms the object, version, arrays, required strings, nonnegative counts, allowed statuses, unique IDs, and cross-record references. A schema library can reduce repetitive validation later, but the trust decision remains the application's responsibility.

The validator narrows `unknown` only after the whole document is structurally consistent:

```typescript
export function isLocalDataV1(value: unknown): value is ChurchServeLocalDataV1 {
  if (!isRecord(value) || value['version'] !== 1) {
    return false;
  }

  const events = value['events'];
  const positions = value['positions'];
  const volunteers = value['volunteers'];
  const assignments = value['assignments'];
  if (
    !Array.isArray(events) ||
    !events.every(isServiceEvent) ||
    !hasUniqueIds(events) ||
    !Array.isArray(positions) ||
    !positions.every(isMinistryPosition) ||
    !hasUniqueIds(positions) ||
    !Array.isArray(volunteers) ||
    !volunteers.every(isVolunteer) ||
    !hasUniqueIds(volunteers) ||
    !Array.isArray(assignments) ||
    !assignments.every(isAssignment) ||
    !hasUniqueIds(assignments)
  ) {
    return false;
  }

  const eventIds = new Set(events.map((event) => event.id));
  const positionIds = new Set(positions.map((position) => position.id));
  const volunteerIds = new Set(volunteers.map((volunteer) => volunteer.id));
  return (
    positions.every((position) => eventIds.has(position.eventId)) &&
    assignments.every(
      (assignment) =>
        positionIds.has(assignment.positionId) && volunteerIds.has(assignment.volunteerId),
    )
  );
}

function isServiceEvent(value: unknown): value is ServiceEvent {
  return (
    isRecord(value) &&
    hasNonEmptyString(value, 'id') &&
    hasNonEmptyString(value, 'title') &&
    hasNonEmptyString(value, 'startsAt') &&
    hasNonEmptyString(value, 'endsAt') &&
    hasNonEmptyString(value, 'location') &&
    isNonNegativeInteger(value['requiredPositions']) &&
    isNonNegativeInteger(value['assignedPositions'])
  );
}

function isMinistryPosition(value: unknown): value is MinistryPosition {
  return (
    isRecord(value) &&
    hasNonEmptyString(value, 'id') &&
    hasNonEmptyString(value, 'eventId') &&
    hasNonEmptyString(value, 'ministry') &&
    hasNonEmptyString(value, 'roleName') &&
    hasNonEmptyString(value, 'startsAt') &&
    hasNonEmptyString(value, 'endsAt')
  );
}

function isVolunteer(value: unknown): value is Volunteer {
  return (
    isRecord(value) &&
    hasNonEmptyString(value, 'id') &&
    hasNonEmptyString(value, 'displayName') &&
    Array.isArray(value['eligibleMinistries']) &&
    value['eligibleMinistries'].every(
      (ministry) => typeof ministry === 'string' && ministry.trim().length > 0,
    )
  );
}

function isAssignment(value: unknown): value is Assignment {
  return (
    isRecord(value) &&
    hasNonEmptyString(value, 'id') &&
    hasNonEmptyString(value, 'positionId') &&
    hasNonEmptyString(value, 'volunteerId') &&
    (value['status'] === 'proposed' ||
      value['status'] === 'accepted' ||
      value['status'] === 'declined')
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasNonEmptyString(value: Record<string, unknown>, key: string): boolean {
  return typeof value[key] === 'string' && value[key].trim().length > 0;
}

function isNonNegativeInteger(value: unknown): boolean {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

function hasUniqueIds(values: readonly { readonly id: string }[]): boolean {
  return new Set(values.map((value) => value.id)).size === values.length;
}
```

No stored value returns the two tutorial seed events. Malformed JSON, an unsupported version, duplicate IDs, invalid statuses, or broken relationships remove the invalid document when possible and recover to the same known-good initial state.

In production, record a sanitized diagnostic when recovery occurs. Do not log the raw stored document because it can contain names and schedule information.

## Implement the Persistent Repository

`LocalStorageServiceEventRepository` loads one validated document during construction and keeps the current event array in a private signal. Its public surface still matches the Part 7 contract:

```typescript
import { Injectable, signal, WritableSignal } from '@angular/core';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { ServiceEventRepository } from '../service-event-repository';
import { ServiceEvent } from '../service-event';
import { KeyValueStorage } from './key-value-storage';
import { ChurchServeLocalDataV1, churchServeStorageKey, readLocalData } from './local-data';

@Injectable()
export class LocalStorageServiceEventRepository extends ServiceEventRepository {
  private readonly document: ChurchServeLocalDataV1;
  private readonly eventsState: WritableSignal<readonly ServiceEvent[]>;

  constructor(private readonly storage: KeyValueStorage) {
    super();
    this.document = readLocalData(storage);
    this.eventsState = signal<readonly ServiceEvent[]>(this.document.events);
  }

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
    const events = [...this.eventsState(), event];

    this.persist({ ...this.document, events });
    this.eventsState.set(events);
    return event;
  }

  private persist(document: ChurchServeLocalDataV1): void {
    this.storage.set(churchServeStorageKey, JSON.stringify(document));
  }
}
```

The write order is deliberate. ChurchServe writes the candidate document before updating its signal. If storage is disabled or full, `set` throws and the in-memory collection retains its previous reference and values. The application never exposes a record that it failed to persist.

## Show a Recoverable Save Error

Storage can fail even after form validation succeeds. The create page catches that infrastructure failure, stays on the form, and exposes an accessible message instead of navigating to a detail URL for a record that would disappear on refresh:

```typescript
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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

  protected readonly saveError = signal('');

  protected createEvent(command: CreateServiceEventCommand): void {
    this.saveError.set('');
    try {
      const event = this.repository.create(command);
      void this.router.navigate(['/schedule', event.id]);
    } catch {
      this.saveError.set(
        'The event could not be saved in this browser. Check storage permissions and try again.',
      );
    }
  }
}
```

Add the alert after the form in `service-event-create.html`:

```html
<app-service-event-form (submitted)="createEvent($event)" />

@if (saveError()) {
  <p class="save-error" role="alert">{{ saveError() }}</p>
}
```

The page deliberately avoids showing the raw exception. Browser-specific quota and permission details are useful for diagnostics, but the user needs a stable explanation and a safe retry path.

## Configure Dependency Injection

Provide the adapter and implementation together:

```typescript
export function provideLocalPersistence(): Provider[] {
  return [
    { provide: KeyValueStorage, useClass: BrowserKeyValueStorage },
    { provide: ServiceEventRepository, useClass: LocalStorageServiceEventRepository },
  ];
}
```

Use that provider group at the application composition root:

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideLocalPersistence } from './features/schedule/persistence/provide-local-persistence';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideLocalPersistence(),
  ],
};
```

Tests use the same provider helper for route integration or replace the abstract contracts with in-memory fakes for isolated failure cases. The schedule, create, and detail components continue asking only for `ServiceEventRepository`.

## Plan Migrations

The tutorial includes a known V0 shape containing `version: 0` and validated events. `migrateV0ToV1` deterministically adds empty positions, volunteers, and assignments, then attempts to save the V1 result. Do not make the current validator accept every historical shape; that spreads compatibility logic through normal application code.

Keep migrations deterministic and test fixtures from every supported version. If a version is too old to migrate safely, preserve the raw value for an intentional export path or ask the user before resetting it.

## Tests for This Part

Use an in-memory adapter:

```typescript
class MemoryStorage extends KeyValueStorage {
  readonly values = new Map<string, string>();
  failWrites = false;

  get(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  set(key: string, value: string): void {
    if (this.failWrites) {
      throw new DOMException('Quota exceeded', 'QuotaExceededError');
    }
    this.values.set(key, value);
  }

  remove(key: string): void {
    this.values.delete(key);
  }
}
```

Test the boundary cases:

```typescript
it('removes malformed JSON and recovers with seeded data', () => {
  const storage = new MemoryStorage();
  storage.set('churchserve:schedule', '{broken');

  expect(readLocalData(storage)).toEqual(createInitialLocalData());
  expect(storage.get('churchserve:schedule')).toBeNull();
});

it('rejects an unsupported schema version', () => {
  const storage = new MemoryStorage();
  storage.set('churchserve:schedule', JSON.stringify({ version: 99 }));

  expect(readLocalData(storage)).toEqual(createInitialLocalData());
});

it('does not change memory when persistence fails', () => {
  const command = {
    title: 'Prayer Night',
    startsAt: '2026-11-06T19:00:00.000Z',
    endsAt: '2026-11-06T20:00:00.000Z',
    location: 'Chapel',
  };
  const storage = new MemoryStorage();
  const repository = new LocalStorageServiceEventRepository(storage);
  const previousEvents = repository.list();
  storage.failWrites = true;

  expect(() => repository.create(command)).toThrowError('Quota exceeded');
  expect(repository.list()).toBe(previousEvents);
  expect(repository.findById(previousEvents[0].id)).toEqual(previousEvents[0]);
});
```

The completed tests also cover a full repository round trip, V0 migration, simulated quota failure, duplicate IDs, invalid assignment status, broken record relationships, and visible UI recovery. The quota test proves a rejected command does not change the in-memory array or overwrite the last valid document.

## Run the Part 8 Checks

```bash
npm test -- --watch=false
npm run build
```

The completed Part 8 repository has ten test files and thirty-eight passing tests. The production build verifies the persistence providers and every lazy route together.

## Beginner Milestone

ChurchServe now supports an application shell, schedule cards, explicit UI states, assignment calculations, validated event forms, routing, a repository boundary, versioned local persistence, runtime recovery, and tests for each feature.

Part 9 begins the intermediate stage by replacing the local repository with an HTTP implementation while preserving the user-facing workflows built so far.
