---
title: "ChurchServe React Tutorial Part 22-2: Build a Permission-Aware React UI"
excerpt: "Validate session permissions, guard lazy routes, hide unavailable actions, and clear cached state when effective capabilities change."
category: "React Tutorial"

seo:
  focusKeyword: "React permission route guard"
  description: "Build permission-aware React routes, validate session capabilities, hide unavailable actions, and clear authorization-bound caches."
  socialTitle: "ChurchServe React Part 22-2: Permission-Aware UI"
  socialDescription: "Make React navigation and actions match server permissions while keeping the API as the security boundary."
---

# ChurchServe React Tutorial Part 22-2: Build a Permission-Aware React UI

Part 22-1 made the API authoritative. A reader can view schedules but receives `403 Forbidden` when attempting scheduler operations. React should present that boundary honestly instead of advertising actions that are guaranteed to fail.

Client permission checks improve navigation and reduce predictable failures. They are not security controls. Users can modify browser code or call endpoints directly, so every restriction here depends on the server policies remaining in place.

> **Quick answer:** Parse the server permission list into a closed TypeScript union, put reusable permission gates around lazy route branches, derive action visibility from the current session, and include sorted permissions in cache identity so downgraded sessions cannot reuse privileged state.

## Define a closed permission vocabulary

Extend `session.ts`:

```typescript
export const PERMISSIONS = {
  scheduleRead: 'schedule.read',
  scheduleManage: 'schedule.manage',
  volunteerRead: 'volunteer.read',
  volunteerManage: 'volunteer.manage',
} as const

export type Permission =
  (typeof PERMISSIONS)[keyof typeof PERMISSIONS]

export type CurrentUser = {
  readonly id: string
  readonly email: string
  readonly displayName: string
  readonly roles: readonly string[]
  readonly permissions: readonly Permission[]
}
```

The runtime object and string-literal union share one source. Components cannot accidentally ask for an arbitrary permission name.

Add focused helpers:

```typescript
export function isPermission(value: unknown): value is Permission {
  return (
    typeof value === 'string' &&
    Object.values(PERMISSIONS).includes(value as Permission)
  )
}

export function hasPermission(
  user: CurrentUser | undefined,
  permission: Permission,
) {
  return user?.permissions.includes(permission) ?? false
}
```

## Reject an invalid session contract

The API response remains untrusted JSON. Require an explicit permission array and validate every value:

```typescript
if (
  !isRecord(value) ||
  !isNonEmptyString(value.id) ||
  !isNonEmptyString(value.email) ||
  !isNonEmptyString(value.displayName) ||
  !Array.isArray(value.roles) ||
  !value.roles.every(isNonEmptyString) ||
  !Array.isArray(value.permissions) ||
  !value.permissions.every(isPermission)
) {
  throw new InvalidSessionResponseError()
}
```

Do not silently discard unknown permission strings. Rejecting `billing.read` exposes an incompatible deployment instead of leaving the interface in an ambiguous state. Tests cover both a missing array and an unknown value.

## Create a reusable permission gate

`RequirePermission` reads the restored session and renders an outlet only when the capability is present:

```tsx
export function RequirePermission({ permission }: RequirePermissionProps) {
  const { state } = useSession()
  const location = useLocation()

  if (state.status !== 'authenticated') {
    const returnTo =
      `${location.pathname}${location.search}${location.hash}`
    return (
      <Navigate
        replace
        to={`/sign-in?returnTo=${encodeURIComponent(returnTo)}`}
      />
    )
  }

  return hasPermission(state.user, permission)
    ? <Outlet />
    : <Navigate replace to="/unavailable" />
}
```

Missing authentication retains the safe return URL behavior from Part 14. An authenticated user without permission reaches a neutral unavailable page rather than a sign-in loop.

## Put gates around lazy route branches

Use small named gate components so route requirements remain visible:

