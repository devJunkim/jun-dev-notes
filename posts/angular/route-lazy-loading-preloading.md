---
title: "Angular Route Lazy Loading: Code Splitting, Preloading, and Failure Boundaries"
excerpt: "Use Angular route-level lazy loading and deliberate preloading to reduce initial JavaScript without turning first navigation into an unobserved failure."
category: "Angular"

seo:
  focusKeyword: "Angular route lazy loading"
  description: "Design Angular route lazy loading with loadComponent, loadChildren, preloading strategies, chunk failures, measurement, and authorization boundaries."
  socialTitle: "Angular Route Lazy Loading Without Hidden Trade-Offs"
  socialDescription: "Split route code deliberately, preload high-value sections, and handle navigation, caching, and chunk failures in production."
---

# Angular Route Lazy Loading: Code Splitting, Preloading, and Failure Boundaries

An Angular application moves every page behind a dynamic import. The entry bundle shrinks, but the first click on a common route now waits for code, data, and guards in sequence. Lazy loading moved work; it did not decide when that work should happen.

> **Quick answer:** Keep the landing and critical startup routes eager, lazy-load feature boundaries with `loadComponent` or `loadChildren`, and choose preloading from measured navigation value rather than enabling it everywhere. Treat chunk delivery as a network dependency, preserve real authorization on the server, and test both successful and failed first navigation.

## Split at User-Meaningful Route Boundaries

A standalone route can load one component dynamically:

```typescript
import { Routes } from '@angular/router';
import { HomePage } from './home/home-page';

export const routes: Routes = [
  { path: '', component: HomePage },
  {
    path: 'reports',
    loadComponent: () =>
      import('./reports/reports-page').then((module) => module.ReportsPage),
  },
];
```

`HomePage` is referenced statically and belongs to the eager graph. `ReportsPage` becomes a separate chunk that Angular requests when navigation needs it.

Use `loadChildren` when a feature owns several child routes:

```typescript
export const routes: Routes = [
  {
    path: 'admin',
    loadChildren: () =>
      import('./admin/admin.routes').then((module) => module.ADMIN_ROUTES),
  },
];
```

```typescript
// admin/admin.routes.ts
import { Routes } from '@angular/router';

export const ADMIN_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./admin-home-page').then((module) => module.AdminHomePage),
  },
  {
    path: 'audit',
    loadComponent: () =>
      import('./audit-page').then((module) => module.AuditPage),
  },
];
```

