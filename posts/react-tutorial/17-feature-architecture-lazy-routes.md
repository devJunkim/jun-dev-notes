---
title: "ChurchServe React Tutorial Part 17: Feature Architecture and Lazy Routes"
excerpt: "Split a React application into enforceable feature boundaries and load route implementations on demand with React Router and Vite."
category: "React Tutorial"

seo:
  focusKeyword: "React feature architecture lazy routes"
  description: "Organize React code into shell, core, shared, and feature boundaries, lazy-load route implementations, and enforce dependencies in CI."
  socialTitle: "React Feature Architecture with Lazy Routes"
  socialDescription: "Turn folders into enforceable React boundaries, lazy-load feature implementations, and verify the resulting production chunks."
---

# ChurchServe React Tutorial Part 17: Feature Architecture and Lazy Routes

[Part 16](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-16-integrated-testing-coverage-and-ci/) established a verification gate around the current application. That protection matters because the root router still imports every schedule page, repository, store, provider, and error boundary. The folder structure suggests features, but the startup graph treats the entire client as one eagerly loaded unit.

Part 17 turns those folders into architectural boundaries. The root route table keeps URL matching and authentication policy, while feature modules own their components, data loaders, providers, and adapters. React Router loads those implementations on demand, Vite emits separate production chunks, and an executable dependency check prevents the codebase from quietly returning to static cross-feature imports.

> **Quick answer:** Keep route paths in the root composition layer so React Router can match URLs before loading code. Put each feature's route implementations and feature-scoped providers behind a typed loader, then connect them with `route.lazy`. Place application-wide authentication in `core`, dependency-neutral contracts and fallbacks in `shared`, and business capabilities in `features`. Finally, enforce allowed static dependencies with a script that runs in the same `verify` command as lint, type checks, tests, and the build.

## What changes in Part 17

This checkpoint reorganizes the client without changing its API contract or visible workflow:

1. Authentication moves from `features/auth` to `core/auth` because session policy is application-wide infrastructure.
2. The root router no longer statically imports schedule or volunteer implementations.
3. A schedule feature module owns its repository, store, provider layout, pages, detail loader, and error boundary.
4. The volunteer placeholder becomes a real feature entry point instead of a generic shell page.
5. Shared TypeScript contracts describe what lazy feature loaders return.
6. A shared accessible fallback covers initial lazy-route and loader work.
7. A small architecture script rejects forbidden static dependencies.
8. Existing routed tests inject a controlled schedule feature loader, preserving deterministic tests.
9. The production build proves that schedule and volunteer code are emitted as separate chunks.

No ASP.NET Core endpoint, database model, migration, or authentication protocol changes in this part.

## Define boundaries by responsibility

The resulting source tree has four important areas:

```text
src/
  core/
    auth/
  features/
    schedule/
      scheduleFeature.tsx
    volunteers/
      volunteerFeature.ts
  shared/
    routing/lazyFeature.ts
    ui/RouteLoadingPage.tsx
  App.tsx
  router.tsx
```

`App.tsx`, `router.tsx`, and ordinary pages form the application shell. They decide which top-level capabilities exist and how URLs are matched.

`core` contains application-wide infrastructure and policy. Session restoration, sign-in, sign-out, route protection, and antiforgery handling are not one business feature; schedule HTTP requests and future volunteer requests both depend on them.

`shared` contains dependency-neutral contracts and UI that may be used from any layer. Shared code must not become a miscellaneous folder that imports back into features.

Each directory under `features` owns a business capability. Schedule may depend on `core` and `shared`, but it must not statically import volunteers. A feature should expose a narrow entry point rather than inviting the shell to assemble its internal store and components.

These are dependency rules, not merely naming preferences:

```text
shell  -> core, shared
core   -> shared
feature -> core, shared, itself
shared -> shared
```

The shell is allowed to discover a feature through a dynamic import. That exception is the composition point that creates a build chunk without making feature internals part of the shell's static graph.

