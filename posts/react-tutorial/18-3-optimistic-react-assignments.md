---
title: "ChurchServe React Tutorial Part 18-3: Optimistic React Assignments"
excerpt: "Build an external assignment store and accessible UI that predicts writes, confirms matching requests, rolls back failures, and reconciles conflicts."
category: "React Tutorial"

seo:
  focusKeyword: "React optimistic update useSyncExternalStore"
  description: "Implement optimistic React assignments with useSyncExternalStore, runtime DTO validation, request identity, rollback, and conflict reconciliation."
  socialTitle: "ChurchServe React Part 18-3: Optimistic Assignments"
  socialDescription: "Make volunteer assignment feel instant while preserving honest pending, failure, confirmation, and concurrency-conflict states."
---

# ChurchServe React Tutorial Part 18-3: Optimistic React Assignments

Part 18-2 created a versioned, idempotent assignment command. React can now predict the result immediately, but it must distinguish that prediction from server-confirmed state and must not let one failed request roll back unrelated work.

> **Quick answer:** Put assignment-board request policy in a feature-scoped external store. Add a pending item before sending the command, tag it with the same client request ID, replace only that item on success, remove only that item on an ordinary failure, and replace the entire board when a `409 Conflict` supplies newer server truth. Invalidate older in-flight callbacks after reconciliation so late responses cannot overwrite the authoritative snapshot.

## Keep the feature boundary from Part 17

The lazy schedule entry point now creates both repositories and both stores:

```tsx
export function createScheduleFeature(
  repository: ServiceEventRepository =
    new HttpServiceEventRepository(),
  assignmentRepository: AssignmentRepository =
    new HttpAssignmentRepository(),
): ScheduleFeature {
  const store = new ScheduleStore(repository)
  const assignmentStore = new AssignmentStore(
    assignmentRepository,
    (eventId, assignedPositions, version) =>
      store.updateStaffing(eventId, assignedPositions, version),
  )

  function ScheduleFeatureLayout() {
    return (
      <ScheduleStoreProvider store={store}>
        <AssignmentStoreProvider store={assignmentStore}>
          <Outlet />
        </AssignmentStoreProvider>
      </ScheduleStoreProvider>
    )
  }
}
```

The root router still knows only the lazy feature contract. Assignment HTTP, state, and lifecycle remain private to schedule. Tests inject controlled repositories through the same factory rather than replacing the route tree.

## Model confirmed and pending assignments

The browser adds synchronization state that the API does not need:

```typescript
export type Assignment = {
  readonly id: string
  readonly positionId: string
  readonly volunteerId: string
  readonly volunteerDisplayName: string
  readonly status: 'accepted' | 'declined'
  readonly syncStatus: 'pending' | 'confirmed'
  readonly clientRequestId?: string
}
```

`status` is business state. `syncStatus` says whether this browser prediction has been confirmed. `clientRequestId` connects one pending item to one HTTP command and response.

The board carries its concurrency and aggregate values:

```typescript
export type AssignmentBoard = {
  readonly eventId: string
  readonly eventVersion: number
  readonly assignedPositions: number
  readonly positions: readonly MinistryPosition[]
  readonly volunteers: readonly Volunteer[]
  readonly assignments: readonly Assignment[]
}
```

## Validate JSON at runtime

TypeScript annotations disappear at runtime. A `fetch` response typed as `AssignmentBoard` is still untrusted JSON.

Parse the outer board and every nested record:

```typescript
export function parseAssignmentBoard(value: unknown): AssignmentBoard {
  if (
    !isRecord(value) ||
    !isNonEmptyString(value.eventId) ||
    !isPositiveInteger(value.eventVersion) ||
    !isNonNegativeInteger(value.assignedPositions) ||
    !Array.isArray(value.positions) ||
    !Array.isArray(value.volunteers) ||
    !Array.isArray(value.assignments)
  ) {
    throw new InvalidAssignmentResponseError()
  }

  return {
    eventId: value.eventId,
    eventVersion: value.eventVersion,
    assignedPositions: value.assignedPositions,
    positions: value.positions.map(parsePosition),
    volunteers: value.volunteers.map(parseVolunteer),
    assignments: value.assignments.map(parseAssignment),
  }
}
```

