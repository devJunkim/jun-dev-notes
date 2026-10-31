---
title: "ChurchServe React Tutorial Part 12: ASP.NET Core API Integration Tests"
excerpt: "Test a React application's ASP.NET Core API through real HTTP requests, EF Core migrations, isolated SQLite databases, validation failures, and restart persistence."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core API integration tests WebApplicationFactory"
  description: "Build ASP.NET Core API integration tests with WebApplicationFactory, xUnit v3, real EF Core migrations, isolated SQLite files, and restart verification."
  socialTitle: "ASP.NET Core API Integration Tests with SQLite"
  socialDescription: "Test routing, JSON, validation, EF Core migrations, SQLite persistence, and host restarts through the real ASP.NET Core HTTP pipeline."
---

# ChurchServe React Tutorial Part 12: ASP.NET Core API Integration Tests

[Part 11](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-11-ef-core-sqlite-and-migrations/) replaced process memory with EF Core and SQLite. Its manual smoke test proved that a created record survived an API restart, but a manual check is easy to skip and difficult to reproduce consistently.

Part 12 turns that persistence boundary into an automated suite. `WebApplicationFactory` starts the real Minimal API in a test host. Each test receives a unique temporary SQLite database, applies the production migration, makes real HTTP requests, and removes its database afterward. One test deliberately stops and recreates the host against the same file to prove that saved data survives beyond one application instance.

> **Quick answer:** Reference the API from a separate xUnit test project, make the top-level `Program` type public, and derive a factory from `WebApplicationFactory<Program>`. Replace the production `DbContext` registration with one that targets a unique temporary SQLite file. Keep the real provider, migrations, routing, JSON serialization, validation, and endpoint handlers in the test path. Disable SQLite connection pooling for test files so host disposal releases them predictably, and verify persistence by reopening the same database from a second host.

## What Part 12 adds

The server test project is intentionally small:

```text
global.json
server/
  ChurchServe.Api/
    Program.cs
  ChurchServe.Api.Tests/
    ChurchServe.Api.Tests.csproj
    ChurchServeApiFactory.cs
    ServiceEventsApiTests.cs
```

The React source does not change. Its fetch repository already has focused adapter tests, and the route tests already cover browser-visible loading and failure states. This checkpoint tests the other side of that boundary: whether an actual HTTP request reaches the correct API behavior and durable storage.

## Choose the integration boundary deliberately

An endpoint unit test can invoke a handler with mocked dependencies. That is useful for complicated pure logic, but it cannot prove that the application is wired correctly.

These integration tests keep the following production behavior in scope:

- Minimal API routing and parameter binding;
- JSON request and response serialization;
- status codes, headers, and Problem Details;
- the real server validator;
- dependency-injection scopes;
- EF Core query translation;
- the SQLite provider;
- the committed migration and startup seeding;
- persistence across separate test hosts.

They do not open a TCP port or automate a browser. `WebApplicationFactory` provides an `HttpClient` backed by ASP.NET Core's in-process test server. The request still travels through the application pipeline, but the suite remains fast and self-contained.

Browser end-to-end tests will eventually cover the combined React and API deployment. They should complement this layer rather than replace it.

## Create a modern .NET test project

Create `ChurchServe.Api.Tests.csproj` beside the API project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference
      Include="Microsoft.AspNetCore.Mvc.Testing"
      Version="10.0.14" />
    <PackageReference Include="xunit.v3" Version="4.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ChurchServe.Api\ChurchServe.Api.csproj" />
  </ItemGroup>
