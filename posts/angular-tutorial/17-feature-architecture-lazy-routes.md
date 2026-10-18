---
title: "ChurchServe Angular Tutorial Part 17: Feature Architecture and Lazy Routes"
excerpt: "Restructure ChurchServe around lazy feature route files, scoped data providers, shared domain contracts, and executable dependency rules."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular feature architecture lazy routes"
  description: "Organize Angular features with lazy route files, route providers, shared contracts, dependency rules, and architecture tests."
  socialTitle: "ChurchServe Part 17: Scalable Angular Architecture"
  socialDescription: "Create enforceable lazy feature boundaries without changing ChurchServe URLs or user behavior."
---

# ChurchServe Angular Tutorial Part 17: Feature Architecture and Lazy Routes

[Part 16](https://dev.jun-kim.net/2026/10/17/churchserve-angular-tutorial-part-16-integrated-angular-testing/) completed the intermediate milestone with focused tests, a routed integration test, coverage, and continuous integration. ChurchServe already groups code by feature and lazy-loads individual pages, but the root route and application configuration still know schedule guards, resolvers, stores, and repository providers.

Part 17 turns those folders into real feature boundaries. The root router lazy-loads route arrays, each feature owns its routes and data providers, a shared domain contract replaces one cross-feature import, and an executable architecture rule prevents those dependencies from returning.

> **Quick answer:** Keep the application shell small, lazy-load feature route arrays with `loadChildren`, and register feature repositories and state in those routes. Allow features to depend on `core` and deliberately shared contracts, but reject imports into sibling features. Route providers limit visibility, not necessarily cache lifetime, so clear identity-bound state explicitly when the session ends.

## What Changes in Part 17

This part preserves every public URL and visible workflow. The structural changes are:

1. `/schedule` loads `schedule.routes.ts`; `/volunteers` loads `volunteers.routes.ts`.
2. Schedule routes own `ScheduleStore` and `ServiceEventRepository` providers.
3. Volunteer routes own `VolunteerRepository` providers.
4. The root application keeps only cross-cutting HTTP, session initialization, and router providers.
5. The `Volunteer` contract moves to `shared/domain` because both schedule assignments and volunteer search use it.
6. A dependency script rejects shared-to-feature, core-to-feature, sibling-feature, and static shell-to-feature imports.
7. Schedule state clears when the session becomes anonymous instead of assuming navigation destroys a lazy route injector.
8. Router tests substitute the repository inside the lazy feature route and prove the store is not root-provided.

Lazy loading is a delivery and ownership boundary, not authorization. The ASP.NET Core API continues to authenticate and authorize every protected request.

## Updated File Structure

```text
src/app/
├── core/
│   └── auth/
├── shared/
│   ├── domain/
│   │   └── volunteer.ts
│   └── ui/
├── features/
│   ├── schedule/
│   │   ├── api/
│   │   ├── schedule.routes.ts
│   │   └── schedule-store.ts
│   └── volunteers/
│       ├── api/
│       └── volunteers.routes.ts
├── app.config.ts
└── app.routes.ts
tools/
└── check-architecture.mjs
```

The existing feature contents remain close to their business capability. This part does not create generic top-level `components`, `services`, or `models` folders merely to make the tree look layered.

## Make the Root Route Table a Composition Boundary

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
    path: 'sign-in',
    loadComponent: () => import('./features/sign-in/sign-in').then((module) => module.SignIn),
    title: 'Sign in | ChurchServe',
  },
  {
    path: 'schedule',
    loadChildren: () =>
      import('./features/schedule/schedule.routes').then((module) => module.SCHEDULE_ROUTES),
  },
  {
    path: 'volunteers',
    loadChildren: () =>
      import('./features/volunteers/volunteers.routes').then((module) => module.VOLUNTEER_ROUTES),
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

`loadComponent` defers one component. `loadChildren` defers a route collection and everything statically reachable only from that file. Angular documents this distinction in its [lazy-loaded routes guide](https://angular.dev/best-practices/performance/lazy-loaded-routes).

The shell now knows the feature entry-point filenames and exported route constants, but not their guards, stores, resolvers, pages, or repository implementations.

## Let the Schedule Feature Own Its Routes

Create `src/app/features/schedule/schedule.routes.ts`:

```typescript
import { Routes } from '@angular/router';
import { signedInGuard } from '../../core/auth/signed-in.guard';
import { unsavedChangesGuard } from '../../core/routing/unsaved-changes.guard';
import { provideServiceEventApi } from './api/provide-service-event-api';
import { serviceEventResolver } from './service-event-detail/service-event.resolver';
import { ScheduleStore } from './schedule-store';

export const SCHEDULE_ROUTES: Routes = [
  {
    path: '',
    canActivate: [signedInGuard],
    providers: [provideServiceEventApi(), ScheduleStore],
    children: [
      {
        path: '',
        loadComponent: () => import('./schedule').then((module) => module.Schedule),
        title: 'Schedule | ChurchServe',
      },
      {
        path: 'new',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./service-event-create/service-event-create').then(
            (module) => module.ServiceEventCreate,
          ),
        title: 'Create service | ChurchServe',
      },
      {
        path: ':eventId',
        resolve: { serviceEvent: serviceEventResolver },
        loadComponent: () =>
          import('./service-event-detail/service-event-detail').then(
            (module) => module.ServiceEventDetail,
          ),
        title: 'Service details | ChurchServe',
      },
    ],
  },
];
```

The componentless parent preserves `/schedule`, `/schedule/new`, and `/schedule/:eventId`. Its environment injector makes the repository and store visible to the list, create page, resolver, and detail page without exposing them through the root injector.

Route-level providers are appropriate for feature state and adapters. The [Angular route documentation](https://angular.dev/guide/routing/define-routes#route-level-providers-for-dependency-injection) describes how children inherit those providers.

## Give Volunteers Its Own Route Entry Point

Create `src/app/features/volunteers/volunteers.routes.ts`:

```typescript
import { Routes } from '@angular/router';
import { signedInGuard } from '../../core/auth/signed-in.guard';
import { provideVolunteerApi } from './api/provide-volunteer-api';

export const VOLUNTEER_ROUTES: Routes = [
  {
    path: '',
    canActivate: [signedInGuard],
    providers: [provideVolunteerApi()],
    loadComponent: () => import('./volunteers').then((module) => module.Volunteers),
    title: 'Volunteers | ChurchServe',
  },
];
```

The feature owns its guard, adapter, page, and title. Adding volunteer detail or availability children later will remain local to this file.

## Keep Cross-Cutting HTTP at the Root

The session store needs `HttpClient` before a lazy feature activates, so HTTP transport remains an application-wide provider. Feature-specific repository bindings move out.

Update `app.config.ts`:

```typescript
import { provideHttpClient } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from './core/auth/session-store';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideAppInitializer(() => firstValueFrom(inject(SessionStore).restore())),
    provideRouter(routes),
  ],
};
```

Remove `provideHttpClient()` from `provideServiceEventApi()` so that helper binds only the feature abstraction:

```typescript
export function provideServiceEventApi(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: ServiceEventRepository, useClass: HttpServiceEventRepository },
  ]);
}
```

This separation distinguishes transport infrastructure from a business-feature adapter.

## Move a Real Cross-Feature Contract to Shared Domain

The architecture check initially found that `schedule/assignment-store.ts` imported `volunteers/volunteer.ts`. Moving the entire assignment store into volunteers would be wrong, and allowing sibling-feature imports would make ownership ambiguous. `Volunteer` is a small contract genuinely used by both capabilities, so move it to `src/app/shared/domain/volunteer.ts`:

```typescript
export interface Volunteer {
  readonly id: string;
  readonly displayName: string;
  readonly eligibleMinistries: readonly string[];
}
```

Schedule imports it with:

```typescript
import { Volunteer } from '../../shared/domain/volunteer';
```

Volunteer repository, DTO, page, and tests import the same shared contract. `shared/domain` should remain small; it is not a destination for code with uncertain ownership. A type belongs there only when multiple features intentionally share its meaning.

## Do Not Assume Navigation Destroys Lazy Route State

A route-scope test exposed an important nuance: Angular may retain a loaded route configuration and its environment injector after navigation. Route providers constrain visibility, but leaving `/schedule` is not a reliable cache-eviction signal.

Identity-bound data must therefore clear on an identity event. Update `ScheduleStore` to observe the session:

```typescript
private readonly sessionStore = inject(SessionStore, { optional: true });

constructor() {
  this.destroyRef.onDestroy(() => this.loadSubscription?.unsubscribe());
  effect(() => {
    if (this.sessionStore?.session().status === 'anonymous') {
      this.clear();
    }
  });
}

private clear(): void {
  this.loadSubscription?.unsubscribe();
  this.loadSubscription = undefined;
  this.loadStateValue.set('idle');
  this.queryValue.set(null);
  this.pageValue.set(null);
  this.loadErrorValue.set('');
  this.eventCacheValue.set(new Map());
}
```

The optional injection keeps focused component tests able to provide the store without constructing an authentication system. Production always has the root `SessionStore`. A focused store test loads an event, changes the session to anonymous, calls `TestBed.tick()`, and verifies that state and cache are empty.

## Enforce Dependency Direction

Create `tools/check-architecture.mjs`. The script scans production TypeScript static imports, classifies each source and target, and reports violations:

```javascript
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, relative, resolve, sep } from 'node:path';

const appRoot = resolve('src/app');
const sourceFiles = collectTypeScriptFiles(appRoot);
const violations = [];

for (const sourceFile of sourceFiles) {
  const sourceArea = describeArea(sourceFile);
  const source = readFileSync(sourceFile, 'utf8');
  const importPattern = /\b(?:import|export)\s+(?!\()(?:[\s\S]*?\s+from\s+)?['"](\.[^'"]+)['"]/g;

  for (const match of source.matchAll(importPattern)) {
    const targetFile = resolve(dirname(sourceFile), match[1]);
    const targetArea = describeArea(targetFile);
    const reason = invalidDependency(sourceArea, targetArea);
    if (reason) {
      violations.push(
        `${relative(appRoot, sourceFile)} -> ${relative(appRoot, targetFile)} (${reason})`,
      );
    }
  }
}

if (violations.length > 0) {
  console.error('Architecture boundary violations:');
  for (const violation of violations) console.error(`- ${violation}`);
  process.exitCode = 1;
} else {
  console.log(`Architecture boundaries passed for ${sourceFiles.length} production TypeScript files.`);
}

function collectTypeScriptFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) return collectTypeScriptFiles(path);
    return entry.isFile() && entry.name.endsWith('.ts') && !entry.name.endsWith('.spec.ts')
      ? [path]
      : [];
  });
}

function describeArea(file) {
  const parts = relative(appRoot, file).split(sep);
  if (parts[0] === 'features') return { layer: 'feature', feature: parts[1] };
  if (parts[0] === 'core' || parts[0] === 'shared') return { layer: parts[0] };
  return { layer: 'shell' };
}

function invalidDependency(source, target) {
  if (source.layer === 'shared' && (target.layer === 'core' || target.layer === 'feature'))
    return 'shared code cannot depend on core or feature code';
  if (source.layer === 'core' && target.layer === 'feature')
    return 'core code cannot depend on feature code';
  if (source.layer === 'feature' && target.layer === 'feature' && source.feature !== target.feature)
    return 'features cannot import another feature directly';
  if (source.layer === 'shell' && target.layer === 'feature')
    return 'the application shell must reach features through dynamic route imports';
  return undefined;
}
```

Add `"check:architecture": "node tools/check-architecture.mjs"` to `package.json` and run it in CI after Angular tests. The rule ignores specs and dynamic `import()` calls intentionally: specs may compose features, and dynamic route entry points are the shell's approved feature dependency.

## Test Lazy Routes without Replacing Production Providers Accidentally

Route providers correctly take precedence over a root test provider. The router suite therefore replaces the provider inside a test copy of the lazy schedule routes:

```typescript
function routesWithServiceEventRepository(repository: ServiceEventRepository): Routes {
  return routes.map((route) =>
    route.path === 'schedule'
      ? {
          ...route,
          loadChildren: async () =>
            SCHEDULE_ROUTES.map((featureRoute) => ({
              ...featureRoute,
              providers: [ScheduleStore, { provide: ServiceEventRepository, useValue: repository }],
            })),
        }
      : route,
  );
}
```

The test still uses the real root and feature route shapes, guards, store, resolver, and pages while substituting only the external repository boundary. Another assertion verifies feature state is absent from the root injector:

```typescript
it('keeps schedule state outside the root injector', () => {
  expect(() => TestBed.inject(ScheduleStore)).toThrow();
});
```

## Run and Verify Part 17

```bash
npm run check:architecture
npm run test:ci
npm run test:coverage
npm run build
dotnet test ChurchServe.slnx --configuration Release
```

The validated checkpoint has 87 passing Angular tests across 21 files, 20 passing API integration tests, and architecture checks across 40 production TypeScript files. Coverage reports 93.36% statements and 94.64% lines.

The production build emits separate `schedule-routes` and `volunteers-routes` chunks. At this checkpoint the initial bundle changes from 294.40 kB to 288.33 kB. That small measurement is evidence of the boundary, not a universal performance promise; actual transfer and navigation costs depend on future feature size and caching.

## Architecture That Constrains Future Changes

Part 17 does not rewrite working pages. It gives each feature a route entry point, keeps providers near their consumers, makes one shared contract explicit, and turns dependency direction into an executable rule. The result is useful because future violations fail locally and in CI instead of surviving as a diagram no one enforces.

Part 18 builds on these boundaries to implement optimistic assignment updates and handle genuine version conflicts.
