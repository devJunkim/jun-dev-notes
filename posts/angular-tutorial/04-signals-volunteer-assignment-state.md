---
title: "ChurchServe Angular Tutorial Part 4: Signals and Volunteer Assignment State"
excerpt: "Manage ChurchServe volunteers, positions, and assignments with Angular signals, immutable updates, computed staffing summaries, and pure unit tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular signals state tutorial"
  description: "Use Angular signals and computed state to manage volunteer assignments, open ministry positions, conflicts, and immutable updates."
  socialTitle: "ChurchServe Part 4: Angular Signals"
  socialDescription: "Build tested volunteer-assignment state with writable signals, computed summaries, and explicit domain rules."
---

# ChurchServe Angular Tutorial Part 4: Signals and Volunteer Assignment State

ChurchServe can display events, but staffing counts are still embedded summary numbers. We now need the records that produce those totals: ministry positions, volunteers, and assignments.

Part 3 made the schedule honest about loading, failure, and empty results. Part 4 moves beneath that presentation layer and models the scheduling decisions themselves. The goal is not to add a large form yet; it is to establish a small state boundary that later components, persistence, and API calls can use without duplicating business rules.

You will define readonly domain records, keep source collections in private writable signals, expose public readonly signals, derive open positions with `computed`, and update assignments immutably through commands. Pure validation tests will cover individual rules, while store tests will prove that derived state changes without partial mutation.

> **Quick answer:** Keep source records in writable signals, expose derived staffing information through computed signals, update collections immutably, and place assignment invariants in testable functions rather than scattering them through click handlers.

## Continue From Part 3

Create three files under the existing schedule feature:

```text
src/app/features/schedule/
  assignment.ts
  assignment-store.ts
  assignment-store.spec.ts
```

Part 4 does not replace the schedule component or add a visible assignment form. It creates the tested domain state that later UI and API parts will consume. Keeping that boundary explicit prevents form controls, HTTP responses, and rendering concerns from becoming the source of business truth.

## Define the Scheduling Records

Create `src/app/features/schedule/assignment.ts`:

```typescript
export interface MinistryPosition {
  readonly id: string;
  readonly eventId: string;
  readonly ministry: string;
  readonly roleName: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface Volunteer {
  readonly id: string;
  readonly displayName: string;
  readonly eligibleMinistries: readonly string[];
}

export interface Assignment {
  readonly id: string;
  readonly positionId: string;
  readonly volunteerId: string;
  readonly status: 'proposed' | 'accepted' | 'declined';
}
```

`MinistryPosition` describes one staffing need for one event. It owns the ministry requirement and service interval because eligibility and future conflict checks depend on them. `eventId` relates it to the existing service event without copying the event object into every position.

`Volunteer` contains only the identity and eligibility data required by this rule set. Contact information and other private profile fields do not belong in assignment validation merely because they may exist elsewhere.

`Assignment` references a position and volunteer by stable ID. It does not copy role names, event times, or display names, so those values continue to have one authoritative source. The status distinguishes a proposed or accepted assignment from a declined one that no longer occupies the position.

The fields and collections are marked `readonly` to make mutation an explicit store responsibility. TypeScript readonly types are a compile-time boundary rather than deep runtime freezing, so the store must still create new arrays and objects during updates.

## Create the Complete Assignment Store

Create `src/app/features/schedule/assignment-store.ts`:

```typescript
import { computed, signal } from '@angular/core';
import { Assignment, MinistryPosition, Volunteer } from './assignment';

export interface AssignmentState {
  readonly positions: readonly MinistryPosition[];
  readonly volunteers: readonly Volunteer[];
  readonly assignments: readonly Assignment[];
}

export type AssignmentValidation = { valid: true } | { valid: false; message: string };

const emptyState: AssignmentState = {
  positions: [],
  volunteers: [],
  assignments: [],
};

export class AssignmentStore {
  private readonly positionsState = signal<readonly MinistryPosition[]>([]);
  private readonly volunteersState = signal<readonly Volunteer[]>([]);
  private readonly assignmentsState = signal<readonly Assignment[]>([]);

  readonly positions = this.positionsState.asReadonly();
  readonly volunteers = this.volunteersState.asReadonly();
  readonly assignments = this.assignmentsState.asReadonly();

  readonly openPositions = computed(() => {
    const assignedIds = new Set(
      this.assignmentsState()
        .filter((assignment) => assignment.status !== 'declined')
        .map((assignment) => assignment.positionId),
    );

    return this.positionsState().filter((position) => !assignedIds.has(position.id));
  });

  constructor(initialState: AssignmentState = emptyState) {
    this.positionsState.set([...initialState.positions]);
    this.volunteersState.set([...initialState.volunteers]);
    this.assignmentsState.set([...initialState.assignments]);
  }

  assign(positionId: string, volunteerId: string): void {
    const result = validateAssignment(
      positionId,
      volunteerId,
      this.positionsState(),
      this.volunteersState(),
      this.assignmentsState(),
    );

    if (!result.valid) {
      throw new Error(result.message);
    }

    this.assignmentsState.update((current) => [
      ...current,
      {
        id: crypto.randomUUID(),
        positionId,
        volunteerId,
        status: 'proposed',
      },
    ]);
  }

  decline(assignmentId: string): void {
    if (!this.assignmentsState().some((assignment) => assignment.id === assignmentId)) {
      throw new Error('Assignment was not found.');
    }

    this.assignmentsState.update((current) =>
      current.map((assignment) =>
        assignment.id === assignmentId
          ? { ...assignment, status: 'declined' as const }
          : assignment,
      ),
    );
  }
}

export function validateAssignment(
  positionId: string,
  volunteerId: string,
  positions: readonly MinistryPosition[],
  volunteers: readonly Volunteer[],
  assignments: readonly Assignment[],
): AssignmentValidation {
  const position = positions.find((item) => item.id === positionId);
  if (!position) {
    return { valid: false, message: 'Position was not found.' };
  }

  const volunteer = volunteers.find((item) => item.id === volunteerId);
  if (!volunteer) {
    return { valid: false, message: 'Volunteer was not found.' };
  }

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

### Seed State Through the Constructor

`AssignmentState` groups the store's three source collections. The constructor accepts that state so tests and future repositories can create a store from known records. It copies each array with spread syntax instead of retaining a caller-owned mutable array reference. Calling `new AssignmentStore()` remains valid because the default state is empty.

The constructor is not a persistence API. Later parts can add explicit loading behavior without changing the meaning of positions, volunteers, or assignments.

### Keep Writable Signals Private

Only the store can call `set` or `update` on `positionsState`, `volunteersState`, and `assignmentsState`. Consumers receive `asReadonly()` views, so they can observe values without bypassing validation commands.

This is encapsulation, not deep immutability. The readonly domain fields and immutable update expressions complete the boundary: successful commands produce new arrays, and a changed assignment produces a new object.

### Derive Open Positions

`openPositions` is computed from positions and assignments instead of stored as another writable collection. Any proposed or accepted assignment occupies its position; a declined assignment does not.

The `Set` makes the final position filter direct and avoids repeatedly scanning assignments for each position. More importantly, the computed signal prevents synchronization bugs. There is no second open-position list that commands must remember to update.

### Validate Before Assigning

`assign` calls the pure validator before changing state. If validation fails, it throws the returned domain message and never reaches `assignmentsState.update`. That ordering is what makes the no-partial-mutation test meaningful.

The successful update creates a new array and a new proposed assignment. `crypto.randomUUID()` is sufficient for this local stage. A later API will own persistent IDs, and optimistic updates can introduce temporary client IDs with explicit reconciliation.

### Decline Without Mutating an Existing Record

`decline` first proves that the assignment exists. It then maps the array and replaces only the matching record with a new object whose status is `declined`. Other assignment objects retain their identity.

The `as const` annotation keeps the new status narrowed to the `declined` member of `Assignment['status']` instead of widening it to an arbitrary string. Once declined, the computed signal automatically returns that position to `openPositions`.

### Keep Validation Pure and Ordered

`AssignmentValidation` is a discriminated union. Callers must check `valid` before reading `message`, and a successful result carries no irrelevant error text.

`validateAssignment` receives all of its data as arguments and performs no signal writes. The checks are deliberately ordered:

1. The position must exist.
2. The volunteer must exist.
3. The volunteer must be eligible for the position's ministry.
4. The position must not have a proposed or accepted assignment.

This first rule set does not detect overlapping assignments or server concurrency. Part 18 will add versioning and conflict handling. Introducing those concerns now would hide the signal and immutable-update concepts Part 4 is designed to teach.

Client validation improves feedback, but a future API must enforce the same invariants because another client can bypass Angular.

## Add the Complete Part 4 Tests

Create `src/app/features/schedule/assignment-store.spec.ts`:

```typescript
import { Assignment, MinistryPosition, Volunteer } from './assignment';
import { AssignmentStore, validateAssignment } from './assignment-store';

