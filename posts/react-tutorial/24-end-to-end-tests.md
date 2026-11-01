---
title: "ChurchServe React Tutorial Part 24: End-to-End Tests for Critical Workflows"
excerpt: "Protect ChurchServe's highest-value React journeys with Playwright, isolated SQLite fixtures, accessible locators, and actionable CI failure evidence."
category: "React Tutorial"

seo:
  focusKeyword: "React Playwright end to end testing"
  description: "Build reliable React Playwright tests for authentication, event creation, keyboard assignment, and concurrency conflicts with isolated ASP.NET Core fixtures."
  socialTitle: "ChurchServe React Part 24: End-to-End Tests with Playwright"
  socialDescription: "Test ChurchServe through a real browser, React Router, ASP.NET Core, session cookies, antiforgery, and SQLite."
---

# ChurchServe React Tutorial Part 24: End-to-End Tests for Critical Workflows

ChurchServe’s component, store, repository, and API integration tests verify most rules quickly. They do not prove that Chromium, React Router, the Vite proxy, ASP.NET Core, authentication cookies, antiforgery, and SQLite work together as one application.

Part 24 adds a deliberately small Playwright suite for the boundaries with the highest failure cost. It does not repeat every field permutation, DTO check, or reducer transition already covered by faster tests.

> **Quick answer:** Start real React and API processes on isolated ports, map destructive fixture behavior only in ASP.NET Core’s `Testing` environment, reset deterministic SQLite data before every scenario, locate controls by accessible role or label, use one worker while tests share a database, and retain traces, screenshots, and video only for failures.

