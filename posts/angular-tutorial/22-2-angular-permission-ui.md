---
title: "ChurchServe Angular Tutorial Part 22-2: Build a Permission-Aware Angular UI"
excerpt: "Validate session permissions, guard lazy routes, hide unavailable actions, and clear cached state when a user's capabilities change."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular permission route guard"
  description: "Build an Angular permission route guard, validate session capabilities, hide unavailable actions, and invalidate authorization-bound caches."
  socialTitle: "ChurchServe Part 22-2: Permission-Aware Angular UI"
  socialDescription: "Make Angular navigation and actions match server permissions while keeping the API as the true security boundary."
---

# ChurchServe Angular Tutorial Part 22-2: Build a Permission-Aware Angular UI

Part 22-1 made the API authoritative. A reader can load schedules but receives `403 Forbidden` when attempting a scheduler operation. The Angular application should now present that boundary honestly: do not advertise routes and controls that the current session cannot use.

Client-side permission checks improve navigation and reduce predictable failures. They are not security controls. Users can modify browser code or call an endpoint directly, so every restriction in this article depends on the API policies from Part 22-1 remaining in place.

> **Quick answer:** Parse the server's permission list into a closed TypeScript union, restore the session before evaluating a route guard, derive action visibility from the authenticated user, and include effective permissions in cache identity so downgraded sessions cannot reuse privileged data.

## What Part 22-2 Changes

```text
src/app/
  core/auth/
    current-user.ts
    permission.guard.ts       # new
    session-dto.ts
    session-dto.spec.ts       # new
    session-store.spec.ts
  features/schedule/
    assignment-store.ts
    assignment-store.spec.ts
    schedule-store.ts
    schedule-store.spec.ts
    schedule.html
    schedule.routes.ts
    schedule.spec.ts
    schedule.ts
    service-event-detail/
      service-event-detail.html
      service-event-detail.spec.ts
      service-event-detail.ts
  features/volunteers/
    volunteers.routes.ts
  app.html
  app.spec.ts
  app.ts
  app.routes.spec.ts
```

The repository and HTTP contracts from earlier parts remain unchanged. Permission behavior sits in the session, routing, presentation, and cache boundaries.

## Define a Closed Permission Vocabulary

Add the permission constants and helpers to `src/app/core/auth/current-user.ts`:

```typescript
export const PERMISSIONS = {
  scheduleRead: 'schedule.read',
  scheduleManage: 'schedule.manage',
  volunteerRead: 'volunteer.read',
  volunteerManage: 'volunteer.manage',
} as const;

export type Permission =
  (typeof PERMISSIONS)[keyof typeof PERMISSIONS];

export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly roles: readonly string[];
  readonly permissions: readonly Permission[];
}

export function hasPermission(
  user: CurrentUser | null,
  permission: Permission,
): boolean {
  return user?.permissions.includes(permission) ?? false;
}

export function isPermission(value: unknown): value is Permission {
  return typeof value === 'string' &&
    Object.values(PERMISSIONS).includes(value as Permission);
}
```

The `as const` object gives components reusable runtime values and gives TypeScript the corresponding string-literal union. Calling code cannot accidentally ask for an arbitrary permission name.

## Reject an Invalid Session Contract

The API response is untrusted input at the Angular boundary. Extend `parseCurrentUser` to require permissions and validate every item:

```typescript
import { CurrentUser, isPermission } from './current-user';

export function parseCurrentUser(value: unknown): CurrentUser {
  if (
    !isRecord(value) ||
    !isNonEmptyString(value['id']) ||
    !isNonEmptyString(value['email']) ||
    !isNonEmptyString(value['displayName']) ||
    !Array.isArray(value['roles']) ||
    !value['roles'].every(
      (role) => typeof role === 'string' && role.length > 0,
    ) ||
    !Array.isArray(value['permissions']) ||
    !value['permissions'].every(isPermission)
  ) {
    throw new Error(
      'The session response did not match the expected contract.',
    );
  }

  return {
    id: value['id'],
    email: value['email'],
    displayName: value['displayName'],
    roles: value['roles'],
    permissions: value['permissions'],
  };
}
```

Do not silently discard an unknown value. A new or misspelled permission can otherwise leave the UI in an ambiguous state. Failing the parser also exposes deployments where the Angular client was updated before the API.

Protect that decision with focused contract tests:

```typescript
it('rejects an unknown permission rather than granting it accidentally', () => {
  expect(() =>
    parseCurrentUser({
      ...response,
      permissions: [...response.permissions, 'billing.read'],
    }),
  ).toThrowError('expected contract');
});

it('requires the server to return permissions explicitly', () => {
  const { permissions: _, ...withoutPermissions } = response;

  expect(() => parseCurrentUser(withoutPermissions))
    .toThrowError('expected contract');
});
```

## Add a Permission Route Guard

Create `src/app/core/auth/permission.guard.ts`:

```typescript
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { hasPermission, isPermission } from './current-user';
import { SessionStore } from './session-store';

export const permissionGuard: CanActivateFn = (route, state) => {
  const sessions = inject(SessionStore);
  const router = inject(Router);
  const requiredPermission = route.data['permission'];

  return sessions.ensureRestored().pipe(
    map((session) => {
      if (session.status !== 'authenticated') {
        return router.createUrlTree(['/sign-in'], {
          queryParams: { returnUrl: state.url },
        });
      }

      return isPermission(requiredPermission) &&
        hasPermission(session.user, requiredPermission)
        ? true
        : router.createUrlTree(['/unavailable']);
    }),
  );
};
```

The guard first restores the cookie-backed session. Anonymous users go to sign-in with a safe return URL. Authenticated users who lack the capability go to ChurchServe's existing unavailable page. Missing or invalid route metadata fails closed.

## Guard Service Creation and Volunteer Search

Add `schedule.manage` to the create route:

```typescript
{
  path: 'new',
  canActivate: [permissionGuard],
  canDeactivate: [unsavedChangesGuard],
  data: { permission: PERMISSIONS.scheduleManage },
  loadComponent: () =>
    import('./service-event-create/service-event-create').then(
      (module) => module.ServiceEventCreate,
    ),
}
```

The volunteer route still requires authentication and now also requires `volunteer.read`:

```typescript
{
  path: '',
  canActivate: [signedInGuard, permissionGuard],
  data: { permission: PERMISSIONS.volunteerRead },
  providers: [provideVolunteerApi()],
  loadComponent: () =>
    import('./volunteers').then((module) => module.Volunteers),
}
```

Keeping the permission in route data makes the guard reusable and leaves the capability requirement visible beside the route it protects.

## Derive Action Visibility from the Session

The schedule component derives its presentation state rather than copying permission values into mutable fields:

```typescript
private readonly sessions = inject(SessionStore);

protected readonly canManageSchedule = computed(() => {
  const session = this.sessions.session();
  return session.status === 'authenticated' &&
    hasPermission(
      session.user,
      PERMISSIONS.scheduleManage,
    );
});
```

Use the result to omit service creation:

```html
@if (canManageSchedule()) {
  <div class="schedule-actions">
    <a routerLink="/schedule/new">Create service</a>
  </div>
}
```

The application shell uses the same pattern with `volunteer.read` before rendering the Volunteers link.

## Keep the Assignment Board Readable

A reader may inspect an assignment board but cannot change it. Existing assignments should remain visible, while open positions should not render a volunteer picker or Assign button.

```html
@if (assignmentStore.assignmentFor(position); as assignment) {
  <div class="assigned-volunteer">
    <strong>{{ assignment.volunteerDisplayName }}</strong>
    @if (assignment.syncStatus === 'pending') {
      <span>Saving…</span>
    } @else {
      <span>Confirmed</span>
    }
  </div>
} @else if (canManageSchedule()) {
  <div class="assignment-action">
    <label [for]="'volunteer-' + position.id">Volunteer</label>
    <select
      [id]="'volunteer-' + position.id"
      [value]="selectedVolunteer(position.id)"
      (change)="selectVolunteer(position.id, $event)"
    >
      <option value="">Choose a volunteer</option>
      @for (
        volunteer of assignmentStore.volunteersFor(position);
        track volunteer.id
      ) {
        <option [value]="volunteer.id">
          {{ volunteer.displayName }}
        </option>
      }
    </select>
    <button
      type="button"
      data-action="assign"
      [disabled]="!selectedVolunteer(position.id)"
      (click)="assign(position.id)"
    >
      Assign
    </button>
  </div>
} @else {
  <p class="assigned-volunteer">Unfilled</p>
}
```

This avoids presenting a guaranteed failure while preserving useful schedule information. The API still makes the final authorization decision.

## Bind Caches to Effective Permissions

The schedule and assignment stores cache server responses. A user's capabilities can change while the user ID remains the same, so the effective permission set belongs in cache identity:

```typescript
function sessionCacheIdentity(
  session: ReturnType<SessionStore['session']>,
): string {
  if (session.status !== 'authenticated') {
    return session.status;
  }

  return `${session.user.id}|${[...session.user.permissions]
    .sort()
    .join(',')}`;
}
```

Sorting makes equivalent sets stable even if response order changes. Adding or removing a permission produces a different identity, and each store's existing effect clears data loaded under the previous authorization context.

Test this with the same user and role but a reduced permission list. The store should return to `idle`, remove cached state, and issue a new repository request on the next load.

## Test Presentation and Navigation Separately

Angular tests now verify that:

- a reader cannot activate `/schedule/new`;
- an anonymous user still receives the sign-in redirect and return URL;
- the Create service link is absent without `schedule.manage`;
- the Volunteers link is absent without `volunteer.read`;
- the volunteer picker and Assign button are absent for a reader;
- permission changes clear both schedule and assignment caches.

For example:

```typescript
expect(
  fixture.nativeElement.querySelector('[data-action="assign"]'),
).toBeNull();
```

That assertion proves only what Angular rendered. Part 22-1's API `403` tests remain essential because a hidden button is not an authorization boundary.

## Verify Part 22-2

```bash
npm run test:ci
npm run test:coverage
npm run check:architecture
npm run build
```

The Angular UI now mirrors the server's capabilities, guards deep links, and prevents cached data from crossing a permission change. Part 22-3 completes the security chapter with privacy boundaries, safe rendering, negative-path tests, and production hardening decisions.
