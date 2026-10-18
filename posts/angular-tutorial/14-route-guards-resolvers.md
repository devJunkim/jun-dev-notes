---
title: "ChurchServe Angular Tutorial Part 14: Route Guards, Resolvers, and Unsaved Changes"
excerpt: "Coordinate protected ChurchServe routes, preloaded event data, redirects, and unsaved form warnings with testable router functions."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular route guards resolvers tutorial"
  description: "Use Angular functional guards, route resolvers, redirects, and CanDeactivate protection with RouterTestingHarness tests."
  socialTitle: "ChurchServe Part 14: Guards and Resolvers"
  socialDescription: "Build predictable navigation for protected routes, missing records, and unsaved forms while keeping authorization on the server."
---

# ChurchServe Angular Tutorial Part 14: Route Guards, Resolvers, and Unsaved Changes

[Part 13](https://dev.jun-kim.net/2026/10/17/churchserve-angular-tutorial-part-13-authentication-and-session-state/) added a server-managed session and protected the coordination APIs. That made the server boundary correct, but the browser could still activate `/schedule` while signed out, send an API request, and show a misleading connection error for the resulting `401 Unauthorized` response.

Part 14 makes navigation agree with that server boundary. Angular now redirects anonymous users before protected pages render, returns them to the URL they originally requested after sign-in, resolves required event data before activating a detail page, and asks before a dirty form is discarded.

> **Quick answer:** Return a `UrlTree` or `RedirectCommand` from functional guards and resolvers instead of starting a second navigation. Resolve only data a route cannot render without. Use `CanDeactivate` for in-app dirty-form navigation and `beforeunload` for refresh or tab closing. Keep API authorization on the server because browser guards are navigation policy, not a security boundary.

## What Changes in Part 14

The visible behavior changes in four places:

1. Opening a protected URL while signed out redirects to `/sign-in` and preserves the original URL.
2. Successful sign-in returns to that protected URL instead of always opening the schedule list.
3. A service-event detail page activates only after its event has loaded. Missing and unavailable records redirect to explicit pages.
4. Leaving a dirty create-event form asks for confirmation. Refreshing or closing the tab also triggers the browser's unsaved-change warning.

These are client-side workflow improvements. The ASP.NET Core API from Part 13 still makes the final authorization decision.

## Updated File Structure

Part 14 adds routing infrastructure under `core`, keeps the event resolver with its feature, and adds a small page for resolver failures:

```text
src/app/
├── core/
│   ├── auth/
│   │   ├── session-store.spec.ts
│   │   ├── session-store.ts
│   │   └── signed-in.guard.ts
│   └── routing/
│       ├── safe-return-url.spec.ts
│       ├── safe-return-url.ts
│       ├── unsaved-changes.guard.spec.ts
│       └── unsaved-changes.guard.ts
├── features/
│   ├── navigation-unavailable/
│   │   ├── navigation-unavailable.html
│   │   ├── navigation-unavailable.scss
│   │   └── navigation-unavailable.ts
│   ├── schedule/
│   │   ├── service-event-create/
│   │   │   └── service-event-create.ts
│   │   └── service-event-detail/
│   │       ├── service-event-detail.html
│   │       ├── service-event-detail.ts
│   │       └── service-event.resolver.ts
│   └── sign-in/
│       └── sign-in.ts
├── app.html
├── app.routes.spec.ts
├── app.routes.ts
├── app.scss
└── app.ts
```

No server or database file changes in this part. The API already returns `401` for anonymous requests and keeps protecting the data even if client code is modified or bypassed.

## Share One Session Restoration Request

The application initializer normally restores the session before the first navigation. A guard should still be correct when it runs in a router test, during a future bootstrap change, or while another consumer is already restoring the session.

`ensureRestored()` returns a known state immediately. While the state is `checking`, it shares one HTTP request with every caller through `shareReplay`. Guards must not assume that another guard or initializer finishes first.

Replace `src/app/core/auth/session-store.ts` with:

```typescript
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, tap } from 'rxjs';
import { CurrentUser, SignInCommand } from './current-user';
import { parseCurrentUser } from './session-dto';

export type SessionState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }
  | { status: 'unavailable'; message: string };

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly http = inject(HttpClient);
  private readonly state = signal<SessionState>({ status: 'checking' });
  private restoreRequest: Observable<SessionState> | undefined;

  readonly session = this.state.asReadonly();

  restore(): Observable<void> {
    this.state.set({ status: 'checking' });
    return this.restoreSession().pipe(map(() => undefined));
  }

  ensureRestored(): Observable<SessionState> {
    const current = this.state();
    return current.status === 'checking' ? this.restoreSession() : of(current);
  }

  private restoreSession(): Observable<SessionState> {
    if (this.restoreRequest) {
      return this.restoreRequest;
    }

    const request = this.http.get<unknown>('/api/session').pipe(
      map(parseCurrentUser),
      map((user): SessionState => ({ status: 'authenticated', user })),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          return of<SessionState>({ status: 'anonymous' });
        }

        return of<SessionState>({
          status: 'unavailable',
          message: 'ChurchServe could not verify your session.',
        });
      }),
      tap((session) => this.state.set(session)),
      finalize(() => {
        if (this.restoreRequest === request) {
          this.restoreRequest = undefined;
        }
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    this.restoreRequest = request;
    return request;
  }

  signIn(command: SignInCommand): Observable<void> {
    return this.http.post<unknown>('/api/session/sign-in', command).pipe(
      map(parseCurrentUser),
      tap((user) => this.state.set({ status: 'authenticated', user })),
      map(() => undefined),
    );
  }

  signOut(): Observable<void> {
    return this.http.delete<void>('/api/session').pipe(
      tap(() => this.state.set({ status: 'anonymous' })),
      map(() => undefined),
    );
  }
}
```

`refCount: false` matters here. Once the restoration starts, a temporary loss of subscribers should not cancel it and allow another guard to create a duplicate request.

## Redirect Anonymous Users with a Functional Guard

Create `src/app/core/auth/signed-in.guard.ts`:

```typescript
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { SessionStore } from './session-store';

export const signedInGuard: CanActivateFn = (_route, state) => {
  const sessions = inject(SessionStore);
  const router = inject(Router);

  return sessions.ensureRestored().pipe(
    map((session) =>
      session.status === 'authenticated'
        ? true
        : router.createUrlTree(['/sign-in'], {
            queryParams: { returnUrl: state.url },
          }),
    ),
  );
};
```

Returning the `UrlTree` makes the redirect the result of the current navigation. Returning `false` and then calling `router.navigate()` would start a second navigation and make cancellation, events, and tests harder to reason about.

An unavailable session also goes to sign-in in this tutorial. A larger application may instead return a dedicated maintenance route so an infrastructure failure is visually distinct from being signed out.

## Validate the Return URL Before Using It

The guard creates `returnUrl` from Angular's own router state, but query parameters can also be typed or linked manually. Keep validation at the point where the value is consumed.

Create `src/app/core/routing/safe-return-url.ts`:

```typescript
export function safeReturnUrl(value: string | null, fallback = '/schedule'): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) {
    return fallback;
  }

  return value;
}
```

This accepts application-relative URLs and rejects absolute, protocol-relative, and backslash-based values. Angular's `navigateByUrl()` does not behave like assigning `window.location`, but validating redirect-like inputs is still a useful invariant and keeps this helper safe if navigation code changes later.

Now update `src/app/features/sign-in/sign-in.ts` so a successful sign-in uses the validated value:

```typescript
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SessionStore } from '../../core/auth/session-store';
import { safeReturnUrl } from '../../core/routing/safe-return-url';

@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignIn {
  private readonly sessionStore = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly form = new FormGroup({
    email: new FormControl('coordinator@churchserve.local', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected signIn(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set('');
    this.sessionStore.signIn(this.form.getRawValue()).subscribe({
      next: () => {
        this.form.controls.password.reset();
        void this.router.navigateByUrl(
          safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')),
        );
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(
          error instanceof HttpErrorResponse && error.status === 401
            ? 'The email or password was incorrect.'
            : 'Sign-in is unavailable. Try again.',
        );
      },
    });
  }
}
```

The default remains `/schedule`, so opening the sign-in page directly behaves exactly as it did in Part 13.

## Resolve the Event Before Activating Its Detail Page

The event title, time, location, and staffing summary are the entire purpose of the detail route. The component cannot render meaningfully without that record, so this is appropriate blocking resolver data.

Create `src/app/features/schedule/service-event-detail/service-event.resolver.ts`:

```typescript
import { inject } from '@angular/core';
import { RedirectCommand, ResolveFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { ServiceEventRepository } from '../service-event-repository';
import { ServiceEvent } from '../service-event';

export const serviceEventResolver: ResolveFn<ServiceEvent | RedirectCommand> = (route, state) => {
  const repository = inject(ServiceEventRepository);
  const router = inject(Router);
  const id = route.paramMap.get('eventId');

  if (!id) {
    return new RedirectCommand(router.parseUrl('/not-found'));
  }

  return repository.findById(id).pipe(
    map((event) => event ?? new RedirectCommand(router.parseUrl('/not-found'))),
    catchError(() =>
      of(
        new RedirectCommand(
          router.createUrlTree(['/unavailable'], {
            queryParams: { returnUrl: state.url },
          }),
        ),
      ),
    ),
  );
};
```

There are two different outcomes:

- `undefined` means the repository received a real `404`, so the resolver redirects to `not-found`.
- A network, parsing, or server error redirects to `unavailable` and preserves the failed URL for retry.

The resolver returns `RedirectCommand`; it does not subscribe or call `navigate()`. Angular cancels the pending detail navigation and performs the redirect as one coordinated router outcome.

## Simplify the Detail Component

The router now guarantees that resolved data exists before activation. The component no longer needs its own loading, missing, and error state machine.

Replace `service-event-detail.ts` with:

```typescript
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ServiceEvent } from '../service-event';

@Component({
  selector: 'app-service-event-detail',
  imports: [DatePipe, RouterLink],
  templateUrl: './service-event-detail.html',
  styleUrl: './service-event-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEventDetail {
  private readonly route = inject(ActivatedRoute);

  protected readonly event = toSignal(
    this.route.data.pipe(map((data) => data['serviceEvent'] as ServiceEvent)),
    { initialValue: this.route.snapshot.data['serviceEvent'] as ServiceEvent },
  );

  protected readonly openPositions = computed(() => {
    const event = this.event();
    return Math.max(0, event.requiredPositions - event.assignedPositions);
  });
}
```

Keep the route data observable rather than reading only `snapshot`. If the router reuses this component while the `eventId` changes, new resolved data can still update the signal.

Replace `service-event-detail.html` with:

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
  }