## Understand the `route.lazy` boundary

React Router's [route object documentation](https://reactrouter.com/start/data/route-object) allows route implementations such as `Component`, `loader`, and `ErrorBoundary` to be resolved lazily. Matching properties cannot be deferred in the same way.

That distinction explains the design. The root router must retain lightweight URL shapes:

```tsx
{
  path: 'schedule/:eventId',
  lazy: async () => (await schedule()).detail,
  HydrateFallback: RouteLoadingPage,
}
```

The static `path` lets the router match `/schedule/sunday-2026-11-01`. Only after matching does it request the detail implementation. The feature can provide the page, loader, and error boundary, but it cannot introduce an unknown child path after matching has already begun.

This is route-level code splitting, not a promise that every component becomes its own network request. One schedule entry point is intentional: its list, create, and detail routes share a store and are likely to be used in the same coordinator session.

## Describe lazy features with shared contracts

Create `src/shared/routing/lazyFeature.ts`:

```typescript
import type { ComponentType } from 'react'
import type { LoaderFunction } from 'react-router'

export type LazyRouteImplementation = {
  Component: ComponentType
  loader?: LoaderFunction
  ErrorBoundary?: ComponentType
}

export type ScheduleFeature = {
  Layout: ComponentType
  list: LazyRouteImplementation
  create: LazyRouteImplementation
  detail: LazyRouteImplementation
}

export type ScheduleFeatureLoader = () => Promise<ScheduleFeature>
export type VolunteerFeatureLoader =
  () => Promise<LazyRouteImplementation>
```

The shell and features share these types without importing one another. The contract exposes only what route composition needs. It does not expose `ScheduleStore`, repository classes, or page-specific props.

The schedule type includes a layout because [Part 15](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-15-route-scoped-shared-state-with-usesyncexternalstore/) established one store shared across schedule child routes. That lifecycle remains a feature concern.

## Let the schedule feature compose itself

Create `src/features/schedule/scheduleFeature.tsx`. Its factory owns the concrete production repository and creates one store for one mounted feature boundary:

```tsx
export function createScheduleFeature(
  repository: ServiceEventRepository =
    new HttpServiceEventRepository(),
): ScheduleFeature {
  const store = new ScheduleStore(repository)

  function ScheduleFeatureLayout() {
    return (
      <ScheduleStoreProvider store={store}>
        <Outlet />
      </ScheduleStoreProvider>
    )
  }

  return {
    Layout: ScheduleFeatureLayout,
    list: { Component: SchedulePage },
    create: { Component: ServiceEventCreatePage },
    detail: {
      Component: ServiceEventDetailPage,
      loader: createServiceEventDetailLoader(store),
      ErrorBoundary: ServiceEventDetailErrorPage,
    },
  }
}
```

The root router no longer needs to know how schedule state is constructed. The feature guarantees that its layout and detail loader close over the same store. When navigation leaves the feature, the provider unmounts and retains Part 15's cleanup behavior.

Keep a test-friendly factory at the same boundary:

```typescript
export function createScheduleFeatureLoader(
  repository: ServiceEventRepository,
): ScheduleFeatureLoader {
  return async () => createScheduleFeature(repository)
}
```

Tests can supply an in-memory repository or behavioral fake without teaching the production router about those adapters. This is dependency injection at a composition seam, not a second route table.

## Load feature entry points from the root router

The production loaders use dynamic `import()`:

```typescript
const loadProductionScheduleFeature:
  ScheduleFeatureLoader = async () => {
    const feature = await import(
      './features/schedule/scheduleFeature'
    )
    return feature.createScheduleFeature()
  }

const loadProductionVolunteerFeature:
  VolunteerFeatureLoader = async () => {
    const feature = await import(
      './features/volunteers/volunteerFeature'
    )
    return feature.volunteerFeature
  }
```

