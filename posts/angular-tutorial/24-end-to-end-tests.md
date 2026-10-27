---
title: "ChurchServe Angular Tutorial Part 24: End-to-End Tests for Critical Workflows"
excerpt: "Protect ChurchServe's highest-value journeys with Playwright, isolated SQLite fixtures, accessible locators, and actionable CI failure evidence."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular Playwright end to end testing"
  description: "Build reliable Angular Playwright tests for authentication, event creation, keyboard assignment, and concurrency conflicts with isolated ASP.NET Core fixtures."
  socialTitle: "ChurchServe Part 24: Angular End-to-End Tests with Playwright"
  socialDescription: "Test ChurchServe through a real browser, API, session cookie, and SQLite database without creating a slow or fragile suite."
---

# ChurchServe Angular Tutorial Part 24: End-to-End Tests for Critical Workflows

The ChurchServe unit and API integration suites verify most rules quickly. They do not prove that Angular routing, the browser, the ASP.NET Core API, antiforgery protection, authentication cookies, and SQLite work together as one application.

Part 24 adds a deliberately small Playwright suite for the boundaries that matter most. It does not repeat every validation permutation already covered by faster tests.

> **Quick answer:** Start a real Angular application and API on isolated ports, map fixture reset behavior only in the `Testing` environment, reset deterministic SQLite data before each scenario, locate controls by accessible role or label, keep the suite single-worker until fixtures are partitioned, and retain traces, screenshots, and video only when a test fails.