Angular’s [route loading guidance](https://angular.dev/best-practices/performance/lazy-loaded-routes) recommends eager loading for primary landing pages and lazy loading sections that are not needed initially.

Do not split every small component. A useful boundary usually aligns with a feature, role-specific area, or infrequently visited workflow. Excessive chunks add request, scheduling, and cache-management overhead without meaningfully reducing startup work.

## Keep Lazy Features Self-Contained

A lazy route stops helping when an eager file imports the feature through another path. Watch for shared barrels that re-export feature components, global registries that reference every page, or startup providers that import a large feature implementation.

Prefer a narrow shared layer:

```text
src/app/
├── core/          # application-wide infrastructure
├── shared/        # small reusable UI and utilities
└── features/
    ├── reports/
    └── admin/
```

A feature can import `shared`; `shared` should not import the feature back. Inspect the production bundle rather than assuming the dynamic import created the boundary you intended.

## Decide Whether to Preload

Lazy loading optimizes initial transfer. Preloading uses later idle network time to reduce the delay on a future navigation.

Angular provides `NoPreloading` by default and `PreloadAllModules` as a built-in alternative:

```typescript
import { ApplicationConfig } from '@angular/core';
import {
  PreloadAllModules,
  provideRouter,
  withPreloading,
} from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withPreloading(PreloadAllModules)),
  ],
};
```

The [Angular preloading documentation](https://angular.dev/guide/routing/customizing-route-behavior#preloading-strategy) explains that the router starts background loading after initial navigation. Preloading does not render the page or replace its data-loading strategy; it makes lazy route configuration and code available earlier.

`PreloadAllModules` is reasonable for a modest application whose lazy sections are all likely to be visited. It can be wasteful on constrained connections or large role-specific applications.

## Preload Selected Routes with Metadata

Use route metadata to express product intent:

```typescript
export const routes: Routes = [
  {
    path: 'schedule',
    data: { preload: true },
    loadChildren: () =>
      import('./schedule/schedule.routes').then(
        (module) => module.SCHEDULE_ROUTES,
      ),
  },
  {
    path: 'audit',
    data: { preload: false },
    loadChildren: () =>
      import('./audit/audit.routes').then((module) => module.AUDIT_ROUTES),
  },
];
```

Then implement `PreloadingStrategy`:

```typescript
import { Injectable } from '@angular/core';
import { PreloadingStrategy, Route } from '@angular/router';
import { Observable, of } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SelectedRoutePreloadingStrategy
  implements PreloadingStrategy
{
  preload(route: Route, load: () => Observable<unknown>) {
    return route.data?.['preload'] === true ? load() : of(null);
  }
}
```

```typescript
provideRouter(
  routes,
  withPreloading(SelectedRoutePreloadingStrategy),
)
```

The stable [`PreloadingStrategy` API](https://angular.dev/api/router/PreloadingStrategy) receives the route and a function that starts loading. Keep the policy fast and deterministic. Do not add a blocking API call to decide whether background code may download.

## Do Not Treat Code Loading as Authorization

Not downloading an admin chunk for an anonymous user can reduce bandwidth and exposure of irrelevant interface code. It does not secure the underlying operation. Browser code and route configuration can be modified, and API endpoints can be called directly.

Route guards improve navigation behavior. Server authorization protects data and mutations. Keep those responsibilities separate even when a custom preloader uses known session state to avoid low-value downloads.

## Model the First Navigation Experience

The first visit to a lazy route may involve:

1. downloading the route chunk;
2. evaluating the module;
3. running guards and resolvers;
4. requesting feature data;
5. rendering the page.

Preloading can remove the first two steps from the critical navigation path. It cannot eliminate slow resolvers, sequential API calls, or expensive rendering.

Provide global navigation feedback only when the delay is perceptible, and avoid flashing a spinner for every fast route. Feature-level loading states should describe data loading rather than pretending code and data are the same operation.

## Handle Chunk Load Failures Deliberately

A lazy import can fail because the user is offline, a proxy returned an error, or an old HTML document references chunks removed by a new deployment.

Production hosting should keep immutable hashed assets long enough for active clients and serve HTML with revalidation. A global navigation error handler can present a bounded recovery action, but automatic reload needs safeguards:

- reload at most once for a suspected stale-chunk failure;
- preserve or safely reconstruct the intended URL;
- do not reload on every application exception;
- avoid discarding unsaved form work without warning;
- record the release and failed chunk identifier without leaking sensitive URLs.

The correct recovery may be “Reconnect and retry,” “Refresh to update,” or a normal error page depending on the failure classification.

## Measure the Result

Compare production builds before and after introducing a boundary. Track:

- entry JavaScript transferred and parsed;
- chunk count and cache behavior;
- first navigation latency for lazy routes;
- how often preloaded routes are actually visited;
- chunk-load failure rate by release;
- Core Web Vitals and interaction metrics on representative devices.

A smaller initial bundle is not automatically a faster workflow. Validate common journeys on realistic networks and CPUs.

## Test the Boundary at Several Levels

Route configuration tests can verify that a feature uses a dynamic loader and carries the intended metadata. A custom strategy test should prove that `load()` runs only for selected routes.

Browser tests should begin from a fresh context and navigate directly to a lazy deep link. Verify the server returns the application shell, the chunk loads, guards behave correctly, and the feature renders. Add a controlled chunk failure in a hosting or proxy test if the recovery path is important.

Lazy loading is a delivery policy, not merely syntax. Good boundaries reduce startup work while preserving understandable navigation, secure APIs, observable failures, and a cache strategy compatible with real deployments.