Vite treats these dynamic imports as code-splitting points. Its [feature guide](https://vite.dev/guide/features) documents dynamic imports and their build behavior.

The schedule parent and its matched child may ask for the same module during one navigation. Cache the promise rather than starting duplicate work:

```typescript
function loadOnce<T>(loader: () => Promise<T>) {
  let pending: Promise<T> | undefined
  return () => (pending ??= loader())
}
```

Then compose the protected branch:

```tsx
const schedule = loadOnce(loadScheduleFeature)
const volunteers = loadOnce(loadVolunteerFeature)

{
  Component: RequireAuthenticatedSession,
  children: [
    {
      lazy: async () => ({
        Component: (await schedule()).Layout,
      }),
      HydrateFallback: RouteLoadingPage,
      children: [
        {
          path: 'schedule',
          lazy: async () => (await schedule()).list,
          HydrateFallback: RouteLoadingPage,
        },
        {
          path: 'schedule/new',
          lazy: async () => (await schedule()).create,
          HydrateFallback: RouteLoadingPage,
        },
        {
          path: 'schedule/:eventId',
          lazy: async () => (await schedule()).detail,
          HydrateFallback: RouteLoadingPage,
        },
      ],
    },
    {
      path: 'volunteers',
      lazy: volunteers,
      HydrateFallback: RouteLoadingPage,
    },
  ],
}
```

Authentication remains eager and outside the lazy feature, so the application shell can restore and display session state without depending on a feature. The current guard is a component, however, so React Router may resolve a matched lazy route before rendering the guard's redirect for an anonymous deep link. Lazy loading is a performance boundary, not a confidentiality control. Avoiding that download would require moving the decision into auth-aware routing middleware or a loader while preserving safe return URLs; the API must authorize every operation either way.

## Use one neutral loading boundary

Part 16 added a detail-specific fallback. Once the entire feature becomes lazy, initial work can occur before a detail component or its styles are available. Replace that fallback with a dependency-neutral shared component:

```tsx
export function RouteLoadingPage() {
  return (
    <section
      className="session-panel"
      aria-labelledby="route-loading-heading"
    >
      <h1 id="route-loading-heading">Loading page</h1>
      <p role="status">Preparing this ChurchServe page…</p>
    </section>
  )
}
```

The fallback is part of the eagerly loaded shell CSS and does not import feature styling. Its status text gives assistive technology meaningful progress feedback. A production application might add route-specific skeletons later, but those must not require the very lazy chunk they are meant to precede.

## Keep integration tests deterministic

Update the route factory to accept a dependency object:

```typescript
type AppRouteDependencies = {
  sessionClient: SessionClient
  loadScheduleFeature?: ScheduleFeatureLoader
  loadVolunteerFeature?: VolunteerFeatureLoader
}
```

Production uses default dynamic loaders. Routed tests inject the same feature factory with a controlled repository:

```tsx
const router = createMemoryRouter(
  createAppRoutes({
    sessionClient,
    loadScheduleFeature:
      createScheduleFeatureLoader(repository),
  }),
  { initialEntries: ['/schedule'] },
)
```

The test still exercises lazy route resolution, the real provider layout, the loader, and page navigation. It does not make a network request or rely on module mocking. The existing list-to-detail-to-create workflow therefore protects the refactor at the boundary that changed.

## Enforce dependencies instead of documenting them

Folder diagrams decay unless automation rejects invalid edges. Add `tools/check-architecture.mjs` to inspect production `.ts` and `.tsx` files, resolve relative static imports, classify both sides, and report forbidden relationships.

The essential policy is explicit:

```javascript
function forbiddenDependency(from, to) {
  if (from.kind === 'shell' && to.kind === 'feature') {
    return 'the application shell may load features only through dynamic imports'
  }
  if (
    from.kind === 'core' &&
    (to.kind === 'feature' || to.kind === 'shell')
  ) {
    return 'core can depend only on core or shared code'
  }
  if (
    from.kind === 'shared' &&
    to.kind !== 'shared'
  ) {
    return 'shared code can depend only on shared code'
  }
  if (from.kind === 'feature' && to.kind === 'shell') {
    return 'a feature cannot depend on the application shell'
  }
  if (
    from.kind === 'feature' &&
    to.kind === 'feature' &&
    from.name !== to.name
  ) {
    return `feature ${from.name} cannot depend on feature ${to.name}`
  }
}
```

The scanner deliberately checks static relative `import` and `export` statements. It ignores test support and dynamic imports. That allows `router.tsx` to be the explicit feature discovery point while preventing a static shell-to-feature edge from pulling implementations back into the entry chunk.

Add the check to the existing gate:

```json
{
  "scripts": {
    "check:architecture": "node tools/check-architecture.mjs",
    "verify": "npm run check:architecture && npm run lint && npm run typecheck && npm run test:ci && npm run build"
  }
}
```

A deliberately invalid core-to-schedule import was used to verify the guard itself. It failed with the importer, specifier, and rule, then the temporary fixture was removed and the real source passed.

This small scanner is appropriate for the current relative-import convention. If the project later adopts path aliases, package workspaces, or generated imports, evolve the resolver or use a dependency-analysis tool rather than pretending this regular-expression scanner understands every JavaScript module form.

## Verify behavior and production chunks

Run the complete frontend gate:

```bash
npm run verify
```

Run coverage separately:

```bash
npm run test:coverage
```

The validated Part 17 checkpoint reports:

- architecture boundaries pass across 42 production TypeScript files;
- lint and strict application/test type checking pass;
- all 75 tests across 13 files pass;
- the Vite production build transforms 125 modules;
- included application coverage is 92.11% statements, 85.14% branches, 93.12% functions, and 92.58% lines;
- Vite emits `scheduleFeature` as a 15.95 kB JavaScript chunk plus 6.25 kB of feature CSS before gzip;
- Vite emits `volunteerFeature` as a separate 0.45 kB JavaScript chunk before gzip.

Hashed filenames change between builds, so verify stable chunk prefixes and the dynamic import graph rather than copying a full generated filename into tests. Bundle size is also not a permanent target; it is evidence that the intended boundary exists at this checkpoint.

## Production trade-offs

Lazy features improve startup boundaries, but they introduce operational details:

- The first navigation to a feature may wait for another network request. Keep an accessible fallback and consider prefetching only when user intent makes it worthwhile.
- A deployment can leave an old browser document requesting a chunk removed by a new release. Asset retention, cache headers, and a controlled reload strategy belong to deployment design.
- A lazy boundary is not an authorization boundary. The server must still authenticate and authorize every protected API operation.
- Code splitting is not data caching. React Router loaders and the schedule store still own request and cache policy.
- One huge feature chunk can become another monolith. Split again when independent navigation and measured usage justify it, not merely to maximize chunk count.
- Dynamic imports hide feature implementation from the shell, but shared contracts can still grow into an overly broad service locator. Keep contracts narrow and capability-specific.
- The current architecture check understands relative static imports. Treat its limitations as explicit tooling scope, not proof that every possible dependency is analyzed.

## Part 17 checkpoint

ChurchServe now has enforceable advanced-stage client boundaries:

- root routes retain URL matching and authentication policy;
- schedule owns its repository, store lifecycle, pages, loader, and error boundary;
- volunteers has an independent lazy entry point;
- application-wide session code lives in `core`;
- neutral route contracts and loading UI live in `shared`;
- tests inject feature loaders without duplicating route configuration;
- an executable rule prevents forbidden static dependencies;
- CI runs that rule through the existing `verify` command;
- the production build proves that feature implementations become separate chunks.

The application behaves as it did before, but its dependency graph now states where future work belongs. Part 18 can build on these boundaries without turning the root router back into the owner of every feature detail.