</Project>
```

`Microsoft.AspNetCore.Mvc.Testing` supplies `WebApplicationFactory`. The project reference makes the API entry point and application assemblies available to the tests. xUnit v3 runs as an executable on Microsoft Testing Platform, so `OutputType` must be `Exe`.

Keep the ASP.NET Core testing package on the same patch line as the API runtime. Version drift can create behavior that differs from the application being tested.

## Select Microsoft Testing Platform for .NET 10

The .NET 10 SDK can run either the older VSTest path or Microsoft Testing Platform. Select the modern runner in the repository-root `global.json`:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

xUnit v3 includes native Microsoft Testing Platform support. Keeping runner selection in source control means local development and CI use the same `dotnet test` behavior instead of depending on machine defaults.

Runner configuration is repository-wide. Before adding it to a larger solution, confirm that every test project supports the selected platform.

## Expose the top-level Program type

Minimal API code uses top-level statements, so the compiler generates the entry-point type. `WebApplicationFactory<TEntryPoint>` needs a public type it can reference from another assembly.

Add this declaration at the end of `Program.cs`:

```csharp
public partial class Program;
```

The declaration does not create another application entry point. It augments the compiler-generated partial `Program` type and makes that type public to the test project.

Avoid making application internals public only to test every method. This change exposes the host entry point because the test framework needs it; endpoint behavior is still exercised through HTTP.

## Give every factory a unique database

The factory chooses a file in the operating-system temporary directory:

```csharp
public sealed class ChurchServeApiFactory : WebApplicationFactory<Program>
{
    public ChurchServeApiFactory(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Path.GetTempPath(),
            $"churchserve-api-tests-{Guid.NewGuid():N}.db");
    }

    public string DatabasePath { get; }
}
```

The GUID prevents parallel tests from sharing state. The optional path supports the restart test, which intentionally gives two factories the same database.

A file-backed database is slightly slower than SQLite's in-memory mode, but it verifies behavior that matters in this checkpoint:

- the migration creates a new physical database;
- the data outlives one host and its service provider;
- journal files and connection cleanup behave like file-backed SQLite;
- tests cannot pass merely because one open in-memory connection retains state.

Do not point tests at the developer database from `appsettings.json`. That creates order-dependent results and risks changing local data.

## Replace the DbContext registration, not only a setting

Override the test host's services:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");
    builder.ConfigureServices(services =>
    {
        services.RemoveAll<DbContextOptions<ChurchServeDbContext>>();
        services.RemoveAll<
            IDbContextOptionsConfiguration<ChurchServeDbContext>>();

        services.AddDbContext<ChurchServeDbContext>(options =>
            options.UseSqlite(
                $"Data Source={DatabasePath};Pooling=False"));
    });
}
```

Removing both EF Core registrations matters. Adding a second `DbContext` configuration can leave competing option sources in the service collection. Replacing the registrations makes the test database selection explicit.

An early version tried only to add a test connection string through host configuration. The application had already consumed its connection string while registering the production context, so tests reached the developer database. A failing count assertion exposed the leak. Replacing the dependency itself fixed the isolation boundary.

This is why a test that merely returns `200` is not enough. Assert known seed counts and values so accidental shared state becomes visible.

## Disable pooling for disposable database files

The test connection string includes `Pooling=False`:

```text
Data Source=<unique temporary path>;Pooling=False
```

SQLite connection pooling is useful in an application, but a pool can retain a handle after the test host is disposed. On Windows, deleting the database then fails because another process handle still owns the file.

Disabling pooling only in the test factory makes lifetime deterministic: disposing the factory and its client releases the file. Production keeps its normal connection behavior.

Cleanup removes the main file and SQLite sidecar files:

```csharp
public static void DeleteDatabaseFiles(string databasePath)
{
    DeleteIfPresent(databasePath);
    DeleteIfPresent($"{databasePath}-shm");
    DeleteIfPresent($"{databasePath}-wal");
}
```

Cleanup belongs in `finally` so a failed assertion does not leave validation artifacts behind.

## Let startup apply the real migration

The factory does not call `EnsureCreated`. When the client starts the test host, the existing application startup executes:

```csharp
await DatabaseInitializer.InitializeAsync(app.Services);
```

That initializer calls `MigrateAsync`, checks whether any service events exist, and inserts seed data only into an empty database.

Using `EnsureCreated` would build a schema directly from the current model and bypass the migration history. Such a test could remain green even if the committed migrations could not build a new database. Part 12 intentionally tests the deployment artifact represented by the migration.

## Test the seeded schedule and ordering

The first test asks the real endpoint for its collection:

```csharp
[Fact]
public Task List_returns_seeded_events_in_start_order() =>
    WithApiAsync(async client =>
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var events = await client.GetFromJsonAsync<ServiceEventResponse[]>(
            "/api/service-events",
            cancellationToken);

        Assert.NotNull(events);
        Assert.Collection(
            events,
            first => Assert.Equal("sunday-2026-11-01", first.Id),
            second => Assert.Equal("rehearsal-2026-11-05", second.Id));
        Assert.True(events[0].StartsAt < events[1].StartsAt);
    });
```

This one test crosses routing, database initialization, SQLite timestamp ordering, JSON serialization, and response deserialization. It also detects database leakage: an unexpected record changes the collection assertion.

Pass `TestContext.Current.CancellationToken` to asynchronous operations. A cancelled test run can then stop outstanding HTTP and content work instead of waiting for default timeouts.

## Keep absence distinct from failure

The detail test exercises both branches of the route:

```csharp
var existing = await client.GetAsync(
    "/api/service-events/sunday-2026-11-01",
    cancellationToken);
var missing = await client.GetAsync(
    "/api/service-events/does-not-exist",
    cancellationToken);

Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
```

The test then deserializes the successful response and checks its title. Status alone does not prove that the endpoint returned the intended record.

This contract aligns with the React fetch repository: only `404` becomes an absent detail. Server errors and invalid payloads remain failures.

## Verify validation without changing storage

Send an invalid creation request with blank text and a reversed time range:

```csharp
var response = await client.PostAsJsonAsync(
    "/api/service-events",
    new CreateServiceEventRequest(
        " ",
        new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.Zero),
        null),
    cancellationToken);

Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
```

Inspect the Problem Details `errors` object for `title`, `location`, and `timeRange`. Then list the records and assert that only the two seeds remain.

The final count assertion is important. A handler that returns `400` after saving would satisfy a status-only test while corrupting storage.

## Prove creation survives a host restart

The persistence test supplies one explicit temporary path to two factories. The first host creates a record:

```csharp
using (var firstFactory = new ChurchServeApiFactory(databasePath))
using (var firstClient = firstFactory.CreateClient())
{
    var response = await firstClient.PostAsJsonAsync(
        "/api/service-events",
        request,
        cancellationToken);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var created =
        await response.Content.ReadFromJsonAsync<ServiceEventResponse>(
            cancellationToken);
    Assert.NotNull(created);
    createdId = created.Id;
    Assert.Equal(
        new Uri($"/api/service-events/{createdId}", UriKind.Relative),
        response.Headers.Location);
}
```

Disposing the factory stops that host and releases its scoped services. The second host points to the same file:

```csharp
using (var restartedFactory = new ChurchServeApiFactory(databasePath))
using (var restartedClient = restartedFactory.CreateClient())
{
    var persisted =
        await restartedClient.GetFromJsonAsync<ServiceEventResponse>(
            $"/api/service-events/{createdId}",
            cancellationToken);

    Assert.NotNull(persisted);
    Assert.Equal("Prayer Night", persisted.Title);
    Assert.Equal("Community Room", persisted.Location);
}
```

Startup sees an up-to-date migration and a nonempty table, so it neither rebuilds the schema nor inserts duplicate seeds. Retrieval through the new host proves the POST wrote to the database rather than an application singleton.

The test uses `finally` to delete the database whether creation, restart, or an assertion fails.

## Keep test DTOs at the HTTP boundary

The tests define small request and response records that match the public JSON contract:

```csharp
private sealed record ServiceEventResponse(
    string Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    int RequiredPositions,
    int AssignedPositions);
```

Reusing the API's response type would make deserialization convenient, but an accidental server-side rename could update both producer and test consumer at compile time. A test-local representation gives the HTTP shape a small degree of independence.

For a larger system, contract snapshots, OpenAPI validation, or generated clients can make that boundary more systematic. Avoid duplicating the entire domain model inside the test project.

## Run the complete Part 12 checkpoint

Run the API integration suite directly:

```bash
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
```

Run the existing React and API checks as well:

```bash
npm run lint
npm run typecheck
npm test
npm run build
dotnet tool restore
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The finalized checkpoint passes four API integration tests. They create isolated databases through the real migration, verify the two seeded records, distinguish existing and missing details, reject invalid creation without changing storage, return a correct `201 Created` response, and retrieve the created record after a new host opens the same database.

The checkpoint also keeps all 49 React tests green, passes lint and strict TypeScript checking, builds the Vite bundle, builds the .NET API with zero warnings and errors, and reports no pending EF Core model changes.

## What these tests still do not prove

Integration coverage has boundaries:

- The test server is in-process and does not prove production reverse-proxy, TLS, DNS, or socket configuration.
- Temporary local SQLite does not reproduce shared production storage, disk latency, backup failures, or multi-node concurrency.
- The suite does not run a browser, so it cannot prove that Vite or production routing connects React to the API.
- Authentication and authorization do not exist yet, so no identity or permission behavior is covered.
- Four focused scenarios are not exhaustive input testing. Validator unit tests can efficiently cover larger value matrices.
- The restart test creates a new host in one operating-system process. A release smoke test should eventually launch the published application as a separate process.
- Parallel tests are isolated by file name, but any future external dependency also needs a test-safe lifecycle.

Part 13 can introduce structured API error handling and integration coverage for unexpected database failures, or begin the authentication boundary so authorization behavior becomes testable before more write operations are added.

## What Part 12 established

The server boundary now has repeatable evidence instead of only a manual smoke test.

- `WebApplicationFactory` executes the real Minimal API pipeline.
- xUnit v3 runs through Microsoft Testing Platform under .NET 10.
- A public partial `Program` gives the test project a stable entry point.
- Every ordinary test receives a unique temporary database.
- The factory replaces EF Core registrations explicitly.
- The production migration and seed initializer run during test startup.
- File-backed SQLite verifies persistence across separate hosts.
- Disabled test-only pooling makes cleanup deterministic on Windows.
- Tests assert response bodies, statuses, headers, validation errors, and storage effects.
- Cancellation tokens allow interrupted test runs to stop pending I/O.

Part 12 protects the API contract that the React application already consumes and gives later server changes a trustworthy regression boundary.