If the focus behavior in this article is unfamiliar, review [Part 23-2's accessible forms and dynamic focus](https://dev.jun-kim.net/2026/10/24/churchserve-angular-tutorial-part-23-2-accessible-forms-dynamic-focus-and-testing/) first.

## What Part 24 Changes

```text
.github/workflows/ci.yml
.gitignore
e2e/
  churchserve.spec.ts                 # new
  proxy.conf.json                     # new
  test-account.ts                     # new
playwright.config.ts                  # new
server/ChurchServe.Api/
  Testing/
    TestFixtureEndpoints.cs           # new
  Program.cs
tests/ChurchServe.Api.Tests/
  ChurchServeApiFactory.cs
  TestFixtureEndpointsTests.cs        # new
package.json
package-lock.json
README.md
```

There is no production feature or database-schema change. The new endpoint is absent unless ASP.NET Core is explicitly running in the `Testing` environment.

## Install Playwright Test

Add Playwright Test as a development dependency and install its pinned Chromium build:

```bash
npm install --save-dev @playwright/test
npx playwright install chromium
```

Add commands to `package.json`:

```json
{
  "scripts": {
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui"
  }
}
```

`npm run test:e2e` is the repeatable headless command used by CI. UI mode is for local diagnosis and test authoring.

Ignore the generated database and failure artifacts:

```text
/playwright-report
/test-results
/.churchserve.e2e.db
/.churchserve.e2e.db-shm
/.churchserve.e2e.db-wal
```

Playwright can retain screenshots, videos, and traces, but none of those files belong in source control.

## Create a Testing-Only Reset Endpoint

Browser scenarios must not depend on the order in which they run or on data left by a previous execution. Add `server/ChurchServe.Api/Testing/TestFixtureEndpoints.cs`:

```csharp
using ChurchServe.Api.Data;

namespace ChurchServe.Api.Testing;

public static class TestFixtureEndpoints
{
  public static RouteHandlerBuilder MapTestFixtureEndpoints(
      this IEndpointRouteBuilder endpoints) =>
      endpoints.MapPost("/api/test/reset", async (
          ChurchServeDbContext dbContext,
          CancellationToken cancellationToken) =>
      {
        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        await DatabaseInitializer.InitializeAsync(dbContext);
        return Results.NoContent();
      });
}
```

The endpoint deletes only the Playwright database configured for the spawned test API, then runs the same idempotent initializer used at application startup. It does not contain a second hand-maintained copy of the seed data.

Map it conditionally in `Program.cs`:

```csharp
using ChurchServe.Api.Testing;

// Existing production endpoint mappings remain unchanged.
app.MapSessionEndpoints();
app.MapServiceEventEndpoints().RequireAuthorization();
app.MapAssignmentEndpoints().RequireAuthorization();
app.MapVolunteerEndpoints().RequireAuthorization();

if (app.Environment.IsEnvironment("Testing"))
{
  app.MapTestFixtureEndpoints();
}

app.Run();
```

Do not rely on an authorization attribute to protect a destructive fixture route in production. The safer boundary is that production never maps the route.

## Prove the Fixture Boundary

Allow the API test factory to select an environment while preserving `Testing` as its default:

```csharp
public sealed class ChurchServeApiFactory : WebApplicationFactory<Program>
{
  private SqliteConnection? connection;
  private readonly string role;
  private readonly string environment;

  public ChurchServeApiFactory(
      string role = "scheduler",
      string environment = "Testing")
  {
    this.role = role;
    this.environment = environment;
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment(environment);
    // Keep the existing authentication and in-memory SQLite configuration.
  }
}
```

Add `TestFixtureEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using ChurchServe.Api.ServiceEvents;

namespace ChurchServe.Api.Tests;

public sealed class TestFixtureEndpointsTests
{
  [Fact]
  public async Task Reset_restores_seed_data_in_the_testing_environment()
  {
    using var factory = new ChurchServeApiFactory();
    using var client = await factory.CreateAuthenticatedClientAsync();
    var created = await client.PostAsJsonAsync(
        "/api/service-events",
        new CreateServiceEventRequest(
            "Temporary event",
            DateTimeOffset.Parse("2026-11-20T10:00:00-05:00"),
            DateTimeOffset.Parse("2026-11-20T11:00:00-05:00"),
            "Test room"));
    created.EnsureSuccessStatusCode();

    var reset = await client.PostAsync("/api/test/reset", content: null);
    var schedule = await client.GetFromJsonAsync<ServiceEventPageResponse>(
        "/api/service-events?month=2026-11&page=1&pageSize=50");

    Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    Assert.NotNull(schedule);
    Assert.Equal(2, schedule.TotalItems);
    Assert.DoesNotContain(
        schedule.Items,
        item => item.Title == "Temporary event");
  }

  [Fact]
  public async Task Reset_route_is_not_mapped_outside_the_testing_environment()
  {
    using var factory = new ChurchServeApiFactory(
        environment: "Production");
    using var client = factory.CreateClient();

    var response = await client.PostAsync(
        "/api/test/reset",
        content: null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
```

The first test proves reset behavior. The second prevents a future refactor from accidentally exposing it outside `Testing`.

## Use an Explicit Synthetic Account

Create `e2e/test-account.ts`:

```typescript
export const E2E_ACCOUNT = {
  id: 'e2e-coordinator',
  email: 'e2e-coordinator@churchserve.local',
  displayName: 'E2E Coordinator',
  password: 'ChurchServe-E2E-Only!24',
} as const;
```

This is synthetic test data, not a real credential. Playwright passes it only to the API process it starts with `ASPNETCORE_ENVIRONMENT=Testing`. The normal development account still uses .NET user-secrets, and the local account authenticator remains disabled in production.

Never substitute a real user, production password, or copied customer data here.

## Isolate the Browser Servers

The normal development application uses ports 4201 and 5091. Part 24 uses 4202 and 5092 so the test suite does not silently attach to a developer's existing servers.

Create `e2e/proxy.conf.json`:

```json
{
  "/api": {
    "target": "http://127.0.0.1:5092",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "warn"
  }
}
```

Then add `playwright.config.ts`:

```typescript
import { defineConfig, devices } from '@playwright/test';
import { E2E_ACCOUNT } from './e2e/test-account';

const applicationUrl = 'http://127.0.0.1:4202';
const apiUrl = 'http://127.0.0.1:5092';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI']
    ? [['line'], ['html', { open: 'never' }]]
    : 'list',
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
        'dotnet run --project server/ChurchServe.Api --no-launch-profile -- --urls http://127.0.0.1:5092',
      url: `${apiUrl}/api/session`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Testing',
        ConnectionStrings__ChurchServe:
          'Data Source=.churchserve.e2e.db',
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
      name: 'ChurchServe Angular',
      command:
        'npm start -- --host 127.0.0.1 --port 4202 --proxy-config e2e/proxy.conf.json',
      url: applicationUrl,
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
});
```

Playwright supports multiple managed web servers and waits for their configured URLs before running tests. Setting `reuseExistingServer: false` is intentional: a stale API or Angular process could otherwise make a test pass against the wrong code.

The suite uses one worker because every scenario resets the same SQLite file. Parallel workers would race. A future version can assign one database and API port per worker, but concurrency should follow isolation rather than precede it.

## Reset State before Every Scenario

Start `e2e/churchserve.spec.ts` with a reset hook:

```typescript
import { expect, Page, test } from '@playwright/test';
import { E2E_ACCOUNT } from './test-account';

test.describe('ChurchServe critical workflows', () => {
  test.beforeEach(async ({ request }) => {
    const response = await request.post('/api/test/reset');
    expect(response.status()).toBe(204);
  });

  // Critical scenarios follow.
});
```

The request goes through Angular's test proxy, just like browser API traffic. Every test starts with the two seeded November services, four positions, and six eligible volunteers.

## Test Authentication and Protected Navigation

Use labels and roles rather than CSS selectors:

```typescript
async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(E2E_ACCOUNT.email);
  await page.getByLabel('Password').fill(E2E_ACCOUNT.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page).toHaveURL(
    /\/schedule\?month=\d{4}-\d{2}&page=1$/,
  );
  await expect(
    page.getByRole('heading', { name: 'Upcoming services' }),
  ).toBeVisible();
}
```

The URL assertion accepts ChurchServe's canonical current-month query string instead of assuming sign-in stops at the bare `/schedule` path.

Protect the sign-out boundary:

```typescript
test('signs in and protects the schedule after sign-out', async ({ page }) => {
  await signIn(page);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();

  await page.goto('/schedule');

  await expect(page).toHaveURL(
    /\/sign-in\?returnUrl=%2Fschedule$/,
  );
  await expect(
    page.getByRole('heading', { name: 'Sign in to ChurchServe' }),
  ).toBeVisible();
});
```

This crosses the real sign-out endpoint, cookie invalidation, session restoration, route guard, and safe return URL.

## Test Validation and Event Creation

One browser test can protect the important validation recovery and the successful create path:

```typescript
test('shows validation and creates a service event', async ({ page }) => {
  await signIn(page);
  await page.getByRole('link', { name: 'Create service' }).click();

  await page.getByRole('button', { name: 'Add to schedule' }).click();
  await expect(page.getByLabel('Service name')).toBeFocused();
  await expect(page.getByText('Enter a service name.')).toBeVisible();

  await page.getByLabel('Service name').fill('Community Prayer Night');
  await page.getByLabel('Starts').fill('2026-11-20T19:00');
  await page.getByLabel('Ends').fill('2026-11-20T20:30');
  await page.getByLabel('Location').fill('Prayer Room');
  await page.getByRole('button', { name: 'Add to schedule' }).click();

  await expect(page).toHaveURL(/\/schedule\/[a-f0-9]{32}$/);
  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Community Prayer Night',
    }),
  ).toBeVisible();
  await expect(page.getByText('Prayer Room')).toBeVisible();
});
```

The test verifies the outcome that matters: invalid submission focuses recovery, and successful persistence opens the stable server-generated detail URL.

## Test the Assignment Flow with the Keyboard

Part 23 made assignment focus predictable. Protect that behavior through the browser:

```typescript
test('assigns an eligible volunteer using the keyboard', async ({ page }) => {
  await signIn(page);
  await page.goto('/schedule/sunday-2026-11-01');
  const position = page
    .getByRole('listitem')
    .filter({ hasText: 'Welcome host' });
  const volunteer = position.getByLabel('Volunteer');

  await volunteer.focus();
  await page.keyboard.press('ArrowDown');
  const assign = position.getByRole('button', { name: 'Assign' });
  await expect(assign).toBeEnabled();
  await volunteer.focus();
  await page.keyboard.press('Tab');
  await expect(assign).toBeFocused();
  await page.keyboard.press('Space');

  await expect(page.getByRole('status'))
    .toHaveText('Anna Brooks was assigned.');
  await expect(position).toContainText('Anna Brooks');
  await expect(position).toContainText('Confirmed');
  await expect(position).toBeFocused();
});
```

This uses the browser's native select and button keyboard behavior. It also verifies that the optimistic replacement leaves focus on the stable position row.

## Reproduce a Real Concurrency Conflict

A unit test can simulate `409 Conflict`, but two browser pages can create the actual stale-write condition:

```typescript
test('refreshes a stale assignment after a coordinator conflict', async ({ page }) => {
  await signIn(page);
  await page.goto('/schedule/sunday-2026-11-01');
  const secondPage = await page.context().newPage();
  await secondPage.goto('/schedule/sunday-2026-11-01');

  const firstPosition = page
    .getByRole('listitem')
    .filter({ hasText: 'Welcome host' });
  const stalePosition = secondPage
    .getByRole('listitem')
    .filter({ hasText: 'Welcome host' });
  await firstPosition.getByLabel('Volunteer').selectOption('anna-brooks');
  await stalePosition.getByLabel('Volunteer').selectOption('annette-lee');

  await firstPosition.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByRole('status'))
    .toHaveText('Anna Brooks was assigned.');

  await stalePosition.getByRole('button', { name: 'Assign' }).click();

  await expect(secondPage.getByRole('status')).toContainText(
    'The schedule changed after you loaded it.',
  );
  await expect(stalePosition).toContainText('Anna Brooks');
  await expect(stalePosition).toContainText('Confirmed');
});
```

Both pages load schedule version 1. The first assignment advances the server version. The stale page submits version 1, receives the real conflict response, replaces its optimistic state with the authoritative board, and shows actionable guidance.

No fixed timeout appears in these tests. Playwright's locators and assertions wait for observable browser outcomes.

## Retain Evidence Only for Failures

The configuration uses:

```typescript
use: {
  trace: 'retain-on-failure',
  screenshot: 'only-on-failure',
  video: 'retain-on-failure',
}
```

A trace includes DOM snapshots, actions, network activity, and assertion context. It is usually more useful than a screenshot alone for a route or request failure. Failure-only retention limits storage and reduces the risk of keeping unnecessary data from successful runs.

Do not put production credentials or personal information into browser fixtures. Test artifacts can contain form values, page text, URLs, and response details.

## Run the Browser Suite in CI

Install Chromium after `npm ci`, run the browser tests after the faster checks, and upload evidence only on failure:

```yaml
- name: Install Playwright browser
  run: npx playwright install --with-deps chromium

# Existing unit, architecture, build, and API checks run first.

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

Running the fast suites first gives developers quick feedback. The browser suite remains small enough to run on every pull request instead of becoming a rarely used nightly safety net.

## Verify Part 24

Run the complete verification set:

```bash
dotnet test ChurchServe.slnx
npm run test:ci
npm run test:coverage
npm run check:architecture
npm run build
npm run test:e2e
```

Part 24 adds two API fixture-boundary tests and four Chromium journeys:

1. Sign in, sign out, and reject later access to the protected schedule.
2. Reveal accessible validation, create an event, and open its stable detail URL.
3. Assign an eligible volunteer using only keyboard actions and preserve focus.
4. Create a genuine stale-write conflict across two pages and recover from the authoritative server response.

This suite is intentionally narrow. Field permutations, DTO parsing, retry rules, caching, authorization combinations, and store transitions remain in the faster unit and API integration suites.

Part 25 can now package and deploy the same application whose critical boundaries are exercised in a real browser.
