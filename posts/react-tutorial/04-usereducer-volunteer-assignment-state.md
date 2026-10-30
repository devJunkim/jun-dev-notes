---
title: "ChurchServe React Tutorial Part 4: useReducer and Volunteer Assignment State"
excerpt: "Manage ministry positions, volunteers, and assignments with a typed React reducer, immutable transitions, derived state, domain validation, and a custom hook."
category: "React Tutorial"

seo:
  focusKeyword: "React useReducer TypeScript tutorial"
  description: "Use React useReducer with TypeScript to manage volunteer assignments, immutable transitions, derived open positions, validation, and focused tests."
  socialTitle: "ChurchServe Part 4: React useReducer State"
  socialDescription: "Build a tested volunteer-assignment boundary with useReducer, typed actions, immutable updates, derived state, and domain rules."
---

# ChurchServe React Tutorial Part 4: useReducer and Volunteer Assignment State

[Part 3](https://dev.jun-kim.net/2026/10/30/churchserve-react-tutorial-part-3-conditional-rendering-and-schedule-states/) modeled loading, error, empty, and populated schedule results. The populated view still relies on summary counts, however. ChurchServe does not yet represent the ministry positions, volunteers, and assignments that produce those counts.

Part 4 adds that domain state without designing an assignment form prematurely. We will define readonly records, validate scheduling commands, update related collections through a pure reducer, derive open positions, and expose the result through a small custom hook.

> **Quick answer:** Put related assignment collections behind `useReducer`, describe changes with a discriminated union of actions, keep the reducer pure and immutable, calculate open positions from source records, and validate every state transition in the reducer even when the UI also validates before dispatch.

## What You Will Build

Part 4 adds four files to the existing schedule feature:

```text
src/features/schedule/
├── assignment.ts
├── assignmentState.test.ts
├── assignmentState.ts
├── useAssignmentState.ts
└── existing schedule files
```

The completed boundary supports two commands:

- Propose a volunteer for an eligible, open ministry position.
- Decline an existing assignment so its position becomes open again.

The browser still looks like Part 3. That is intentional. Domain rules should be stable and tested before form controls, API calls, and optimistic feedback depend on them.

## Define the Assignment Records

Create `src/features/schedule/assignment.ts`:

```ts
export type MinistryPosition = {
  readonly id: string
  readonly eventId: string
  readonly ministry: string
  readonly roleName: string
  readonly startsAt: string
  readonly endsAt: string
}

export type Volunteer = {
  readonly id: string
  readonly displayName: string
  readonly eligibleMinistries: readonly string[]
}

export type Assignment = {
  readonly id: string
  readonly positionId: string
  readonly volunteerId: string
  readonly status: 'proposed' | 'accepted' | 'declined'
}
```

`MinistryPosition` represents one staffing need for one service event. The time interval belongs to the position because future conflict rules need to compare the volunteer's assignments, including positions that may not share an event.

`Volunteer` includes only the information required by this rule set. Contact details and other private profile fields do not need to travel through assignment validation.

`Assignment` relates records through stable IDs instead of copying display names, event details, or role names. A proposed or accepted assignment occupies its position. A declined assignment remains part of the history but no longer occupies the position.

The `readonly` modifiers establish compile-time intent. They do not freeze objects at runtime, so the reducer must still create new arrays and objects rather than mutating existing state.

## Model State and Actions

Create `src/features/schedule/assignmentState.ts` and begin with the state, actions, and validation result:

```ts
import type { Assignment, MinistryPosition, Volunteer } from './assignment'

export type AssignmentState = {
  readonly positions: readonly MinistryPosition[]
  readonly volunteers: readonly Volunteer[]
  readonly assignments: readonly Assignment[]
}

export type AssignmentAction =
  | {
      type: 'assignment/proposed'
      assignmentId: string
      positionId: string
      volunteerId: string
    }
  | { type: 'assignment/declined'; assignmentId: string }

export type AssignmentValidation =
  | { valid: true }
  | { valid: false; message: string }
```

The three collections form one related state boundary. Positions and volunteers are source records, while assignments describe their current relationships.

`AssignmentAction` is a discriminated union. Each action carries the minimum data needed to describe something that happened. Components do not construct the next assignment array themselves; they dispatch intent and let the reducer enforce the transition.

The proposed action already contains an assignment ID. Generating that ID is not the reducer's job because a reducer must be pure: the same state and action should produce the same next state. The custom hook will generate the client ID before dispatch.

## Validate the Domain Rules

Add the pure validator to `assignmentState.ts`:

```ts
export function validateAssignment(
  positionId: string,
  volunteerId: string,
  state: AssignmentState,
): AssignmentValidation {
  const position = state.positions.find(({ id }) => id === positionId)
  if (!position) {
    return { valid: false, message: 'Position was not found.' }
  }

  const volunteer = state.volunteers.find(({ id }) => id === volunteerId)
  if (!volunteer) {
    return { valid: false, message: 'Volunteer was not found.' }
  }

  if (!volunteer.eligibleMinistries.includes(position.ministry)) {
    return {
      valid: false,
      message: 'Volunteer is not eligible for this ministry.',
    }
  }

  const occupied = state.assignments.some(
    (assignment) =>
      assignment.positionId === positionId &&
      assignment.status !== 'declined',
  )

  return occupied
    ? { valid: false, message: 'Position already has an assignment.' }
    : { valid: true }
}
```

The checks are ordered deliberately:

1. The position must exist.
2. The volunteer must exist.
3. The volunteer must be eligible for the position's ministry.
4. The position must not already have a proposed or accepted assignment.

The function receives all of its inputs and changes nothing. A component can use the result for immediate feedback, a reducer can use it to protect state, and tests can cover the rules without rendering React.

This first rule set does not detect overlapping service times or concurrent changes from another user. Those rules belong in later checkpoints. A future API must also enforce every invariant because client-side validation can be bypassed.

## Write the Pure Reducer

Add the reducer above the validator in `assignmentState.ts`:

```ts
export function assignmentReducer(
  state: AssignmentState,
  action: AssignmentAction,
): AssignmentState {
  switch (action.type) {
    case 'assignment/proposed': {
      const validation = validateAssignment(
        action.positionId,
        action.volunteerId,
        state,
      )

      if (!validation.valid) {
        return state
      }

      return {
        ...state,
        assignments: [
          ...state.assignments,
          {
            id: action.assignmentId,
            positionId: action.positionId,
            volunteerId: action.volunteerId,
            status: 'proposed',
          },
        ],
      }
    }

    case 'assignment/declined': {
      if (!state.assignments.some(({ id }) => id === action.assignmentId)) {
        return state
      }

      return {
        ...state,
        assignments: state.assignments.map((assignment) =>
          assignment.id === action.assignmentId
            ? { ...assignment, status: 'declined' as const }
            : assignment,
        ),
      }
    }
  }
}
```

The reducer revalidates proposed assignments even though the hook will validate before dispatch. The pre-dispatch check can provide a useful message from the current render snapshot; the reducer remains the final client-side invariant boundary when queued actions are processed against successive states.

An invalid or stale action returns the same state reference. A successful proposal returns a new state object, a new assignment array, and one new assignment record. Declining maps the collection and replaces only the matching record.

The reducer does not call an API, generate an ID, write storage, or update the clock. React can call reducers while calculating the next render, so side effects would make behavior unpredictable and can run more than expected in development checks.

## Derive Open Positions

Add this selector to `assignmentState.ts`:

```ts
export function selectOpenPositions(
  state: AssignmentState,
): readonly MinistryPosition[] {
  const occupiedPositionIds = new Set(
    state.assignments
      .filter(({ status }) => status !== 'declined')
      .map(({ positionId }) => positionId),
  )

  return state.positions.filter(
    ({ id }) => !occupiedPositionIds.has(id),
  )
}
```

Do not store `openPositions` as another writable collection. It is completely determined by positions and assignments. Storing both the source records and their summary creates synchronization work for every command and another opportunity for disagreement.

The `Set` makes the final lookup direct and documents that position identity, rather than array position, defines occupancy.

## Expose the Reducer Through a Custom Hook

Create `src/features/schedule/useAssignmentState.ts`:

```ts
import { useReducer } from 'react'
import type { AssignmentState, AssignmentValidation } from './assignmentState'
import {
  assignmentReducer,
  selectOpenPositions,
  validateAssignment,
} from './assignmentState'

type AssignmentIdFactory = () => string

const createAssignmentId: AssignmentIdFactory = () => crypto.randomUUID()

function copyInitialState(state: AssignmentState): AssignmentState {
  return {
    positions: [...state.positions],
    volunteers: [...state.volunteers],
    assignments: [...state.assignments],
  }
}

export function useAssignmentState(
  initialState: AssignmentState,
  idFactory: AssignmentIdFactory = createAssignmentId,
) {
  const [state, dispatch] = useReducer(
    assignmentReducer,
    initialState,
    copyInitialState,
  )

  function assign(
    positionId: string,
    volunteerId: string,
  ): AssignmentValidation {
    const validation = validateAssignment(positionId, volunteerId, state)

    if (!validation.valid) {
      return validation
    }

    dispatch({
      type: 'assignment/proposed',
      assignmentId: idFactory(),
      positionId,
      volunteerId,
    })

    return validation
  }

  function decline(assignmentId: string): boolean {
    if (!state.assignments.some(({ id }) => id === assignmentId)) {
      return false
    }

    dispatch({ type: 'assignment/declined', assignmentId })
    return true
  }

  return {
    state,
    openPositions: selectOpenPositions(state),
    assign,
    decline,
  }
}
```

`useReducer` connects the pure transition function to React rendering. The lazy initializer copies the three input arrays once, so the hook does not retain caller-owned array references. The records are readonly, making a shallow collection copy sufficient for this boundary.

The hook translates convenient commands into typed actions. `assign` returns the validation result so a future component can display a domain message without learning the validator's inputs. `decline` returns `false` when the assignment is not in the current render snapshot.

`crypto.randomUUID()` is acceptable for a local proposed record. A future API will own persistent IDs. Passing an ID factory also keeps hook tests deterministic without mocking the global crypto object.

`openPositions` is recalculated from the current state during rendering. The collection is small, and memoization would add complexity without a demonstrated performance problem. If profiling later shows that this selector is expensive, its pure boundary makes optimization straightforward.

## Test Validation and State Transitions

Create `src/features/schedule/assignmentState.test.ts`. Start with shared records and the validation tests:

```ts
import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { Assignment, MinistryPosition, Volunteer } from './assignment'
import {
  assignmentReducer,
  selectOpenPositions,
  validateAssignment,
  type AssignmentState,
} from './assignmentState'
import { useAssignmentState } from './useAssignmentState'

const positions: readonly MinistryPosition[] = [
  {
    id: 'sound-1',
    eventId: 'event-1',
    ministry: 'technical',
    roleName: 'Sound operator',
    startsAt: '2026-11-01T09:30:00-05:00',
    endsAt: '2026-11-01T11:30:00-05:00',
  },
]

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
]

describe('validateAssignment', () => {
  it('rejects an unknown position', () => {
    expect(
      validateAssignment('missing', 'technical-volunteer', createState()),
    ).toEqual({ valid: false, message: 'Position was not found.' })
  })

  it('rejects an unknown volunteer', () => {
    expect(validateAssignment('sound-1', 'missing', createState())).toEqual({
      valid: false,
      message: 'Volunteer was not found.',
    })
  })

  it('rejects a volunteer outside the required ministry', () => {
    expect(
      validateAssignment('sound-1', 'welcome-volunteer', createState()),
    ).toEqual({
      valid: false,
      message: 'Volunteer is not eligible for this ministry.',
    })
  })

  it('rejects a position occupied by an active assignment', () => {
    expect(
      validateAssignment(
        'sound-1',
        'technical-volunteer',
        createState([
          {
            id: 'assignment-1',
            positionId: 'sound-1',
            volunteerId: 'technical-volunteer',
            status: 'accepted',
          },
        ]),
      ),
    ).toEqual({
      valid: false,
      message: 'Position already has an assignment.',
    })
  })

  it('allows a declined assignment to be replaced', () => {
    expect(
      validateAssignment(
        'sound-1',
        'technical-volunteer',
        createState([
          {
            id: 'assignment-1',
            positionId: 'sound-1',
            volunteerId: 'technical-volunteer',
            status: 'declined',
          },
        ]),
      ),
    ).toEqual({ valid: true })
  })
})
```

These five cases document the complete rule order. They remain fast because the validator has no React or browser dependency.

## Test the Reducer and Hook Boundary

Continue the same test file:

```ts
describe('assignmentReducer', () => {
  it('proposes an assignment and derives no open positions', () => {
    const state = createState()
    const nextState = assignmentReducer(state, {
      type: 'assignment/proposed',
      assignmentId: 'assignment-1',
      positionId: 'sound-1',
      volunteerId: 'technical-volunteer',
    })

    expect(nextState).not.toBe(state)
    expect(nextState.assignments).toEqual([
      {
        id: 'assignment-1',
        positionId: 'sound-1',
        volunteerId: 'technical-volunteer',
        status: 'proposed',
      },
    ])
    expect(selectOpenPositions(nextState)).toEqual([])
  })

  it('declines an assignment and reopens its position', () => {
    const state = createState([
      {
        id: 'assignment-1',
        positionId: 'sound-1',
        volunteerId: 'technical-volunteer',
        status: 'proposed',
      },
    ])

    const nextState = assignmentReducer(state, {
      type: 'assignment/declined',
      assignmentId: 'assignment-1',
    })

    expect(nextState.assignments[0].status).toBe('declined')
    expect(selectOpenPositions(nextState)).toEqual(positions)
  })

  it('preserves state when a proposed assignment is invalid', () => {
    const state = createState()
    const nextState = assignmentReducer(state, {
      type: 'assignment/proposed',
      assignmentId: 'assignment-1',
      positionId: 'sound-1',
      volunteerId: 'welcome-volunteer',
    })

    expect(nextState).toBe(state)
  })

  it('preserves state when the declined assignment does not exist', () => {
    const state = createState()
    const nextState = assignmentReducer(state, {
      type: 'assignment/declined',
      assignmentId: 'missing',
    })

    expect(nextState).toBe(state)
  })
})

describe('useAssignmentState', () => {
  it('dispatches assignment commands through the reducer', () => {
    const { result } = renderHook(() =>
      useAssignmentState(createState(), () => 'assignment-1'),
    )

    act(() => {
      expect(result.current.assign('sound-1', 'technical-volunteer')).toEqual({
        valid: true,
      })
    })

    expect(result.current.openPositions).toEqual([])
    expect(result.current.state.assignments[0].id).toBe('assignment-1')

    act(() => {
      expect(result.current.decline('assignment-1')).toBe(true)
    })

    expect(result.current.openPositions).toEqual(positions)
  })
})

function createState(
  assignments: readonly Assignment[] = [],
): AssignmentState {
  return { positions, volunteers, assignments }
}
```

The reducer tests assert both value changes and reference behavior. Successful actions return new state. Rejected actions return the original reference, proving that no partial mutation occurred.

The hook test is intentionally small. The pure tests already cover every branch; this test proves that the hook dispatches commands, uses the deterministic ID factory, and exposes fresh derived state after React renders the update.

## Update the Project Notes

Add Part 4 to `README.md`:

```md
## Part 4

The `part_4` branch adds readonly volunteer-assignment records, pure validation
and reducer logic, immutable state transitions, derived open positions, and a
custom `useAssignmentState` hook with focused domain and hook tests.
```

## Run the Part 4 Quality Gate

Run all repository checks:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The completed Part 4 checkpoint has four test files and twenty passing tests:

```text
Test Files  4 passed (4)
Tests       20 passed (20)
```

The production bundle still renders the Part 3 schedule. Part 4 is a domain-state checkpoint, so the lack of a new screen is expected rather than unfinished work.

## Production Boundaries

This reducer protects in-memory client state, not the system of record:

- The API must repeat authorization, eligibility, occupancy, and future conflict checks.
- A database constraint or concurrency strategy must prevent two clients from occupying one position.
- Persistent IDs should come from the server; client IDs need explicit reconciliation during optimistic updates.
- API failures should become explicit UI state rather than throwing from the reducer.
- Larger record sets may need indexed selectors or server-side summaries, but optimization should follow measurement.

The hook currently owns one local reducer instance. If several distant routes eventually need the same client state, its value and commands can be provided through context or replaced by a server-state boundary. The domain types, validation function, reducer actions, and selector remain useful regardless of that delivery mechanism.

React's official documentation explains when to [extract state logic into a reducer](https://react.dev/learn/extracting-state-logic-into-a-reducer) and why [redundant derived values should stay out of state](https://react.dev/learn/choosing-the-state-structure).

## Part 4 Checkpoint

ChurchServe now has explicit ministry positions, volunteers, and assignments. A pure reducer owns immutable transitions, a validator protects the first scheduling rules, a selector derives open positions, and a custom hook connects that domain boundary to React.

Confirm that:

- Proposed and accepted assignments occupy a position.
- Declined assignments leave the position open.
- Unknown, ineligible, and duplicate assignment commands preserve existing state.
- The reducer performs no ID generation or side effects.
- Open positions are derived rather than stored independently.
- All twenty tests and the production build pass.

Part 5 will add a typed service-event form with cross-field time validation and safe submission behavior.