If the keyboard and focus assertions are unfamiliar, review [Part 23-2’s accessible forms and dynamic focus](https://dev.jun-kim.net/2026/11/01/churchserve-react-tutorial-part-23-2-accessible-forms-dynamic-focus-and-testing/) first.

## What Part 24 adds

```text
.github/workflows/ci.yml
.gitignore
e2e/
  churchserve.spec.ts
  test-account.ts
playwright.config.ts
server/ChurchServe.Api/
  Testing/
    TestFixtureEndpoints.cs
server/ChurchServe.Api.Tests/
  TestFixtureEndpointsTests.cs
package.json
package-lock.json
vite.config.ts
vitest.config.ts
```

There is no production feature or schema change. The reset route is absent unless the API process explicitly runs in the `Testing` environment.

## Install Playwright Test

Add Playwright Test as a development dependency and install its pinned Chromium build:

```bash
npm install --save-dev @playwright/test
npx playwright install chromium
```

Add repeatable scripts:

```json
{
  "scripts": {
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui"
  }
}
```

The headless command runs locally and in CI. UI mode is useful for authoring tests and investigating a failed locator.

Keep generated evidence out of Git:

```text
/playwright-report/
/test-results/
```

The repository’s existing `*.db`, `*.db-shm`, and `*.db-wal` rules also ignore the isolated SQLite files.

## Share production initialization without sharing production data

Extract the existing seed behavior into a private method that accepts a cancellation token:

```csharp
public static async Task InitializeAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider
        .GetRequiredService<ChurchServeDbContext>();

    await dbContext.Database.MigrateAsync();
    await SeedAsync(dbContext, CancellationToken.None);
}
```

Add an explicit reset operation for the testing fixture:

```csharp
public static async Task ResetAsync(
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken)
{
    await dbContext.Database.EnsureDeletedAsync(cancellationToken);
    dbContext.ChangeTracker.Clear();
    await dbContext.Database.MigrateAsync(cancellationToken);
    await SeedAsync(dbContext, cancellationToken);
}
```

The reset applies reviewed migrations and invokes the same initializer used at startup. It does not maintain a second set of browser-test records that can drift from the application.

The Playwright API receives an explicit database path and disables SQLite pooling. Never point this process at a development, staging, or production connection string.

## Map the destructive route only in Testing

Create `TestFixtureEndpoints.cs`:

```csharp
public static class TestFixtureEndpoints
{
    public static RouteHandlerBuilder MapTestFixtureEndpoints(
        this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/test/reset", async (
            ChurchServeDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            await DatabaseInitializer.ResetAsync(
                dbContext,
                cancellationToken);
            return Results.NoContent();
        });
}
```

Conditionally map it in `Program.cs`:

```csharp
app.MapSessionEndpoints();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestFixtureEndpoints();
}
```

ASP.NET Core exposes the active environment through the host, and Microsoft documents [`IsEnvironment`](https://learn.microsoft.com/aspnet/core/fundamentals/environments?view=aspnetcore-10.0) for custom names such as `Testing`.

Do not rely only on authentication or a secret header for a database reset route. The production request pipeline should not contain the route at all.

The endpoint intentionally needs no session in `Testing`, allowing the `beforeEach` hook to reset state before a browser signs in. That is safe only because the process uses a synthetic account, an isolated database, and a pipeline that production never maps.

## Prove the environment boundary

Allow the integration-test factory to receive an environment while preserving `Testing` as its default:

```csharp
public ChurchServeApiFactory(
    string? databasePath = null,
    string role = "scheduler",
    string environment = "Testing")
{
    _role = role;
    _environment = environment;
    // Existing isolated database path setup remains unchanged.
}
```

Part 24 adds three API tests:

- creating a temporary event and resetting restores the two seeded events;
- reset succeeds without an authenticated session in `Testing`;
- `POST /api/test/reset` returns `404 Not Found` in `Production`.

The last assertion is the critical safety test. A future refactor cannot silently expose the destructive route outside the intended host environment.

## Use an explicit synthetic account

Create `e2e/test-account.ts`:

```typescript
export const E2E_ACCOUNT = {
  id: 'e2e-coordinator',
  email: 'e2e-coordinator@churchserve.local',
  displayName: 'E2E Coordinator',
  password: 'ChurchServe-E2E-Only!24',
} as const
```

This is test data, not a real credential. Playwright passes it only to the API process it starts with `ASPNETCORE_ENVIRONMENT=Testing`. Development still uses .NET user-secrets, and the local account remains unsuitable for production.

Never replace this fixture with a real user, copied customer records, or a production password. Failure traces can contain form values, page text, URLs, network metadata, and response content.

## Start isolated React and API processes

The normal development ports are 5173 and 5100. The browser suite uses 5174 and 5101 so it cannot silently attach to a developer’s existing servers.

Playwright officially supports [starting multiple web servers](https://playwright.dev/docs/test-webserver) and waiting for their health URLs. Configure both processes:

```typescript
const applicationUrl = 'http://127.0.0.1:5174'
const apiUrl = 'http://127.0.0.1:5101'
const databasePath = resolve('.churchserve.e2e.db')

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  use: {
    ...devices['Desktop Chrome'],
    baseURL: applicationUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  webServer: [
    {
      name: 'ChurchServe API',
      command:
        'dotnet run --project server/ChurchServe.Api --no-launch-profile -- --urls http://127.0.0.1:5101',
      url: `${apiUrl}/api/session`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Testing',
        ConnectionStrings__ChurchServe:
          `Data Source=${databasePath};Pooling=False`,
        Authentication__LocalAccount__Id: E2E_ACCOUNT.id,
        Authentication__LocalAccount__Email: E2E_ACCOUNT.email,
        Authentication__LocalAccount__DisplayName:
          E2E_ACCOUNT.displayName,
        Authentication__LocalAccount__Password:
          E2E_ACCOUNT.password,
        Authentication__LocalAccount__Role: 'scheduler',
      },
    },
    {
      name: 'ChurchServe React',
      command:
        'npm run dev -- --host 127.0.0.1 --port 5174 --strictPort',
      url: applicationUrl,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ...process.env,
        CHURCHSERVE_API_URL: apiUrl,
      },
    },
  ],
})
```

`reuseExistingServer: false` prevents a stale process from making tests pass against the wrong build. One worker is deliberate because every scenario resets one SQLite database. Parallelism can be added after each worker owns a distinct database and server ports.

## Make the Vite proxy configurable

Browser traffic should still use same-origin `/api` URLs so cookie and antiforgery behavior matches the application. Keep the development default while allowing the managed test API:

```typescript
server: {
  proxy: {
    '/api': process.env.CHURCHSERVE_API_URL ??
      'http://localhost:5100',
  },
}
```

The Playwright API request fixture and browser requests now cross the same Vite proxy.

Vitest’s default file discovery also sees `*.spec.ts`. Exclude the browser directory so the unit runner does not attempt to execute Playwright’s test API:

```typescript
test: {
  environment: 'jsdom',
  exclude: [...configDefaults.exclude, 'e2e/**'],
}
```

Separating discovery prevents `npm run verify` from treating an end-to-end specification as a jsdom unit test.

## Reset state before every scenario

Start the browser specification with a fixture reset:

```typescript
test.describe('ChurchServe critical workflows', () => {
  test.beforeEach(async ({ request }) => {
    const response = await request.post('/api/test/reset')
    expect(response.status()).toBe(204)
  })

  // Critical scenarios follow.
})
```

Each scenario begins with two events, four ministry positions, six volunteers, and no assignments. Tests do not depend on file leftovers or execution order.

## Test authentication and protected navigation

Use labels, roles, and observable URLs:

```typescript
async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in')
  await page.getByLabel('Email').fill(E2E_ACCOUNT.email)
  await page.getByLabel('Password').fill(E2E_ACCOUNT.password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page).toHaveURL(/\/schedule$/)
  await expect(
    page.getByRole('heading', { name: 'Upcoming services' }),
  ).toBeVisible()
}
```

The first journey signs in, calls the real sign-out endpoint, waits for the protected schedule to redirect to its encoded return URL, and confirms that direct schedule navigation remains protected.

This crosses the session cookie, antiforgery token, React session provider, route guard, safe return URL, and API endpoints.

## Test validation and event creation

One journey protects both recovery and success:

```typescript
await page.getByRole('link', { name: 'Create service' }).click()

await page.getByRole('button', { name: 'Add to schedule' }).click()
await expect(page.getByLabel('Service name')).toBeFocused()
await expect(page.getByText('Enter a service name.')).toBeVisible()

await page.getByLabel('Service name').fill('Community Prayer Night')
await page.getByLabel('Starts').fill('2026-11-20T19:00')
await page.getByLabel('Ends').fill('2026-11-20T20:30')
await page.getByLabel('Location').fill('Prayer Room')
await page.getByRole('button', { name: 'Add to schedule' }).click()

await expect(page).toHaveURL(/\/schedule\/[a-f0-9-]{36}$/)
await expect(
  page.getByRole('heading', {
    level: 1,
    name: 'Community Prayer Night',
  }),
).toBeVisible()
```

The test does not inspect React state or mock the repository. It verifies first-invalid focus, real server persistence, navigation to the generated ID, and visible detail data.

## Test assignment with the keyboard

Part 23 moved focus to a stable row when optimistic UI removes the Assign button. Exercise the real browser keyboard behavior:

```typescript
const position = page.getByRole('listitem', {
  name: 'Welcome host',
  exact: true,
})
const volunteer = position.getByLabel('Volunteer')

await volunteer.focus()
await page.keyboard.press('ArrowDown')
const assign = position.getByRole('button', { name: 'Assign' })
await expect(assign).toBeEnabled()
await volunteer.focus()
await page.keyboard.press('Tab')
await expect(assign).toBeFocused()
await page.keyboard.press('Space')

await expect(page.getByRole('status')).toHaveText(
  'Anna Brooks was assigned.',
)
await expect(position).toContainText('Confirmed')
await expect(position).toBeFocused()
```

This uses Chromium’s native select, Tab, and button activation behavior. Calling a React handler directly would not prove keyboard reachability or focus preservation.

## Reproduce a genuine stale-write conflict

Two pages in one authenticated browser context can create a real concurrency conflict:

```typescript
const secondPage = await page.context().newPage()
await secondPage.goto('/schedule/sunday-2026-11-01')

await firstPosition.getByLabel('Volunteer')
  .selectOption('anna-brooks')
await stalePosition.getByLabel('Volunteer')
  .selectOption('annette-lee')

await firstPosition.getByRole('button', { name: 'Assign' }).click()
await expect(page.getByRole('status')).toHaveText(
  'Anna Brooks was assigned.',
)

await stalePosition.getByRole('button', { name: 'Assign' }).click()

await expect(secondPage.getByRole('alert')).toContainText(
  'The schedule changed after you loaded it.',
)
await expect(stalePosition).toContainText('Anna Brooks')
await expect(stalePosition).toContainText('Confirmed')
```

Both pages load version 1. The first write advances the server version. The stale page submits version 1, receives `409 Conflict`, replaces its optimistic state with the authoritative board, and presents actionable guidance.

No fixed delay appears in these tests. Playwright assertions wait for observable outcomes.

## Let the browser suite find browser-only defects

The first browser run uncovered a problem that mock-based repository tests could not reproduce. Each HTTP client stored native `fetch` as a class property and later invoked it as a method. Chromium rejected that changed invocation context before sending the session request.

Bind the browser function when it becomes the default dependency:

```typescript
constructor(
  baseUrl = '/api/session',
  fetcher: Fetch = globalThis.fetch.bind(globalThis),
) {
  this.fetcher = fetcher
}
```

Apply the same correction to session, service-event, and assignment clients. Injected test doubles remain unchanged.

This is why end-to-end tests should complement unit tests rather than duplicate them. The unit tests correctly verified request construction with mock functions; only a real browser exposed the host-function binding requirement.

## Retain evidence only when a test fails

Playwright supports [failure-only traces, screenshots, and video](https://playwright.dev/docs/test-use-options):

```typescript
use: {
  trace: 'retain-on-failure',
  screenshot: 'only-on-failure',
  video: 'retain-on-failure',
}
```

A trace contains action history, DOM snapshots, console output, and network details. It is usually more useful than a screenshot alone for routing, cookie, or request failures.

Failure-only retention limits storage and reduces unnecessary exposure of test data. Even synthetic fixtures should be handled deliberately because evidence can contain browser state.

## Run the browser suite in CI

Playwright’s [CI guidance](https://playwright.dev/docs/ci) recommends installing browser dependencies and using one worker for reproducibility. Run fast verification first, then install Chromium and execute the browser suite:

```yaml
- name: Install Playwright browser
  run: npx playwright install --with-deps chromium

- name: Run critical browser journeys
  run: npm run test:e2e

- name: Upload browser failure evidence
  if: failure()
  uses: actions/upload-artifact@v4
  with:
    name: playwright-results
    path: |
      playwright-report/
      test-results/
    if-no-files-found: ignore
    retention-days: 7
```

Keeping four high-value journeys on every pull request is more useful than allowing a broad, slow suite to become an ignored nightly job.

## Verify Part 24

Run the complete checkpoint:

```bash
npm run verify
npm run test:coverage
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj --configuration Release
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project server/ChurchServe.Api --no-build
npx playwright install chromium
npm run test:e2e
```

Part 24 passes 120 React tests, 23 API integration tests, four Chromium journeys, architecture rules, lint, strict TypeScript, the production Vite build, coverage, and the EF Core model-drift check. Statement coverage remains 90.83 percent.

The browser suite covers:

1. Sign in, sign out, and protected-route enforcement.
2. Accessible validation, server persistence, and created-event navigation.
3. Keyboard volunteer assignment with optimistic focus preservation.
4. A real stale-write conflict across two pages and authoritative recovery.

Field permutations, DTO validation, retry policy, cache behavior, authorization combinations, and store transitions remain in faster suites.

## Result

ChurchServe now tests its critical boundaries through a real Chromium process, React Router, Vite proxy, ASP.NET Core API, cookie session, antiforgery system, and isolated SQLite database. Deterministic fixture reset keeps scenarios independent, environment-gated routing protects production, and CI retains actionable evidence only when something fails.

Part 25 can package and deploy an application whose critical workflows are now exercised end to end.