`parsePosition` verifies valid ordered timestamps. `parseVolunteer` verifies every ministry string. `parseAssignment` permits only known statuses and adds `syncStatus: 'confirmed'` because values from the API are no longer predictions.

Controlled parsing turns a server regression into a recoverable board error instead of corrupting store state.

## Keep HTTP details behind a repository

The client contract exposes two operations:

```typescript
export interface AssignmentRepository {
  loadBoard(
    eventId: string,
    signal?: AbortSignal,
  ): Promise<AssignmentBoard>

  assign(
    command: AssignVolunteerCommand,
    signal?: AbortSignal,
  ): Promise<AssignVolunteerResult>
}
```

`HttpAssignmentRepository` encodes the event ID, sends same-origin credentials, and adds the antiforgery token:

```typescript
const response = await fetcher(
  `${baseUrl}/${encodeURIComponent(command.eventId)}/assignments`,
  {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': readAntiforgeryToken(cookieSource),
    },
    body: JSON.stringify({
      positionId: command.positionId,
      volunteerId: command.volunteerId,
      expectedEventVersion: command.expectedEventVersion,
      clientRequestId: command.clientRequestId,
    }),
  },
)
```

When the status is `409`, parse the conflict body and throw a domain-specific error:

```typescript
if (response.status === 409) {
  const conflict = parseAssignmentConflict(payload)
  throw new AssignmentConflictError(
    conflict.message,
    conflict.board,
  )
}
```

The store understands authoritative conflicts without importing `Response`, status codes, cookies, or transport parsing.

## Build a feature-scoped external store

Like the schedule store from Part 15, `AssignmentStore` exposes stable methods for React's [`useSyncExternalStore`](https://react.dev/reference/react/useSyncExternalStore):

```typescript
readonly subscribe = (listener: Listener) => {
  this.listeners.add(listener)
  return () => this.listeners.delete(listener)
}

readonly getSnapshot = () => this.snapshot
```

Snapshots form a discriminated union: `idle`, `loading`, `error`, or `ready`. A ready snapshot contains the board and an optional success, error, or conflict notice.

`load(eventId)` aborts obsolete work, records a generation, and applies the board only when both still match. `dispose()` aborts loading, increments the generation, and resets state when the lazy schedule layout unmounts.

## Insert the prediction before awaiting HTTP

Validate against the current ready board, then create one request identity:

```typescript
const clientRequestId = idFactory()
const pending: Assignment = {
  id: `pending-${clientRequestId}`,
  positionId,
  volunteerId,
  volunteerDisplayName: volunteer.displayName,
  status: 'accepted',
  syncStatus: 'pending',
  clientRequestId,
}

setSnapshot({
  ...current,
  assignments: [...current.assignments, pending],
  notice: undefined,
})
```

The row updates before the promise resolves. The command uses the same ID and the version from the snapshot that produced the prediction:

```typescript
repository.assign({
  eventId: current.eventId,
  positionId,
  volunteerId,
  expectedEventVersion: current.eventVersion,
  clientRequestId,
})
```

Local validation blocks an unknown position, unknown volunteer, ineligible ministry, or already occupied position. The server repeats every rule against current data.

## Confirm only the matching pending item

On success, first ensure the response still belongs to the current store generation and that its pending item still exists. Then replace by request identity:

```typescript
assignments: snapshot.assignments.map((item) =>
  item.clientRequestId === clientRequestId
    ? result.assignment
    : item,
)
```

Update the confirmed event version and staffing count from the response. A callback also updates `ScheduleStore` so returning to the list does not resurrect a stale open-position count:

```typescript
store.updateStaffing(eventId, assignedPositions, version)
```

That method updates both the feature cache and any ready list snapshot, but ignores a version older than the cached event.

## Roll back one failure, reconcile a conflict

An ordinary network or server failure has no newer board. Remove only its pending request:

```typescript
assignments: snapshot.assignments.filter(
  (item) => item.clientRequestId !== clientRequestId,
)
```

Another pending position remains visible. This is why filtering by position or clearing all pending state would be incorrect.

A conflict is different. The server supplied authoritative state:

```typescript
if (error instanceof AssignmentConflictError) {
  generation++
  applyBoard(error.board, {
    kind: 'conflict',
    message: `${error.message} The latest assignments are shown.`,
  })
  return
}
```

Incrementing the generation invalidates callbacks for other requests launched from the discarded snapshot. Without that guard, a late success could update the reconciled version or staffing count even after its pending item disappeared.

## Render honest accessible states

`AssignmentBoardPanel` starts loading in an effect keyed by event ID. It renders explicit loading, retryable error, empty-position, ready, pending, confirmed, and notice states.

Each open position uses an associated label and select:

```tsx
<label htmlFor={`volunteer-${position.id}`}>
  Volunteer
</label>
<select
  id={`volunteer-${position.id}`}
  value={selectedVolunteer}
  onChange={(event) =>
    setSelectedVolunteers((current) => ({
      ...current,
      [position.id]: event.target.value,
    }))
  }
>
  <option value="">Choose a volunteer</option>
  {eligibleVolunteers.map((volunteer) => (
    <option key={volunteer.id} value={volunteer.id}>
      {volunteer.displayName}
    </option>
  ))}
</select>
```

An occupied row displays the volunteer and one honest status:

```tsx
<span className={assignment.syncStatus === 'pending' ? 'saving' : undefined}>
  {assignment.syncStatus === 'pending' ? 'Saving…' : 'Confirmed'}
</span>
```

Success notices use `role="status"`; failures and conflicts use `role="alert"`. The interface does not announce a prediction as saved.

The panel also distinguishes the event-level assigned count from the configured board rows. This matters while position authoring remains incomplete: seeded events can contain staffing aggregates beyond the positions introduced in this tutorial.

## Test asynchronous interleavings deliberately

The store tests control promise completion rather than relying on timers. They verify that:

1. Loading exposes a board and filters volunteers by ministry.
2. Assignment immediately inserts a pending item with the current version and request ID.
3. Success replaces only the matching pending item.
4. Failure removes only its own prediction.
5. Conflict replaces the board and invalidates older callbacks.
6. Local eligibility failure never calls the repository.

The HTTP tests verify URL encoding, credentials, antiforgery headers, payload shape, runtime parsing, conflict conversion, and non-conflict HTTP errors.

Component tests select a volunteer through its accessible label, click Assign, observe `Saving…`, resolve the controlled repository promise, and observe `Confirmed`, the next version, and the status message.

## Run the complete checkpoint

Run frontend verification and coverage:

```bash
npm run verify
npm run test:coverage
```

Run the API and migration checks:

```bash
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj \
  --configuration Release
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The validated Part 18 checkpoint reports:

- architecture boundaries pass across 47 production TypeScript files;
- all 76 React tests across 15 files pass;
- all 16 ASP.NET Core integration tests pass against SQLite;
- lint and strict application/test type checking pass;
- the production build transforms 132 modules and preserves separate lazy schedule and volunteer chunks;
- V8 coverage reports 90.54% statements, 83.1% branches, 93.62% functions, and 91.73% lines;
- the API builds with zero warnings and errors;
- EF Core reports no pending model changes.

## Part 18 checkpoint

ChurchServe now has a complete optimistic assignment path for seeded positions:

- the database persists positions, eligibility, assignments, request IDs, and event versions;
- the API makes retries idempotent and stale writes recoverable;
- React validates every response before it reaches state;
- a feature-scoped external store owns loading and optimistic transitions;
- one request identity controls confirmation and rollback;
- conflicts replace local predictions with server truth;
- late callbacks cannot overwrite a reconciled snapshot;
- schedule list state receives confirmed staffing updates;
- accessible UI distinguishes loading, saving, confirmation, failure, and conflict.

Newly created events still have no position-authoring workflow. That requires its own contracts, rules, persistence commands, and interface rather than silently manufacturing generic positions during event creation.
