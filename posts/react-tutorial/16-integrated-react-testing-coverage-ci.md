---
title: "ChurchServe React Tutorial Part 16: Integrated Testing, Coverage, and CI"
excerpt: "Build a balanced React verification strategy with typed builders, behavioral fakes, routed workflow tests, V8 coverage, and GitHub Actions."
category: "React Tutorial"

seo:
  focusKeyword: "React integration testing Vitest coverage CI"
  description: "Test React components, stores, repositories, and routed workflows with Testing Library, Vitest coverage, typed fakes, and GitHub Actions CI."
  socialTitle: "Integrated React Testing with Vitest and CI"
  socialDescription: "Complete a React testing strategy with reusable builders, realistic fakes, routed workflows, V8 coverage, and deterministic CI."
---

# ChurchServe React Tutorial Part 16: Integrated Testing, Coverage, and CI

[Part 15](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-15-route-scoped-shared-state-with-usesyncexternalstore/) put schedule request policy and caching behind a route-scoped external store. Focused tests prove the store's transitions, but independently correct units can still fail when composed. The router might create the wrong store, a loader might bypass its cache, a form might navigate differently in the real route tree, or production compilation might accidentally include test support.

Part 16 completes the intermediate milestone by connecting those boundaries. ChurchServe gains typed test-data builders, a behavioral repository fake, a routed list-to-detail-to-create workflow, separate production and test TypeScript configurations, deterministic verification commands, V8 coverage, and a GitHub Actions workflow.

> **Quick answer:** Use the smallest test that can observe the risk, then add a small number of routed tests for composition failures. Test pure rules directly, external stores through public snapshots, repositories at their transport boundary, components through accessible DOM behavior, and workflows through the real route objects. Reuse deterministic builders and contract-compatible fakes, keep test support outside production compilation and coverage metrics, and run the same lint, type, test, build, API, and migration checks on every pull request.

## What changes in Part 16

Part 16 intentionally adds verification infrastructure rather than a new business capability:

1. `buildServiceEvent()` creates complete, stable event data with small per-test overrides.
2. `FakeServiceEventRepository` implements the real asynchronous repository contract and records meaningful interactions.
3. One integration test renders the real application routes and exercises schedule, detail, back navigation, creation, and the new detail route.
4. A detail loading fallback removes blank initial-loader output and makes the routed test warning-free.
5. `tsconfig.test.json` type-checks tests and helpers separately from production source compilation.
6. `test:ci`, `test:coverage`, and `verify` provide deterministic commands for local and automated use.
7. Vitest generates V8 text and HTML coverage while excluding tests, builders, fakes, setup, and the browser entry point.
8. GitHub Actions runs frontend verification, API integration tests, and the EF Core model-drift check for pull requests and `main`.

The existing focused tests remain. The integrated test supplements them instead of replacing them with broader, slower setup.

## Match the test level to the risk

ChurchServe now uses several test boundaries:

| Risk | Starting test level | Example |
| --- | --- | --- |
| Validation and assignment rules | Pure function or reducer test | `createServiceEvent`, `assignmentState` |
| Request coordination and cache policy | External-store test | `scheduleStore.test.ts` |
| HTTP URL, method, headers, and response parsing | Repository adapter test | `httpServiceEventRepository.test.ts` |
| Accessible rendering and interaction | Component test | `ServiceEventForm.test.tsx` |
| Route nesting, session boundary, loaders, and shared store | Router integration test | `router.integration.test.tsx` |
| Real browser, API process, and database | End-to-end test | Added in a later tutorial |

A broader test is not automatically more valuable. Date validation does not need a router. URL encoding does not need a rendered application. Conversely, a store test cannot prove that the route tree shares the same instance, and a component test cannot prove that navigation invokes the expected loader.