describe('validateAssignment', () => {
  const positions: readonly MinistryPosition[] = [
    {
      id: 'sound-1',
      eventId: 'event-1',
      ministry: 'technical',
      roleName: 'Sound operator',
      startsAt: '2026-11-01T09:30:00-05:00',
      endsAt: '2026-11-01T11:30:00-05:00',
    },
  ];

  const volunteers: readonly Volunteer[] = [
    {
      id: 'technical-volunteer',
      displayName: 'Alex',
      eligibleMinistries: ['technical'],
    },
    {
      id: 'welcome-volunteer',
      displayName: 'Jordan',
      eligibleMinistries: ['welcome'],
    },
  ];

  it('rejects an unknown position', () => {
    expect(validateAssignment('missing', 'technical-volunteer', positions, volunteers, [])).toEqual(
      {
        valid: false,
        message: 'Position was not found.',
      },
    );
  });

  it('rejects an unknown volunteer', () => {
    expect(validateAssignment('sound-1', 'missing', positions, volunteers, [])).toEqual({
      valid: false,
      message: 'Volunteer was not found.',
    });
  });

  it('rejects a volunteer outside the required ministry', () => {
    expect(validateAssignment('sound-1', 'welcome-volunteer', positions, volunteers, [])).toEqual({
      valid: false,
      message: 'Volunteer is not eligible for this ministry.',
    });
  });

  it('rejects a position occupied by an active assignment', () => {
    const assignments: readonly Assignment[] = [
      {
        id: 'assignment-1',
        positionId: 'sound-1',
        volunteerId: 'technical-volunteer',
        status: 'accepted',
      },
    ];

    expect(
      validateAssignment('sound-1', 'technical-volunteer', positions, volunteers, assignments),
    ).toEqual({
      valid: false,
      message: 'Position already has an assignment.',
    });
  });

  it('allows a declined assignment to be replaced', () => {
    const assignments: readonly Assignment[] = [
      {
        id: 'assignment-1',
        positionId: 'sound-1',
        volunteerId: 'technical-volunteer',
        status: 'declined',
      },
    ];

    expect(
      validateAssignment('sound-1', 'technical-volunteer', positions, volunteers, assignments),
    ).toEqual({ valid: true });
  });
});

