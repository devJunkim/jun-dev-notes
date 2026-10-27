---
title: "ChurchServe Angular Tutorial Part 16: Integrated Angular Testing"
excerpt: "Build a balanced ChurchServe test suite with typed builders, behavioral fakes, routed workflow tests, coverage, and continuous integration."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular component service testing tutorial"
  description: "Test Angular components, signal stores, HTTP repositories, and routed workflows with Vitest, TestBed, RouterTestingHarness, and CI."
  socialTitle: "ChurchServe Part 16: Integrated Angular Tests"
  socialDescription: "Complete the intermediate milestone with reusable test data, realistic fakes, routed integration tests, coverage, and GitHub Actions."
---

# ChurchServe Angular Tutorial Part 16: Integrated Angular Testing

[Part 15](https://dev.jun-kim.net/2026/10/17/churchserve-angular-tutorial-part-15-shared-state-with-signals/) put schedule request state and caching behind a route-scoped signal store. Its focused tests prove individual store policies, but a production failure can still hide between independently correct pieces: the router may create the wrong injector, a resolver may bypass the cache, or a component may fail to render the store's value.

Part 16 completes the intermediate milestone by connecting those boundaries in the test suite. ChurchServe gains typed test-data builders, a behavioral repository fake, an integrated routed workflow test, explicit CI and coverage commands, and a GitHub Actions workflow.

> **Quick answer:** Use the smallest test that can observe the risk. Test deterministic rules directly, stores through their public signals, HTTP repositories with `HttpTestingController`, components through their DOM, and routed workflows with the real route table plus `RouterTestingHarness`. Reuse typed builders and behavioral fakes where they improve clarity, but do not replace every dependency with a mock. Run the same deterministic suite in continuous integration.

## What Changes in Part 16

Part 16 intentionally makes no visible UI or API change. It improves the system that verifies existing behavior:

1. `buildServiceEvent()` creates valid, explicit event data with small per-test overrides.
2. `FakeServiceEventRepository` implements the real repository contract and records observable interactions.
3. The route suite navigates from the schedule list to event details and proves that the route-scoped store cache crosses that boundary.
4. Test-only utilities are included by `tsconfig.spec.json` and excluded from production compilation.
5. `test:ci` forces deterministic, non-watch execution.
6. `test:coverage` generates V8 coverage while excluding test-support code from application metrics.
7. GitHub Actions runs Angular tests, the production build, and .NET API integration tests on pull requests and `main`.

The existing focused tests remain. Integration tests supplement them rather than replacing them with slower, broader setup.

## Updated File Structure

Part 16 adds test support outside `src/app` and one CI workflow:

```text
.github/
└── workflows/
    └── ci.yml
src/
├── app/
│   └── app.routes.spec.ts
└── testing/
    ├── fake-service-event-repository.ts
    └── service-event.builder.ts
package.json
package-lock.json
tsconfig.app.json
tsconfig.spec.json
```

Keeping support code in `src/testing` makes its purpose visible and avoids burying a shared fake under one feature spec. The TypeScript configuration prevents those files from becoming production inputs.

## Choose the Smallest Test That Can See the Failure

ChurchServe now exercises several test levels:

| Behavior | Best starting level | ChurchServe example |
| --- | --- | --- |
| Query normalization or time-range validation | Pure or focused class test | `schedule-query.spec.ts`, `service-event-form.spec.ts` |
| Signal transitions and derived state | Store test | `schedule-store.spec.ts` |
| HTTP method, URL, body, and DTO parsing | HTTP repository test | `http-service-event-repository.spec.ts` |
| Error messages and accessible controls | Rendered component test | `schedule.spec.ts`, `sign-in.spec.ts` |
| Guards, resolvers, route scope, and redirects | Router integration test | `app.routes.spec.ts` |
| Real browser, API process, and database journey | End-to-end test | Added later in Part 24 |

A broader test is not automatically better. A form validator does not need the router. An HTTP repository test does not need a real server. Conversely, a class-only assertion cannot prove that an error is rendered with `role="alert"`, and a store-only test cannot prove the route provider creates the expected shared instance.

Angular's testing guidance uses Vitest for the test syntax, `TestBed` for Angular dependency injection and rendering, and `RouterTestingHarness` for routed components. The [Angular testing overview](https://angular.dev/guide/testing) and [routing test guide](https://angular.dev/guide/routing/testing) are useful references for these boundaries.

## Create Valid Events with a Builder

Create `src/testing/service-event.builder.ts`:

```typescript
import { ServiceEvent } from '../app/features/schedule/service-event';

export function buildServiceEvent(overrides: Partial<ServiceEvent> = {}): ServiceEvent {
  return {
    id: 'sunday-2026-11-01',
    title: 'Sunday Morning Service',
    startsAt: '2026-11-01T10:00:00-05:00',
    endsAt: '2026-11-01T11:30:00-05:00',
    location: 'Main Sanctuary',
    requiredPositions: 10,
    assignedPositions: 7,
    ...overrides,
  };
}
```

The defaults form one valid, recognizable event. A test that cares about staffing can write:

```typescript
const fullyStaffed = buildServiceEvent({
  requiredPositions: 4,
  assignedPositions: 4,
});
```

That reads more clearly than repeating every property, and TypeScript catches a future required model field in one shared location. The overrides remain visible at the call site, unlike a large mystery fixture loaded from JSON.

Builders should not silently randomize values. Stable IDs and timestamps make failures reproducible, snapshots readable, and timezone assumptions explicit.

## Use a Behavioral Fake at the Application Boundary

Create `src/testing/fake-service-event-repository.ts`:

```typescript
import { Observable, of, throwError } from 'rxjs';
import { CreateServiceEventCommand } from '../app/features/schedule/create-service-event-command';
import { Page } from '../app/features/schedule/page';
import { ScheduleQuery } from '../app/features/schedule/schedule-query';
import { ServiceEventRepository } from '../app/features/schedule/service-event-repository';
import { ServiceEvent } from '../app/features/schedule/service-event';
import { buildServiceEvent } from './service-event.builder';

export class FakeServiceEventRepository extends ServiceEventRepository {
  readonly listQueries: ScheduleQuery[] = [];
  readonly findByIdCalls: string[] = [];
  readonly createCalls: CreateServiceEventCommand[] = [];

  private events: readonly ServiceEvent[];
  private findByIdFailure: unknown | undefined;

  constructor(events: readonly ServiceEvent[] = [buildServiceEvent()]) {
    super();
    this.events = events;
  }

  list(query: ScheduleQuery): Observable<Page<ServiceEvent>> {
    this.listQueries.push(query);
    const start = (query.page - 1) * query.pageSize;

    return of({
      items: this.events.slice(start, start + query.pageSize),
      page: query.page,
      pageSize: query.pageSize,
      totalItems: this.events.length,
    });
  }

  findById(id: string): Observable<ServiceEvent | undefined> {
    this.findByIdCalls.push(id);
    if (this.findByIdFailure !== undefined) {
      return throwError(() => this.findByIdFailure);
    }

    return of(this.events.find((event) => event.id === id));
  }

  create(command: CreateServiceEventCommand): Observable<ServiceEvent> {
    this.createCalls.push(command);
    const event = buildServiceEvent({
      ...command,
      id: `created-${this.events.length + 1}`,
      requiredPositions: 0,
      assignedPositions: 0,
    });
    this.events = [...this.events, event];
    return of(event);
  }

  failFindByIdWith(error: unknown): void {
    this.findByIdFailure = error;
  }
}
```

This fake is more realistic than three unrelated mock functions. It implements `ServiceEventRepository`, pages its in-memory collection, returns records by ID, stores a created event, and can simulate a detail failure. If the abstract repository contract changes, the fake must compile against the same change.

The call arrays expose only interactions that matter to tests. For example, `findByIdCalls` can prove that the resolver did not issue a redundant detail request after the list store cached the event. Tests do not inspect private store fields or Angular internals.

This fake is not a substitute for HTTP tests. It deliberately does not reproduce URL encoding, JSON parsing, authentication cookies, or server errors. Those belong at the HTTP repository and API integration boundaries.

## Keep Test Utilities Out of Production Compilation

Update `tsconfig.app.json`:

```json
{
  "extends": "./tsconfig.json",
  "compilerOptions": {
    "types": []
  },
  "include": ["src/**/*.ts"],
  "exclude": ["src/**/*.spec.ts", "src/testing/**/*.ts"]
}
```

Update `tsconfig.spec.json`:

```json
{
  "extends": "./tsconfig.json",
  "compilerOptions": {
    "types": ["vitest/globals"]
  },
  "include": ["src/**/*.d.ts", "src/**/*.spec.ts", "src/testing/**/*.ts"]
}
```

The application configuration excludes both specs and test support. The test configuration includes them and supplies Vitest's global types. This is clearer than relying on tree shaking to remove test-only files after compiling them as application sources.

## Test a Routed Workflow Across Real Angular Boundaries

Update `app.routes.spec.ts` to use the builder and fake in its setup:

```typescript
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { SessionState, SessionStore } from './core/auth/session-store';
import { ServiceEventDetail } from './features/schedule/service-event-detail/service-event-detail';
import { ServiceEventRepository } from './features/schedule/service-event-repository';
import { Schedule } from './features/schedule/schedule';
import { FakeServiceEventRepository } from '../testing/fake-service-event-repository';
import { buildServiceEvent } from '../testing/service-event.builder';
import { routes } from './app.routes';

describe('application routes', () => {
  const session = signal<SessionState>({
    status: 'authenticated',
    user: {
      id: 'coordinator-1',
      email: 'coordinator@churchserve.local',
      displayName: 'Demo Coordinator',
      roles: ['scheduler'],
    },
  });
  const event = buildServiceEvent();
  let repository: FakeServiceEventRepository;

  beforeEach(() => {
    session.set({
      status: 'authenticated',
      user: {
        id: 'coordinator-1',
        email: 'coordinator@churchserve.local',
        displayName: 'Demo Coordinator',
        roles: ['scheduler'],
      },
    });
    repository = new FakeServiceEventRepository([event]);
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: ServiceEventRepository, useValue: repository },
        {
          provide: SessionStore,
          useValue: {
            session: session.asReadonly(),
            ensureRestored: () => of(session()),
          },
        },
      ],
    });
  });

  it('loads the schedule and reuses its cached event in the detail resolver', async () => {
    const harness = await RouterTestingHarness.create();

    const schedule = await harness.navigateByUrl('/schedule?month=2026-11&page=1', Schedule);

    expect(schedule).toBeTruthy();
    expect(harness.routeNativeElement?.textContent).toContain('Sunday Morning Service');
    expect(harness.routeNativeElement?.textContent).toContain('3 open positions');
    expect(repository.listQueries).toEqual([
      { month: '2026-11', ministry: null, page: 1, pageSize: 4 },
    ]);

    await harness.navigateByUrl(`/schedule/${event.id}`, ServiceEventDetail);

    expect(harness.routeNativeElement?.textContent).toContain('Sunday Morning Service');
    expect(harness.routeNativeElement?.textContent).toContain('3 open positions');
    expect(repository.findByIdCalls).toEqual([]);
  });
});
```

The test uses the real application route table. The signed-in session fake lets the real guard continue, the parent schedule route creates the real `ScheduleStore`, the list component sends the parsed query to that store, and the rendered DOM proves the event and derived open-position value reached the user.

Navigation then moves to the real detail route. The resolver asks the same route-scoped store for the event. `findByIdCalls` remains empty, proving that the parent route's injector and cache survived the child-route transition. This one test would catch several wiring regressions that isolated component, store, and resolver tests cannot see.

The rest of `app.routes.spec.ts` continues to cover direct detail navigation, the create route, missing records, anonymous redirects, resolver failures, and the wildcard route. The failure test now uses the fake's named method:

```typescript
repository.failFindByIdWith(new Error('API unavailable'));
```

Named fake behavior communicates intent better than replacing a method mid-test with an untyped function.

## Keep HTTP Contract Tests Focused

The existing `HttpServiceEventRepository` tests use `HttpTestingController`, which is the correct boundary for request construction and DTO parsing:

```typescript
it('lists and maps service events returned by the API', async () => {
  const result = firstValueFrom(repository.list(query));

  const request = http.expectOne(
    '/api/service-events?month=2026-11&page=2&pageSize=4&ministry=technical',
  );
  expect(request.request.method).toBe('GET');
  request.flush({ items: [dto], page: 2, pageSize: 4, totalItems: 5 });

  await expect(result).resolves.toEqual({
    items: [expect.objectContaining({ id: 'event-1', title: 'Sunday Service' })],
    page: 2,
    pageSize: 4,
    totalItems: 5,
  });
});
```

Each HTTP suite calls `http.verify()` after a test so an unexpected outstanding request fails the suite. The routed integration test should not duplicate these URL and parsing assertions; its fake keeps that workflow fast and deterministic.

## Assert User-Observable Component Behavior

ChurchServe's component tests render standalone components and inspect the DOM. For example, the service-event form test proves that an invalid submit reveals accessible validation text:

```typescript
it('does not emit an invalid command and reveals validation errors', () => {
  const fixture = TestBed.createComponent(ServiceEventForm);
  const emitted: CreateServiceEventCommand[] = [];
  fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));

  fixture.componentInstance.submit();
  fixture.detectChanges();

  expect(emitted).toEqual([]);
  expect(fixture.componentInstance.form.controls.title.touched).toBe(true);
  expect(fixture.nativeElement.textContent).toContain('Enter a service name.');
});
```

This assertion tolerates internal refactoring while protecting what the user observes. Direct class assertions are still appropriate for complex pure calculations, but they cannot prove that a template exposes a result, button state, or live-region message correctly.

## Add Deterministic CI and Coverage Commands

Install the V8 coverage provider at the same major version as Vitest:

```bash
npm install --save-dev @vitest/coverage-v8@5.0.5
```

This updates both `package.json` and `package-lock.json`. Add the scripts to `package.json`:

```json
{
  "scripts": {
    "test": "ng test",
    "test:ci": "ng test --watch=false --progress=false",
    "test:coverage": "ng test --watch=false --coverage --coverage-exclude=src/testing/**"
  },
  "devDependencies": {
    "@vitest/coverage-v8": "^5.0.5"
  }
}
```

`test` stays convenient for local watch mode. `test:ci` always exits after one run and suppresses build progress noise. `test:coverage` creates the ignored `coverage/` report and excludes builders and fakes so the numbers describe application code rather than test infrastructure. Angular documents the coverage provider and `--coverage` option in its [code coverage guide](https://angular.dev/guide/testing/code-coverage).

Coverage is a diagnostic. It can reveal an unvisited failure branch, but a high percentage cannot prove that assertions are meaningful, concurrency is safe, authorization is correct, or sensitive information stays private. ChurchServe does not introduce a percentage gate in this part.

## Run the Suite on Pull Requests

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
        uses: actions/checkout@v4

      - name: Set up Node.js
        uses: actions/setup-node@v4
        with:
          node-version: 26.8.2
          cache: npm

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Install Angular dependencies
        run: npm ci

      - name: Run Angular tests
        run: npm run test:ci

      - name: Build Angular application
        run: npm run build

      - name: Run API integration tests
        run: dotnet test ChurchServe.slnx --configuration Release
```

The workflow runs with read-only repository contents permission and no application secrets. Angular unit tests replace browser HTTP boundaries, while API integration tests use isolated test databases, so CI does not need the local tutorial password or a committed SQLite file.

Running on pull requests catches changes before merge. Running again on `main` verifies the merged commit and provides a stable branch result. `npm ci` uses the lock file rather than recalculating dependency versions.

## Run and Verify Part 16

Run the deterministic suite and production build locally:

```bash
npm run test:ci
npm run test:coverage
npm run build
dotnet test ChurchServe.slnx --configuration Release
```

The validated Part 16 results are:

- 85 Angular tests across 21 files pass.
- V8 coverage reports 93.47% statements, 90.49% branches, 88.19% functions, and 94.68% lines for included application sources.
- The Angular production build passes strict TypeScript and template compilation.
- All 20 ASP.NET Core integration tests pass in Release mode.

Coverage values will change as the application evolves; they document this checkpoint rather than promise a permanent threshold. Release mode for .NET validation also avoids locking conflicts with a locally running Debug API executable on Windows.

## Intermediate Milestone Complete

ChurchServe now has a balanced feedback system around the architecture built so far. Pure rules, signal stores, HTTP repositories, rendered components, routes, and the real API each have tests at an appropriate boundary. Typed builders keep inputs readable, behavioral fakes preserve contracts, and one routed workflow proves the important pieces cooperate.

Part 17 begins the advanced stage by reorganizing ChurchServe around lazy feature boundaries while keeping this behavior unchanged and protected by the suite.