Testing Library's [guiding principle](https://testing-library.com/docs/) is that tests gain confidence when they resemble how the software is used. Its [query priority guidance](https://testing-library.com/docs/queries/about/) prefers roles, accessible names, and labels. ChurchServe therefore finds links, headings, forms, alerts, and regions the way users and assistive technologies encounter them instead of querying internal component structure.

## Create complete events with a typed builder

Create `src/testing/serviceEventBuilder.ts`:

```typescript
import type { ServiceEvent } from '../features/schedule/serviceEvent'

export function buildServiceEvent(
  overrides: Partial<ServiceEvent> = {},
): ServiceEvent {
  return {
    id: 'sunday-2026-11-01',
    title: 'Sunday Morning Service',
    startsAt: '2026-11-01T10:00:00-05:00',
    endsAt: '2026-11-01T11:30:00-05:00',
    location: 'Main Sanctuary',
    requiredPositions: 10,
    assignedPositions: 7,
    ...overrides,
  }
}
```

The defaults form one valid and recognizable record. A staffing test can expose only its relevant difference:

```typescript
const fullyStaffedEvent = buildServiceEvent({
  requiredPositions: 4,
  assignedPositions: 4,
})
```

That call is easier to interpret than a repeated seven-property object. If `ServiceEvent` gains a required field, TypeScript identifies one shared repair point.

The builder does not generate random IDs or dates. Deterministic values make failures reproducible, timezone assumptions visible, and output readable. A builder should reduce irrelevant detail, not hide the data a test actually cares about.

## Use a behavioral fake at the repository boundary

Create `src/testing/FakeServiceEventRepository.ts`:

```typescript
export class FakeServiceEventRepository
  implements ServiceEventRepository {
  readonly listSignals: Array<AbortSignal | undefined> = []
  readonly findByIdCalls: string[] = []
  readonly createCalls: CreateServiceEventCommand[] = []

  private events: readonly ServiceEvent[]
  private listFailure?: unknown
  private findByIdFailure?: unknown
  private createFailure?: unknown

  constructor(events: readonly ServiceEvent[] = [buildServiceEvent()]) {
    this.events = [...events]
  }
}
```

The fake implements the same interface as the HTTP and in-memory repositories. Contract changes must compile across production and test adapters.

List behavior records the cancellation signal and respects an already-aborted request:

```typescript
readonly list = async (signal?: AbortSignal) => {
  this.listSignals.push(signal)
  signal?.throwIfAborted()
  if (this.listFailure !== undefined) {
    throw this.listFailure
  }
  return this.events
}
```

Detail lookup records the requested ID:

```typescript
readonly findById = async (
  eventId: string,
  signal?: AbortSignal,
) => {
  this.findByIdCalls.push(eventId)
  signal?.throwIfAborted()
  if (this.findByIdFailure !== undefined) {
    throw this.findByIdFailure
  }
  return this.events.find((event) => event.id === eventId)
}
```

Creation behaves like a small in-memory server boundary:

```typescript
readonly create = async (
  command: CreateServiceEventCommand,
  signal?: AbortSignal,
) => {
  this.createCalls.push(command)
  signal?.throwIfAborted()
  if (this.createFailure !== undefined) {
    throw this.createFailure
  }

  const event = buildServiceEvent({
    ...command,
    id: `created-${this.events.length + 1}`,
    requiredPositions: 0,
    assignedPositions: 0,
  })
  this.events = [...this.events, event]
  return event
}
```

Named methods enable intentional failures:

```typescript
repository.failFindByIdWith(new Error('API unavailable'))
```

This communicates purpose better than replacing a method in the middle of a test. The fake still does not reproduce `fetch`, URL encoding, cookie credentials, antiforgery headers, or runtime JSON validation. Existing HTTP repository tests own those risks, while ASP.NET Core integration tests own the real endpoint and database boundary.

## Test one complete routed workflow

Create `src/router.integration.test.tsx`. Use the real route objects with controlled session and repository dependencies:

```tsx
const event = buildServiceEvent()
const repository = new FakeServiceEventRepository([event])
const sessionClient: SessionClient = {
  restore: vi.fn().mockResolvedValue(currentUser),
  signIn: vi.fn().mockResolvedValue(currentUser),
  signOut: vi.fn().mockResolvedValue(undefined),
}
const router = createMemoryRouter(
  createAppRoutes(repository, sessionClient),
  { initialEntries: ['/schedule'] },
)

render(<RouterProvider router={router} />)
```

This is not a separate test-only route table. It uses the same composition function as `createBrowserRouter`, including the session provider, authenticated branch, schedule store provider, detail loader, error boundary, and unsaved-change blocker.

First verify list loading and derived state through the DOM:

```typescript
expect(
  await screen.findByRole('heading', { name: 'Upcoming services' }),
).toBeVisible()
expect(
  await screen.findByRole('region', {
    name: 'Current schedule summary',
  }),
).toHaveTextContent('3 open positions')
expect(repository.listSignals).toHaveLength(1)
```

The test does not read the store snapshot. It proves that route composition delivered repository data through the store and component to an accessible region.

Navigate as a user would:

```typescript
await user.click(
  screen.getByRole('link', {
    name: 'View Sunday Morning Service',
  }),
)

expect(
  await screen.findByRole('heading', {
    name: 'Sunday Morning Service',
  }),
).toBeVisible()
expect(repository.findByIdCalls).toEqual([])
```

An empty call list proves the detail loader and list page shared the route-scoped cache. A store-only test cannot detect a missing provider in the actual router.

Return to the list and verify it is still loaded:

```typescript
await user.click(
  screen.getByRole('link', { name: /Back to schedule/ }),
)
expect(
  await screen.findByRole('heading', { name: 'Upcoming services' }),
).toBeVisible()
expect(repository.listSignals).toHaveLength(1)
```

The provider remained mounted across child routes, so no second list request was required.

## Carry the workflow through creation

The same test opens the real creation route and interacts through labels:

```typescript
await user.click(screen.getByRole('link', { name: 'Create service' }))
await user.type(screen.getByLabelText('Service name'), 'Prayer Night')
await user.type(screen.getByLabelText('Starts'), '2026-11-06T19:00')
await user.type(screen.getByLabelText('Ends'), '2026-11-06T20:00')
await user.type(screen.getByLabelText('Location'), 'Chapel')
await user.click(
  screen.getByRole('button', { name: 'Add to schedule' }),
)
```

Then verify the user outcome and repository command:

```typescript
expect(
  await screen.findByRole('heading', { name: 'Prayer Night' }),
).toBeVisible()
expect(screen.getByText('Chapel')).toBeVisible()
expect(repository.createCalls).toEqual([
  {
    title: 'Prayer Night',
    startsAt: new Date('2026-11-06T19:00').toISOString(),
    endsAt: new Date('2026-11-06T20:00').toISOString(),
    location: 'Chapel',
  },
])
expect(repository.findByIdCalls).toEqual([])
expect(router.state.location.pathname).toBe('/schedule/created-2')
```

The route uses the confirmed creation response cached by `ScheduleStore`, so its detail loader does not issue an immediate lookup. One test now proves list rendering, derived state, cache reuse, route persistence, controlled form normalization, repository mutation, navigation, and created-detail rendering cooperate.

That is a lot of behavior for one integration test, but it is one coherent user journey. Focused tests still isolate individual failure cases and edge rules.

## Provide an explicit loader fallback

The integrated suite initially exposed a React Router warning during direct detail loading: the route had a loader and error boundary but no initial fallback. Add one beside the successful detail page:

```tsx
export function ServiceEventDetailLoadingPage() {
  return (
    <section
      className="schedule-route-page"
      aria-labelledby="detail-heading"
    >
      <Link className="back-link" to="/schedule">
        ← Back to schedule
      </Link>
      <h1 id="detail-heading">Service event</h1>
      <p className="state-panel" role="status">
        Loading the service event…
      </p>
    </section>
  )
}
```