```tsx
export function RequireScheduleManage() {
  return (
    <RequirePermission permission={PERMISSIONS.scheduleManage} />
  )
}
```

The schedule branch first requires read access. Its creation child adds management. This abbreviated route tree omits unrelated fallback and detail configuration:

```tsx
{
  Component: RequireScheduleRead,
  children: [
    {
      lazy: async () => ({ Component: (await schedule()).Layout }),
      children: [
        { path: 'schedule', lazy: async () => (await schedule()).list },
        {
          Component: RequireScheduleManage,
          children: [
            { path: 'schedule/new', lazy: async () => (await schedule()).create },
          ],
        },
        { path: 'schedule/:eventId', lazy: async () => (await schedule()).detail },
      ],
    },
  ],
}
```

The volunteer placeholder route requires `volunteer.read`. The feature remains a placeholder; permission-aware routing does not pretend the volunteer workflow is already implemented.

## Derive navigation and action visibility

The application shell renders links from the authenticated user’s effective capabilities:

```tsx
{hasPermission(state.user, PERMISSIONS.scheduleRead) && (
  <NavLink to="/schedule">Schedule</NavLink>
)}

{hasPermission(state.user, PERMISSIONS.volunteerRead) && (
  <NavLink to="/volunteers">Volunteers</NavLink>
)}
```

`HomePage` applies the same rule to its calls to action. `SchedulePage` omits **Create service** without `schedule.manage`.

The route gate remains necessary. Hiding a link does not prevent a user from entering `/schedule/new` manually.

## Keep the assignment board useful for readers

The detail page derives management permission and passes it to the board:

```tsx
const canManageSchedule =
  session.status === 'authenticated' &&
  hasPermission(session.user, PERMISSIONS.scheduleManage)

<AssignmentBoardPanel
  event={event}
  canManageSchedule={canManageSchedule}
/>
```

Existing assignments remain visible to a reader. An unfilled position renders neutral text instead of a volunteer selector:

```tsx
{assignment ? (
  <ConfirmedAssignment assignment={assignment} />
) : canManageSchedule ? (
  <AssignmentControls position={position} />
) : (
  <p className="assigned-volunteer">Unfilled</p>
)}
```

This avoids presenting a guaranteed failure while preserving the schedule information a reader is allowed to inspect.

## Bind cached data to effective permissions

The feature stores retain schedule and assignment data. A capability change for the same user must clear that state.

`AuthorizationCacheBoundary` derives identity from user ID and a sorted permission set:

```tsx
const identity =
  state.status === 'authenticated'
    ? `${state.user.id}|${[...state.user.permissions]
        .sort()
        .join(',')}`
    : state.status

const previousIdentity = useRef(identity)

useEffect(() => {
  if (previousIdentity.current === identity) return
  previousIdentity.current = identity
  scheduleStore.clear()
  assignmentStore.clear()
}, [assignmentStore, identity, scheduleStore])
```

Sorting makes equivalent sets stable even if response ordering changes. Adding or removing a permission produces a new identity and clears events, positions, volunteers, assignments, and active requests.

Sign-out already unmounts the protected feature and disposes the stores. This boundary also protects a future in-place session refresh where the user ID stays the same but capabilities change.

## Test routing and presentation separately

React tests now prove that:

- a reader sees the schedule but not **Create service**;
- navigating directly to `/schedule/new` reaches the unavailable page;
- navigation omits capabilities the session does not grant;
- assignment details remain readable without selectors or an Assign button;
- changing permissions for the same user clears both feature stores;
- absent and unknown session permissions fail DTO validation.

These assertions prove what React presents. Part 22-1’s direct API calls remain the authorization proof.

## Result

ChurchServe’s React interface now mirrors the server’s capabilities. It validates the permission contract, guards deep links, hides unavailable actions, preserves read-only assignment value, and prevents cached data from crossing an authorization change.

Part 22-3 completes the chapter with privacy boundaries, safe rendering tests, and production security decisions.