</main>
```

Do not move every page request into a resolver. The schedule list can render filters and a loading state while its collection loads, so blocking the whole route would provide little benefit. Resolve only route-critical data.

## Add a Recoverable Resolver-Failure Page

Create `src/app/features/navigation-unavailable/navigation-unavailable.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { safeReturnUrl } from '../../core/routing/safe-return-url';

@Component({
  selector: 'app-navigation-unavailable',
  imports: [RouterLink],
  templateUrl: './navigation-unavailable.html',
  styleUrl: './navigation-unavailable.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavigationUnavailable {
  private readonly route = inject(ActivatedRoute);

  protected readonly retryUrl = safeReturnUrl(
    this.route.snapshot.queryParamMap.get('returnUrl'),
    '/',
  );
}
```

Create `navigation-unavailable.html`:

```html
<main class="unavailable-page" aria-labelledby="unavailable-heading">
  <p class="eyebrow">Navigation</p>
  <h1 id="unavailable-heading">That page is temporarily unavailable</h1>
  <p>ChurchServe could not load the data required for this page. Try again when the API is ready.</p>
  <div class="actions">
    <a [routerLink]="retryUrl">Try again</a>
    <a routerLink="/schedule">Return to the schedule</a>
  </div>
</main>
```

Create `navigation-unavailable.scss`:

```scss
.unavailable-page {
  width: min(100% - 2rem, 46rem);
  margin: 0 auto;
  padding: clamp(3rem, 8vw, 7rem) 0;
}

.eyebrow {
  margin: 0 0 0.5rem;
  color: #087f5b;
  font-size: 0.82rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

h1 {
  margin: 0;
  color: #102a43;
}

p {
  color: #52667a;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 1.5rem;
}

a {
  padding: 0.7rem 1rem;
  border: 1px solid #087f5b;
  border-radius: 0.5rem;
  color: #087f5b;
  font-weight: 750;
  text-decoration: none;
}

a:first-child {
  background: #087f5b;
  color: #fff;
}

a:focus-visible {
  outline: 3px solid #74c0fc;
  outline-offset: 3px;
}
```

## Protect Dirty Forms with CanDeactivate

The guard should depend on a small capability instead of a concrete page type. Create `src/app/core/routing/unsaved-changes.guard.ts`:

```typescript
import { inject, Injectable } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

export interface DirtyAware {
  hasUnsavedChanges(): boolean;
}

@Injectable({ providedIn: 'root' })
export class UnsavedChangesPrompt {
  confirmDiscard(): boolean {
    return globalThis.confirm('Discard the unsaved service event?');
  }
}

export const unsavedChangesGuard: CanDeactivateFn<DirtyAware> = (component) =>
  !component.hasUnsavedChanges() || inject(UnsavedChangesPrompt).confirmDiscard();
```

Separating the prompt behind `UnsavedChangesPrompt` makes the guard deterministic in tests. A production application could later replace the native confirmation with an accessible application dialog without changing the guard contract.

Update `service-event-create.ts`:

```typescript
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { DirtyAware } from '../../../core/routing/unsaved-changes.guard';
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
export class ServiceEventCreate implements DirtyAware {
  private readonly router = inject(Router);
  private readonly repository = inject(ServiceEventRepository);
  private readonly destroyRef = inject(DestroyRef);
  private readonly eventForm = viewChild(ServiceEventForm);

  protected readonly saveError = signal('');

  hasUnsavedChanges(): boolean {
    return this.eventForm()?.form.dirty ?? false;
  }

  @HostListener('window:beforeunload', ['$event'])
  protected preventBrowserUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }

  protected createEvent(command: CreateServiceEventCommand): void {
    this.saveError.set('');
    this.repository
      .create(command)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          this.eventForm()?.form.markAsPristine();
          void this.router.navigate(['/schedule', event.id]);
        },
        error: () =>
          this.saveError.set(
            'The event could not be created. Check your connection and try again.',
          ),
      });
  }
}
```

`CanDeactivate` covers Angular navigation. It cannot intercept browser refresh, closing a tab, or typing another address, so `beforeunload` covers those cases. Browsers control the final wording and may suppress the prompt in some situations.

Mark the form pristine only after the server confirms the save. At that point the data is no longer unsaved, so navigation to the new detail page should not prompt.

## Register Guards, Resolver, and Error Route

Replace `src/app/app.routes.ts` with:

```typescript
import { Routes } from '@angular/router';
import { signedInGuard } from './core/auth/signed-in.guard';
import { unsavedChangesGuard } from './core/routing/unsaved-changes.guard';
import { serviceEventResolver } from './features/schedule/service-event-detail/service-event.resolver';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/home/home').then((module) => module.Home),
    title: 'ChurchServe | Ministry scheduling',
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/sign-in/sign-in').then((module) => module.SignIn),
    title: 'Sign in | ChurchServe',
  },
  {
    path: 'schedule',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/schedule/schedule').then((module) => module.Schedule),
    title: 'Schedule | ChurchServe',
  },
  {
    path: 'schedule/new',
    canActivate: [signedInGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./features/schedule/service-event-create/service-event-create').then(
        (module) => module.ServiceEventCreate,
      ),
    title: 'Create service | ChurchServe',
  },
  {
    path: 'schedule/:eventId',
    canActivate: [signedInGuard],
    resolve: { serviceEvent: serviceEventResolver },
    loadComponent: () =>
      import('./features/schedule/service-event-detail/service-event-detail').then(
        (module) => module.ServiceEventDetail,
      ),
    title: 'Service details | ChurchServe',
  },
  {
    path: 'volunteers',
    canActivate: [signedInGuard],
    loadComponent: () =>
      import('./features/volunteers/volunteers').then((module) => module.Volunteers),
    title: 'Volunteers | ChurchServe',
  },
  {
    path: 'unavailable',
    loadComponent: () =>
      import('./features/navigation-unavailable/navigation-unavailable').then(
        (module) => module.NavigationUnavailable,
      ),
    title: 'Page unavailable | ChurchServe',
  },
  {
    path: 'not-found',
    loadComponent: () => import('./features/not-found/not-found').then((module) => module.NotFound),
    title: 'Page not found | ChurchServe',
  },
  { path: '**', redirectTo: 'not-found' },
];
```

Guards run before resolvers. An anonymous request for a detail page therefore redirects to sign-in without making the protected event request. After sign-in, the router retries the original URL and the resolver runs with the authenticated cookie.

## Show Feedback While a Resolver Blocks Navigation

Moving loading into the router removes the detail page's spinner, but a slow resolver still needs visible feedback. Angular exposes the current navigation as a signal.

Add this computed signal to `App` in `app.ts`:

```typescript
protected readonly isNavigating = computed(() => this.router.currentNavigation() !== null);
```

Add this block after the header in `app.html`:

```html
@if (isNavigating()) {
  <p class="navigation-progress" role="status">Loading page…</p>
}
```

Add the corresponding style to `app.scss`:

```scss
.navigation-progress {
  margin: 0;
  padding: 0.45rem clamp(1rem, 4vw, 4rem);
  background: #e6fcf5;
  color: #087f5b;
  font-size: 0.9rem;
  font-weight: 750;
  text-align: center;
}
```

This feedback belongs in the shell because the destination component does not exist until its blocking resolver finishes.

## Test Router Outcomes, Not Private Implementation

`RouterTestingHarness` exercises real route matching, guards, resolvers, redirects, and lazy component activation. The important cases are:

- an authenticated user activates a detail route;
- an anonymous user is redirected with the complete return URL;
- a missing event redirects to `not-found`;
- a failed event request redirects to `unavailable`;
- a clean form leaves without prompting;
- a dirty form stays when confirmation is declined;
- a dirty form leaves when confirmation is accepted;
- concurrent session checks make one HTTP request;
- unsafe return URLs fall back to `/schedule`.

The anonymous route test looks like this:

```typescript
it('redirects anonymous users to sign-in and preserves the requested URL', async () => {
  session.set({ status: 'anonymous' });
  const harness = await RouterTestingHarness.create();
  const router = TestBed.inject(Router);

  await harness.navigateByUrl('/schedule/sunday-2026-11-01');

  expect(router.url).toBe(
    '/sign-in?returnUrl=%2Fschedule%2Fsunday-2026-11-01',
  );
  expect(harness.routeNativeElement?.textContent).toContain('Sign in to ChurchServe');
});
```

The dirty-form guard test replaces the real prompt with a fake:

```typescript
it('stays when discarding a dirty form is declined', () => {
  const component = { hasUnsavedChanges: () => true };
  prompt.confirmDiscard.mockReturnValue(false);

  const result = TestBed.runInInjectionContext(
    () => unsavedChangesGuard(component, route, state, state) as boolean,
  );

  expect(result).toBe(false);
  expect(prompt.confirmDiscard).toHaveBeenCalledOnce();
});
```

These tests do not claim that a guard secures server data. The API integration tests from Part 13 still verify that anonymous requests receive `401 Unauthorized`.

## Test the Behavior in the Browser

Start the API and Angular application as usual:

```bash
dotnet run --project server/ChurchServe.Api
npm start
```

Then verify these flows at `http://localhost:4201`:

1. Sign out and enter `/schedule` directly. ChurchServe should open `/sign-in?returnUrl=%2Fschedule` instead of showing a schedule connection error.
2. Sign in. The application should return to `/schedule`.
3. Open a real service event. The shell can show `Loading page…` while the resolver runs, and the detail page should appear only when the record is ready.
4. Change the event ID in the address bar to a missing value. The router should open the not-found page.
5. Stop the API after signing in and try a detail URL. The router should open the unavailable page with a retry link.
6. Restart the API, open `/schedule/new`, type into the form, and click another navigation link. Declining the confirmation should keep the form on screen.
7. With the form still dirty, refresh the browser. The browser should show its own unsaved-change warning.
8. Save a valid event. Navigation to its detail page should happen without a discard prompt because the form becomes pristine after persistence succeeds.

Run the automated checks:

```bash
npm test -- --watch=false
npm run build
dotnet test ChurchServe.slnx
```

If the development API is running and locks its Debug executable on Windows, keep it running and use the separate Release output instead:

```bash
dotnet test ChurchServe.slnx --configuration Release
```

For this implementation, 77 Angular tests across 20 files passed, the production Angular build passed, and all 20 API integration tests passed in Release configuration.

## Production Considerations

Route coordination introduces several trade-offs worth making explicit:

- A resolver delays activation. Use it for data required to render the route, not every secondary panel.
- A request can still fail after a guard succeeds. Cookies can expire and network conditions can change, so HTTP repositories must continue handling `401`, `403`, and transport failures.
- `beforeunload` behavior is browser-controlled. Do not depend on custom prompt wording or assume every browser displays it identically.
- Native `confirm()` is intentionally small for this tutorial. A design-system dialog needs keyboard focus management, accessible naming, and an asynchronous guard result.
- Redirect inputs should remain internal. If a future implementation uses `window.location` or accepts absolute URLs, strengthen the allowlist rather than relaxing this helper.
- Client guards are observable and modifiable by the person running the browser. Server authorization remains mandatory.

## Navigation Is Now a Coordinated Workflow

Part 14 removes the gap between protected APIs and unprotected client navigation. Session restoration is shared, anonymous users retain their intended destination, route-critical event data arrives before activation, failure routes are explicit, and dirty work is harder to lose accidentally.

Part 15 will build on these predictable transitions by bringing related schedule state into a focused feature store without turning one global service into a dumping ground.