Register it with the data route:

```tsx
{
  path: 'schedule/:eventId',
  loader: createServiceEventDetailLoader(scheduleStore),
  Component: ServiceEventDetailPage,
  HydrateFallback: ServiceEventDetailLoadingPage,
  ErrorBoundary: ServiceEventDetailErrorPage,
}
```

The fallback prevents blank initial route output and gives assistive technology a status message while the loader resolves. The warning was not silenced; the route was completed.

## Separate production and test TypeScript inputs

The original application configuration included all of `src`, which meant production type checking also treated tests and test helpers as application inputs. Exclude them in `tsconfig.app.json`:

```json
{
  "include": ["src"],
  "exclude": [
    "src/**/*.test.ts",
    "src/**/*.test.tsx",
    "src/testing"
  ]
}
```

Create `tsconfig.test.json` for tests and support code:

```json
{
  "extends": "./tsconfig.app.json",
  "compilerOptions": {
    "tsBuildInfoFile": "./node_modules/.tmp/tsconfig.test.tsbuildinfo",
    "types": ["vite/client", "vitest/globals"]
  },
  "include": [
    "src/**/*.test.ts",
    "src/**/*.test.tsx",
    "src/testing/**/*.ts",
    "src/setupTests.ts"
  ],
  "exclude": []
}
```

The empty exclusion replaces the inherited production exclusions for this project. Imported application modules are still type-checked as dependencies of tests.

Update the type command to verify both projects:

```json
"typecheck": "tsc -b --pretty false && tsc -p tsconfig.test.json --pretty false"
```

The production build still runs the root TypeScript project, which references application and tool configuration only. Test code receives a separate strict check without becoming a production input.

## Add deterministic verification commands

Install the coverage provider at the same version as Vitest:

```bash
npm install --save-dev @vitest/coverage-v8@5.0.5
```

Add scripts to `package.json`:

```json
{
  "scripts": {
    "test": "vitest run",
    "test:ci": "vitest run --reporter=default",
    "test:coverage": "vitest run --coverage",
    "typecheck": "tsc -b --pretty false && tsc -p tsconfig.test.json --pretty false",
    "verify": "npm run lint && npm run typecheck && npm run test:ci && npm run build"
  }
}
```

`vitest run` is explicitly non-watch and exits with a failure status when a test fails. `verify` is the frontend command both developers and CI can run.

Do not hide lint, type, test, or build work behind one opaque custom script implementation. npm composition keeps each command independently useful and makes CI output identify the failing boundary.

## Configure V8 coverage intentionally

Vitest supports V8 coverage through the optional `@vitest/coverage-v8` package. Configure it in `vitest.config.ts`:

```typescript
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      exclude: [
        'src/**/*.test.{ts,tsx}',
        'src/testing/**',
        'src/setupTests.ts',
        'src/main.tsx',
      ],
    },
  },
})
```

The terminal report supports quick review, while HTML provides line-by-line exploration under `coverage/`. Add that generated directory to `.gitignore`.

Tests, builders, fakes, and setup code are excluded because the metric should describe application execution, not how thoroughly test infrastructure tests itself. `main.tsx` is a thin browser bootstrap that requires a real DOM root and adds little useful coverage signal.

Vitest's [coverage guide](https://vitest.dev/guide/coverage.html) documents both V8 and Istanbul providers. Coverage is diagnostic rather than proof: a high percentage cannot establish useful assertions, safe concurrency, correct authorization, accessible behavior, or absence of sensitive-data leaks.

Part 16 records the result but does not introduce a percentage gate. A future threshold should be chosen by risk and enforced deliberately, not set just below today's number.

## Run verification on pull requests and main

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

permissions:
  contents: read

