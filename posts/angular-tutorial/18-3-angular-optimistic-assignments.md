---
title: "ChurchServe Angular Tutorial Part 18-3: Optimistic Angular Assignments"
excerpt: "Build a signal-based assignment store and UI that updates immediately, confirms matching requests, rolls back failures, and reconciles version conflicts."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular optimistic update signals"
  description: "Implement optimistic Angular updates with signals, request identity, rollback, conflict reconciliation, runtime DTO validation, and focused tests."
  socialTitle: "ChurchServe Part 18-3: Optimistic Angular UI"
  socialDescription: "Make volunteer assignments feel instant while preserving honest pending, failure, and conflict states."
---

# ChurchServe Angular Tutorial Part 18-3: Optimistic Angular Assignments

Part 18-2 created a versioned, idempotent assignment command. The Angular application can now update immediately, but it must distinguish a local prediction from server-confirmed state.

> **Quick answer:** Insert a pending assignment before the HTTP request completes, tag it with the same client request ID sent to the server, replace only that pending item on success, remove only that item on an ordinary failure, and replace the whole board with the server snapshot on `409 Conflict`.

## Files Added and Updated

```text
src/app/features/schedule/
├── api/
│   ├── assignment-dto.ts
│   ├── http-assignment-repository.ts
│   └── provide-assignment-api.ts
├── service-event-detail/
│   ├── service-event-detail.html
│   ├── service-event-detail.scss
│   └── service-event-detail.ts
├── assignment-repository.ts
├── assignment-store.ts
├── assignment.ts
├── schedule.routes.ts
└── service-event.ts
```

The feature keeps its API adapter private, exposes a repository boundary to state code, and provides both at the schedule route boundary introduced in Part 17.

## Define the Client Domain Contracts

Create `assignment.ts`:

```typescript
import { Volunteer } from '../../shared/domain/volunteer';

export interface MinistryPosition {
  readonly id: string;
  readonly eventId: string;
  readonly ministry: string;
  readonly roleName: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface Assignment {
  readonly id: string;
  readonly positionId: string;
  readonly volunteerId: string;
  readonly volunteerDisplayName: string;
  readonly status: 'accepted' | 'declined';
  readonly syncStatus: 'pending' | 'confirmed';
  readonly clientRequestId?: string;
}

export interface AssignmentBoard {
  readonly eventId: string;
  readonly eventVersion: number;
  readonly assignedPositions: number;
  readonly positions: readonly MinistryPosition[];
  readonly volunteers: readonly Volunteer[];
  readonly assignments: readonly Assignment[];
}

export interface AssignVolunteerCommand {
  readonly eventId: string;
  readonly positionId: string;
  readonly volunteerId: string;
  readonly expectedEventVersion: number;
  readonly clientRequestId: string;
}

export interface AssignVolunteerResult {
  readonly assignment: Assignment;
  readonly eventVersion: number;
  readonly assignedPositions: number;
}
```

`syncStatus` is deliberately client-only. The API reports business status; the UI adds whether that result is still speculative.

Add `version` to `ServiceEvent`:

```typescript
export interface ServiceEvent {
  readonly id: string;
  readonly title: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly location: string;
  readonly requiredPositions: number;
  readonly assignedPositions: number;
  readonly version: number;
}
```

## Create the Repository Boundary

Create `assignment-repository.ts`:

```typescript
import { Observable } from 'rxjs';
import {
  AssignmentBoard,
  AssignVolunteerCommand,
  AssignVolunteerResult,
} from './assignment';

export abstract class AssignmentRepository {
  abstract loadBoard(eventId: string): Observable<AssignmentBoard>;
  abstract assign(command: AssignVolunteerCommand): Observable<AssignVolunteerResult>;
}

export class AssignmentConflictError extends Error {
  constructor(
    message: string,
    readonly board: AssignmentBoard,
  ) {
    super(message);
    this.name = 'AssignmentConflictError';
  }
}
```

The specialized error preserves the authoritative board without leaking `HttpErrorResponse` into state management.

## Validate Responses at Runtime

Static TypeScript types do not validate JSON. `assignment-dto.ts` parses `unknown` values before they enter the store:

```typescript
export function parseAssignmentBoard(value: unknown): AssignmentBoard {
  if (
    !isRecord(value) ||
    !hasString(value, 'eventId') ||
    !isPositiveInteger(value['eventVersion']) ||
    !isNonNegativeInteger(value['assignedPositions']) ||
    !Array.isArray(value['positions']) ||
    !Array.isArray(value['volunteers']) ||
    !Array.isArray(value['assignments'])
  ) {
    throw new Error('The assignment-board response did not match the expected contract.');
  }

  return {
    eventId: value['eventId'] as string,
    eventVersion: value['eventVersion'],
    assignedPositions: value['assignedPositions'],
    positions: value['positions'].map(parsePosition),
    volunteers: value['volunteers'].map(parseVolunteer),
    assignments: value['assignments'].map(parseAssignment),
  };
}

export function parseAssignVolunteerResult(value: unknown): AssignVolunteerResult {
  if (
    !isRecord(value) ||
    !isPositiveInteger(value['eventVersion']) ||
    !isNonNegativeInteger(value['assignedPositions'])
  ) {
    throw new Error('The assignment response did not match the expected contract.');
  }

  return {
    assignment: parseAssignment(value['assignment']),
    eventVersion: value['eventVersion'],
    assignedPositions: value['assignedPositions'],
  };
}

export function parseAssignmentConflict(value: unknown): {
  readonly message: string;
  readonly board: AssignmentBoard;
} {
  if (!isRecord(value) || !hasString(value, 'message')) {
    throw new Error('The assignment-conflict response did not match the expected contract.');
  }

  return { message: value['message'] as string, board: parseAssignmentBoard(value['board']) };
}
```