describe('AssignmentStore', () => {
  it('removes a position from the open list after assignment', () => {
    const store = createSeededAssignmentStore();
    const originalAssignments = store.assignments();

    store.assign('sound-1', 'technical-volunteer');

    expect(store.openPositions().map((position) => position.id)).not.toContain('sound-1');
    expect(store.assignments()).not.toBe(originalAssignments);
    expect(store.assignments()).toEqual([
      expect.objectContaining({
        positionId: 'sound-1',
        volunteerId: 'technical-volunteer',
        status: 'proposed',
      }),
    ]);
  });

  it('reopens a position when its assignment is declined', () => {
    const store = createSeededAssignmentStore();
    store.assign('sound-1', 'technical-volunteer');
    const assignmentId = store.assignments()[0].id;

    store.decline(assignmentId);

    expect(store.assignments()[0].status).toBe('declined');
    expect(store.openPositions().map((position) => position.id)).toContain('sound-1');
  });

  it('leaves state unchanged when assignment validation fails', () => {
    const store = createSeededAssignmentStore();
    const originalAssignments = store.assignments();
    const originalOpenPositions = store.openPositions();

    expect(() => store.assign('sound-1', 'welcome-volunteer')).toThrowError(
      'Volunteer is not eligible for this ministry.',
    );
    expect(store.assignments()).toBe(originalAssignments);
    expect(store.openPositions()).toBe(originalOpenPositions);
  });

  it('leaves state unchanged when declining an unknown assignment', () => {
    const store = createSeededAssignmentStore();
    const originalAssignments = store.assignments();

    expect(() => store.decline('missing')).toThrowError('Assignment was not found.');
    expect(store.assignments()).toBe(originalAssignments);
  });
});

function createSeededAssignmentStore(): AssignmentStore {
  return new AssignmentStore({
    positions: [
      {
        id: 'sound-1',
        eventId: 'event-1',
        ministry: 'technical',
        roleName: 'Sound operator',
        startsAt: '2026-11-01T09:30:00-05:00',
        endsAt: '2026-11-01T11:30:00-05:00',
      },
    ],
    volunteers: [
      {
        id: 'technical-volunteer',
        displayName: 'Alex',
        eligibleMinistries: ['technical'],
      },
      {
        id: 'welcome-volunteer',
        displayName: 'Jordan',
        eligibleMinistries: ['welcome'],
      },
    ],
    assignments: [],
  });
}
```

### Test Pure Rules Independently

The first group covers every validation branch. Missing records are checked before eligibility, active assignments occupy a position, and declined assignments allow a replacement. These tests do not create Angular fixtures because the validator has no Angular dependency.

### Test Derived and Immutable Store Behavior

The store tests verify observable outcomes rather than private signals. A successful assignment removes the position from `openPositions`, produces a new assignment array, and records the proposed relationship. Declining that assignment changes its status and reopens the position automatically.

The failure tests save the original array references before executing invalid commands. Reference equality afterward proves the store did not create partial state before throwing.

### Keep Test Data Readable

`createSeededAssignmentStore` centralizes one valid technical position and two volunteers with different eligibility. Each test changes only the state relevant to its assertion, so failures remain easy to diagnose.

The generated assignment ID is intentionally asserted with `expect.objectContaining` rather than a fixed value. The business behavior depends on the relationships and status, not on predicting `crypto.randomUUID()`.

## Add the Files to the Visual Studio Solution

If you use the repository's `ChurchServe.slnx`, add the three files to the existing schedule solution folder:

```xml
<Folder Name="/src/app/features/schedule/">
  <File Path="src\app\features\schedule\assignment-store.spec.ts" />
  <File Path="src\app\features\schedule\assignment-store.ts" />
  <File Path="src\app\features\schedule\assignment.ts" />
  <!-- Existing schedule files remain here. -->
</Folder>
```

The `.slnx` entries affect Visual Studio organization only. Angular discovers TypeScript through its project configuration and imports, not through the solution file.

## Run the Part 4 Checks

Run the full unit suite:

```bash
npm test -- --watch=false
```

The completed Part 4 repository has five test files and twenty tests. Nine of those tests belong to the new assignment store and validator. Your count can be higher if you added coverage in earlier parts.

Then compile the production application:

```bash
npm run build
```

The production screen still looks like Part 3 because Part 4 deliberately adds domain state without prematurely designing an assignment form. The meaningful checkpoint is the passing assignment-state test suite and a clean production build.

## Part 4 Checkpoint

ChurchServe now has explicit scheduling records, encapsulated signal state, computed open positions, immutable commands, and tested assignment rules. The UI remains unchanged by design; later components can consume readonly signals and invoke commands without owning business invariants.

Part 5 will build typed reactive forms for creating service events and positions, including cross-field date validation and safe submission.