jobs:
  verify:
    runs-on: ubuntu-latest

    steps:
      - name: Check out repository
        uses: actions/checkout@v7

      - name: Set up Node.js
        uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm

      - name: Set up .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Install dependencies
        run: npm ci

      - name: Run frontend verification
        run: npm run verify

      - name: Restore .NET tools
        run: dotnet tool restore

      - name: Run API integration tests
        run: dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj --configuration Release

      - name: Check EF Core model drift
        run: dotnet ef migrations has-pending-model-changes --project server/ChurchServe.Api
```

The workflow grants the token only read access to repository contents. GitHub's [workflow permissions documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax) notes that specifying one permission sets unspecified permissions to none.

`npm ci` installs the exact lockfile rather than recalculating versions. The official current action documentation uses `actions/checkout@v7`, `actions/setup-node@v7`, and `actions/setup-dotnet@v6`. Node 24 is a stable CI target for this Vite application, while .NET setup installs the supported 10.0 SDK channel.

No application secrets are required. Frontend tests use injected clients and repositories. API integration tests configure their own testing host and isolated database. The model-drift check uses the repository's local `dotnet-ef` tool manifest.

Pull-request runs catch changes before merge. A `main` run verifies the actual merged commit rather than assuming the branch result transfers perfectly through merge.

## Run and verify Part 16

Run the same frontend gate as CI:

```bash
npm run verify
```

Run coverage separately when reviewing test depth:

```bash
npm run test:coverage
```

Run server verification:

```bash
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj \
  --configuration Release
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The validated Part 16 checkpoint reports:

- 75 React tests across 13 files pass;
- lint completes without warnings;
- application and test TypeScript projects pass strict checking;
- the Vite production build succeeds;
- V8 coverage reports 92.21% statements, 85% branches, 93.24% functions, and 92.73% lines for included application files;
- the ASP.NET Core API builds with zero warnings and errors;
- all nine API integration tests pass;
- EF Core reports no pending model changes.

Coverage values will change as ChurchServe evolves. They describe this checkpoint and provide a baseline for investigation rather than a permanent promise.

## Production testing boundaries

The suite is stronger, but it does not prove everything:

- jsdom is not a complete browser. Layout, focus edge cases, native dialog behavior, cookie policy, and browser-specific APIs still need browser testing.
- A fake repository does not verify HTTP headers, serialization, authentication cookies, antiforgery tokens, or server failure bodies. Keep adapter and API tests.
- Integration tests should cover valuable seams, not repeat every component edge case through the entire route tree.
- Avoid snapshots of large DOM trees when roles, names, and visible outcomes express intent more clearly.
- Keep fake behavior simpler than production. A fake that reimplements the server can be wrong in the same way as the client assumption.
- Coverage exclusions and thresholds are policy. Review changes to them as carefully as changes to production configuration.
- CI action tags are convenient moving major versions, not immutable commit pins. Higher-security environments may pin full action commit SHAs and manage upgrades explicitly.
- Pull-request workflows execute contributed package scripts. Keep permissions minimal and do not expose deployment secrets to untrusted changes.
- End-to-end tests remain necessary for the deployed origin, reverse proxy, real cookies, browser, API process, and database together.

## Intermediate milestone complete

ChurchServe now has a balanced feedback system around the architecture built so far:

- deterministic builders keep valid test data readable;
- behavioral fakes implement real application contracts;
- pure rules, stores, HTTP adapters, components, routes, and the API are tested at appropriate boundaries;
- one routed workflow proves list, cache, detail, form, creation, and navigation cooperate;
- loader startup has an explicit accessible fallback;
- test support is type-checked without entering production compilation;
- coverage exposes unvisited application branches without counting test infrastructure;
- one local command matches frontend CI verification;
- GitHub Actions verifies frontend, API, and migration state on pull requests and `main`;
- lockfiles and minimal permissions keep automation predictable.

Part 17 begins the advanced stage by reorganizing ChurchServe around lazy feature boundaries while this suite protects the existing behavior.