The complete file also validates every position, volunteer, and assignment member. A server contract regression therefore becomes a controlled load error instead of corrupting feature state.

## Implement the HTTP Adapter

Create `http-assignment-repository.ts`:

```typescript
@Injectable()
export class HttpAssignmentRepository extends AssignmentRepository {
  private readonly http = inject(HttpClient);

  loadBoard(eventId: string) {
    return this.http
      .get<unknown>(`/api/service-events/${encodeURIComponent(eventId)}/assignment-board`)
      .pipe(map(parseAssignmentBoard));
  }

  assign(command: AssignVolunteerCommand): Observable<AssignVolunteerResult> {
    return this.http
      .post<unknown>(`/api/service-events/${encodeURIComponent(command.eventId)}/assignments`, {
        positionId: command.positionId,
        volunteerId: command.volunteerId,
        expectedEventVersion: command.expectedEventVersion,
        clientRequestId: command.clientRequestId,
      })
      .pipe(
        map(parseAssignVolunteerResult),
        catchError((error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 409) {
            const conflict = parseAssignmentConflict(error.error);
            return throwError(
              () => new AssignmentConflictError(conflict.message, conflict.board),
            );
          }

          return throwError(() => error);
        }),
      );
  }
}
```

Create `provide-assignment-api.ts`:

```typescript
export function provideAssignmentApi(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: AssignmentRepository, useClass: HttpAssignmentRepository },
  ]);
}
```

Add `provideAssignmentApi()` and `AssignmentStore` to the provider list on the parent schedule route. They remain feature-scoped rather than becoming root singletons.

## Build the Optimistic Store

The central `assign` method records a pending item first:

```typescript
assign(positionId: string, volunteerId: string): void {
  const eventId = this.eventIdState();
  const eventVersion = this.eventVersionState();
  if (!eventId || eventVersion === null) {
    throw new Error('The assignment board is not loaded.');
  }

  const validation = validateAssignment(
    positionId,
    volunteerId,
    this.positionsState(),
    this.volunteersState(),
    this.assignmentsState(),
  );
  if (!validation.valid) {
    throw new Error(validation.message);
  }

  const volunteer = this.volunteersState().find((item) => item.id === volunteerId)!;
  const clientRequestId = crypto.randomUUID();
  const pending: Assignment = {
    id: `pending-${clientRequestId}`,
    positionId,
    volunteerId,
    volunteerDisplayName: volunteer.displayName,
    status: 'accepted',
    syncStatus: 'pending',
    clientRequestId,
  };

  this.noticeState.set(null);
  this.assignmentsState.update((current) => [...current, pending]);

  this.repository
    .assign({
      eventId,
      positionId,
      volunteerId,
      expectedEventVersion: eventVersion,
      clientRequestId,
    })
    .subscribe({
      next: (result) => {
        this.assignmentsState.update((current) =>
          current.map((assignment) =>
            assignment.clientRequestId === clientRequestId
              ? result.assignment
              : assignment,
          ),
        );
        this.eventVersionState.set(result.eventVersion);
        this.assignedPositionsState.set(result.assignedPositions);
        this.noticeState.set(`${volunteer.displayName} was assigned.`);
      },
      error: (error: unknown) => {
        if (error instanceof AssignmentConflictError) {
          this.applyBoard(error.board);
          this.noticeState.set(`${error.message} The latest assignments are shown.`);
          return;
        }

        this.assignmentsState.update((current) =>
          current.filter((assignment) => assignment.clientRequestId !== clientRequestId),
        );
        this.noticeState.set('The assignment was not saved. Try again.');
      },
    });
}
```

Matching by `clientRequestId` is what makes overlapping requests safe. A failed first request must not remove a second pending assignment. A conflict is different from an ordinary failure: the server supplied newer truth, so `applyBoard` replaces the entire snapshot.

The displayed assigned count includes pending operations without mutating the confirmed count:

```typescript
readonly assignedPositions = computed(() => {
  const confirmed = this.assignedPositionsState();
  if (confirmed === null) {
    return null;
  }

  return confirmed + this.assignmentsState().filter(
    (item) => item.syncStatus === 'pending',
  ).length;
});
```

Local validation prevents obviously invalid clicks, while the API repeats every rule against current data:

```typescript
export function validateAssignment(
  positionId: string,
  volunteerId: string,
  positions: readonly MinistryPosition[],
  volunteers: readonly Volunteer[],
  assignments: readonly Assignment[],
): AssignmentValidation {
  const position = positions.find((item) => item.id === positionId);
  if (!position) return { valid: false, message: 'Position was not found.' };

  const volunteer = volunteers.find((item) => item.id === volunteerId);
  if (!volunteer) return { valid: false, message: 'Volunteer was not found.' };

  if (!volunteer.eligibleMinistries.includes(position.ministry)) {
    return { valid: false, message: 'Volunteer is not eligible for this ministry.' };
  }

  const occupied = assignments.some(
    (item) => item.positionId === positionId && item.status !== 'declined',
  );

  return occupied
    ? { valid: false, message: 'Position already has an assignment.' }
    : { valid: true };
}
```

## Render the Assignment Controls

The detail component keeps select values in a signal so zoneless change detection sees them:

```typescript
private readonly selectedVolunteerIds = signal<Readonly<Record<string, string>>>({});
protected readonly assignmentStore = inject(AssignmentStore);

protected selectedVolunteer(positionId: string): string {
  return this.selectedVolunteerIds()[positionId] ?? '';
}

protected selectVolunteer(positionId: string, event: Event): void {
  const volunteerId = (event.target as HTMLSelectElement).value;
  this.selectedVolunteerIds.update((current) => ({ ...current, [positionId]: volunteerId }));
}

protected assign(positionId: string): void {
  const volunteerId = this.selectedVolunteer(positionId);
  if (volunteerId) {
    this.assignmentStore.assign(positionId, volunteerId);
    this.selectedVolunteerIds.update((current) => ({ ...current, [positionId]: '' }));
  }
}
```

Render one row per position:

```html
<ul class="position-list">
  @for (position of assignmentStore.positions(); track position.id) {
    <li>
      <div class="position-copy">
        <span class="ministry">{{ position.ministry }}</span>
        <h3>{{ position.roleName }}</h3>
        <p>
          {{ position.startsAt | date: 'shortTime' }}–{{ position.endsAt | date: 'shortTime' }}
        </p>
      </div>

      @if (assignmentStore.assignmentFor(position.id); as assignment) {
        <div class="assigned-volunteer">
          <strong>{{ assignment.volunteerDisplayName }}</strong>
          @if (assignment.syncStatus === 'pending') {
            <span class="saving" aria-live="polite">Saving…</span>
          } @else {
            <span>Confirmed</span>
          }
        </div>
      } @else {
        <div class="assignment-action">
          <label [for]="'volunteer-' + position.id">Volunteer</label>
          <select
            [id]="'volunteer-' + position.id"
            [value]="selectedVolunteer(position.id)"
            (change)="selectVolunteer(position.id, $event)"
          >
            <option value="">Choose a volunteer</option>
            @for (volunteer of assignmentStore.volunteersFor(position); track volunteer.id) {
              <option [value]="volunteer.id">{{ volunteer.displayName }}</option>
            }
          </select>
          <button
            type="button"
            [disabled]="!selectedVolunteer(position.id)"
            (click)="assign(position.id)"
          >
            Assign
          </button>
        </div>
      }
    </li>
  }
</ul>
```

The surrounding section also renders loading, retryable load failure, no-position, notice, and schedule-version states. “Saving…” is honest feedback: the UI changes immediately but does not call the assignment confirmed until the server answers.

## Test the State Transitions

The most important store tests control request completion with `Subject` instances. Verify that:

1. Loading exposes progress and a recoverable error.
2. Calling `assign` immediately creates a pending item and sends the current version.
3. Success replaces only the item with the matching request ID.
4. An ordinary failure removes only the failed operation.
5. A `409` applies the authoritative board.
6. A pending position rejects a duplicate click.
7. Eligible-volunteer filtering matches the position ministry.

One focused rollback test looks like this:

```typescript
it('rolls back only the failed optimistic operation', () => {
  loadBoard(store, repository, twoPositionBoard);
  store.assign('sound-1', 'alex');
  store.assign('camera-1', 'alex');

  repository.assignResponses[0].error(new Error('offline'));

  expect(store.assignments()).toHaveLength(1);
  expect(store.assignments()[0].positionId).toBe('camera-1');
  expect(store.notice()).toContain('not saved');
});
```

The HTTP adapter tests should cover URL encoding, request payload, response parsing, `409` conversion, and propagation of non-conflict errors. Component tests should exercise loading, error/retry, empty positions, volunteer selection, the pending label, and confirmed state.

## Result and Boundary

Part 18 now has a complete optimistic assignment path for seeded positions: the coordinator chooses an eligible volunteer, the row changes to “Saving…” immediately, and it becomes “Confirmed” after persistence. Network failures roll back only their own operation, and stale versions reconcile from the server’s board.

New events still have no position-authoring UI. That is a separate capability rather than a missing optimistic-update detail: it needs its own contracts, validation, persistence commands, and user experience.
