---
title: "ChurchServe Angular Tutorial Part 9-2: Build an ASP.NET Core API with SQLite"
excerpt: "Scaffold a runnable ASP.NET Core API for ChurchServe, persist service events with EF Core and SQLite, proxy Angular requests, and verify the endpoints with integration tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core SQLite API tutorial"
  description: "Build an ASP.NET Core REST API with EF Core and SQLite, seed service events, connect Angular through a development proxy, and test the API."
  socialTitle: "ChurchServe Part 9-2: ASP.NET Core API with SQLite"
  socialDescription: "Complete ChurchServe's REST boundary with an ASP.NET Core API, EF Core SQLite persistence, Angular proxying, and integration tests."
---

# ChurchServe Angular Tutorial Part 9-2: Build an ASP.NET Core API with SQLite

Part 9-1 created the Angular `HttpClient` boundary, but a client cannot load or save data without a server. Part 9-2 completes the vertical slice by scaffolding an ASP.NET Core minimal API and persisting service events through EF Core and SQLite.

SQLite keeps the tutorial runnable without requiring a database-server installation. The API is structured around EF Core so a later part can replace the provider and connection string with Microsoft SQL Server while preserving the HTTP contract.

> **Quick answer:** Scaffold an ASP.NET Core API and xUnit project, register an EF Core SQLite `DbContext`, create and seed the database, implement list/detail/create endpoints, proxy Angular's `/api` requests to the server, and test the real HTTP and persistence pipeline with in-memory SQLite.

## Files Added and Changed

Part 9-2 adds the backend and connects the development servers:

```text
server/
  ChurchServe.Api/
    Data/
      ChurchServeDbContext.cs
      DatabaseInitializer.cs
    ServiceEvents/
      CreateServiceEventRequest.cs
      ServiceEvent.cs
      ServiceEventEndpoints.cs
      ServiceEventResponse.cs
    Properties/
      launchSettings.json
    appsettings.json
    ChurchServe.Api.csproj
    Program.cs
tests/
  ChurchServe.Api.Tests/
    ChurchServeApiFactory.cs
    ServiceEventEndpointsTests.cs
    ChurchServe.Api.Tests.csproj
proxy.conf.json
angular.json                                          updated
ChurchServe.slnx                                      updated
README.md                                             updated
.gitignore                                            updated
```

The Angular repository code from Part 9-1 does not change. It already targets relative `/api/service-events` URLs.

## Confirm the Shared HTTP Contract

The server implements the routes introduced by the Angular client:

```text
GET  /api/service-events
GET  /api/service-events/{eventId}
POST /api/service-events
```

The server owns persistence and business validation. Angular owns presentation and defensive response parsing. Keeping those responsibilities separate prevents browser code from becoming the authority for IDs, staffing counts, versions, or validation.

## Add the ASP.NET Core and SQLite Projects

Run the following commands from the ChurchServe repository root. They create the projects in dedicated `server` and `tests` folders rather than mixing .NET files into the Angular source tree:

```bash
dotnet new webapi --name ChurchServe.Api --output server/ChurchServe.Api --framework net10.0 --no-openapi --no-https
dotnet new xunit --name ChurchServe.Api.Tests --output tests/ChurchServe.Api.Tests --framework net10.0
dotnet add server/ChurchServe.Api/ChurchServe.Api.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.13
dotnet add tests/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.13
dotnet add tests/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj reference server/ChurchServe.Api/ChurchServe.Api.csproj
dotnet sln ChurchServe.slnx add server/ChurchServe.Api/ChurchServe.Api.csproj --solution-folder server
dotnet sln ChurchServe.slnx add tests/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj --solution-folder tests
```

The scaffolding sequence has four jobs:

1. `dotnet new webapi` creates a minimal ASP.NET Core host. `--no-openapi` avoids adding documentation packages before the tutorial needs them, while `--no-https` keeps the two-process local setup focused on proxying rather than development certificates.
2. `dotnet new xunit` creates the integration-test project.
3. `dotnet add` installs the SQLite provider and ASP.NET Core test host, then gives the tests a project reference to the API.
4. `dotnet sln` adds both projects to `ChurchServe.slnx`, so Visual Studio can build, test, and navigate the complete application.

The generated weather endpoint and `.http` sample target the template application, so remove them before adding the ChurchServe endpoints. The API project references `Microsoft.EntityFrameworkCore.Sqlite`; the test project references `Microsoft.AspNetCore.Mvc.Testing` and the API project. Pinning the EF Core package to the same .NET servicing version keeps the example reproducible.

Configure the SQLite file in `server/ChurchServe.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ChurchServe": "Data Source=churchserve.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

The database file is runtime state, not source code. Add `*.db`, `*.db-shm`, and `*.db-wal` to `.gitignore`, along with the usual .NET `bin` and `obj` directories.

## Model Service Events behind EF Core

Create `ServiceEvents/ServiceEvent.cs`:

```csharp
namespace ChurchServe.Api.ServiceEvents;

public sealed class ServiceEvent
{
  public required string Id { get; init; }

  public required string Title { get; init; }

  public DateTimeOffset StartsAt { get; init; }

  public DateTimeOffset EndsAt { get; init; }

  public required string Location { get; init; }

  public int RequiredPositions { get; init; }

  public int AssignedPositions { get; init; }

  public int Version { get; init; }
}
```

`DateTimeOffset` preserves the offset supplied by the client. `Version` starts at one and prepares the wire contract for later optimistic-concurrency work. It is not yet an EF concurrency token.

Create `Data/ChurchServeDbContext.cs`:

```csharp
using ChurchServe.Api.ServiceEvents;
using Microsoft.EntityFrameworkCore;

namespace ChurchServe.Api.Data;

public sealed class ChurchServeDbContext(DbContextOptions<ChurchServeDbContext> options)
    : DbContext(options)
{
  public DbSet<ServiceEvent> ServiceEvents => Set<ServiceEvent>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    var serviceEvent = modelBuilder.Entity<ServiceEvent>();
    serviceEvent.ToTable("ServiceEvents");
    serviceEvent.HasKey(item => item.Id);
    serviceEvent.Property(item => item.Id).HasMaxLength(64);
    serviceEvent.Property(item => item.Title).HasMaxLength(200);
    serviceEvent.Property(item => item.Location).HasMaxLength(200);
  }
}
```

The `DbContext` is the provider boundary. Endpoint code depends on EF Core, not on SQLite-specific SQL. That separation is what makes a later SQL Server migration manageable.

## Create and Seed the SQLite Database

Create `Data/DatabaseInitializer.cs`:

```csharp
using ChurchServe.Api.ServiceEvents;
using Microsoft.EntityFrameworkCore;

namespace ChurchServe.Api.Data;

public static class DatabaseInitializer
{
  public static async Task InitializeAsync(ChurchServeDbContext dbContext)
  {
    await dbContext.Database.EnsureCreatedAsync();

    if (await dbContext.ServiceEvents.AnyAsync())
    {
      return;
    }

    dbContext.ServiceEvents.AddRange(
        new ServiceEvent
        {
          Id = "sunday-2026-11-01",
          Title = "Sunday Morning Service",
          StartsAt = DateTimeOffset.Parse("2026-11-01T10:00:00-05:00"),
          EndsAt = DateTimeOffset.Parse("2026-11-01T11:30:00-05:00"),
          Location = "Main Sanctuary",
          RequiredPositions = 10,
          AssignedPositions = 7,
          Version = 1,
        },
        new ServiceEvent
        {
          Id = "rehearsal-2026-11-05",
          Title = "Worship Team Rehearsal",
          StartsAt = DateTimeOffset.Parse("2026-11-05T19:00:00-05:00"),
          EndsAt = DateTimeOffset.Parse("2026-11-05T20:30:00-05:00"),
          Location = "Music Room",
          RequiredPositions = 6,
          AssignedPositions = 6,
          Version = 1,
        });

    await dbContext.SaveChangesAsync();
  }
}
```

`EnsureCreatedAsync` is deliberately used for this tutorial checkpoint because there is no deployed schema history yet. It creates the file and tables on first run. A production application should move to reviewed EF Core migrations before multiple environments or upgrades depend on the schema.

The initializer seeds only an empty database, so restarting the API does not overwrite events created through the UI.

## Define Request and Response Contracts

Create `ServiceEvents/CreateServiceEventRequest.cs`:

```csharp
namespace ChurchServe.Api.ServiceEvents;

public sealed record CreateServiceEventRequest(
    string? Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location);
```

Create `ServiceEvents/ServiceEventResponse.cs`:

```csharp
namespace ChurchServe.Api.ServiceEvents;

public sealed record ServiceEventResponse(
    string Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    int RequiredPositions,
    int AssignedPositions,
    int Version);
```

Separate request, persistence, and response types prevent clients from choosing server-owned fields such as IDs, staffing counts, or versions. The browser can send only the four values needed to create an event.

## Implement List, Detail, and Create Endpoints

Create `ServiceEvents/ServiceEventEndpoints.cs`:

```csharp
using ChurchServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChurchServe.Api.ServiceEvents;

public static class ServiceEventEndpoints
{
  public static IEndpointRouteBuilder MapServiceEventEndpoints(this IEndpointRouteBuilder endpoints)
  {
    var group = endpoints.MapGroup("/api/service-events");

    group.MapGet("/", async (ChurchServeDbContext dbContext) =>
    {
      var serviceEvents = await dbContext.ServiceEvents
              .AsNoTracking()
              .ToListAsync();

      return Results.Ok(serviceEvents
              .OrderBy(item => item.StartsAt)
              .Select(ToResponse));
    });

    group.MapGet("/{id}", async (string id, ChurchServeDbContext dbContext) =>
    {
      var serviceEvent = await dbContext.ServiceEvents
              .AsNoTracking()
              .SingleOrDefaultAsync(item => item.Id == id);

      return serviceEvent is null
              ? Results.NotFound()
              : Results.Ok(ToResponse(serviceEvent));
    });

    group.MapPost("/", async (CreateServiceEventRequest request, ChurchServeDbContext dbContext) =>
    {
      var errors = Validate(request);
      if (errors.Count > 0)
      {
        return Results.ValidationProblem(errors);
      }

      var serviceEvent = new ServiceEvent
      {
        Id = Guid.NewGuid().ToString("N"),
        Title = request.Title!.Trim(),
        StartsAt = request.StartsAt,
        EndsAt = request.EndsAt,
        Location = request.Location!.Trim(),
        RequiredPositions = 0,
        AssignedPositions = 0,
        Version = 1,
      };

      dbContext.ServiceEvents.Add(serviceEvent);
      await dbContext.SaveChangesAsync();

      return Results.Created(
              $"/api/service-events/{Uri.EscapeDataString(serviceEvent.Id)}",
              ToResponse(serviceEvent));
    });

    return endpoints;
  }

  private static Dictionary<string, string[]> Validate(CreateServiceEventRequest request)
  {
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Title))
    {
      errors["title"] = ["A service name is required."];
    }

    if (string.IsNullOrWhiteSpace(request.Location))
    {
      errors["location"] = ["A location is required."];
    }

    if (request.EndsAt <= request.StartsAt)
    {
      errors["endsAt"] = ["The end time must be after the start time."];
    }

    return errors;
  }

  private static ServiceEventResponse ToResponse(ServiceEvent serviceEvent) =>
      new(
          serviceEvent.Id,
          serviceEvent.Title,
          serviceEvent.StartsAt,
          serviceEvent.EndsAt,
          serviceEvent.Location,
          serviceEvent.RequiredPositions,
          serviceEvent.AssignedPositions,
          serviceEvent.Version);
}
```

Read queries use `AsNoTracking` because the returned entities are not modified. SQLite's EF Core provider does not translate every `DateTimeOffset` ordering expression, so this small tutorial result set is loaded before ordering. A production schedule endpoint should use a provider-supported sortable representation, filter to a bounded date range, and paginate rather than loading an unbounded table.

The POST endpoint repeats essential validation even though Angular validates its form. Requests can bypass the browser, so server validation is the security and correctness boundary. A successful create returns `201 Created`, the new resource location, and the complete response DTO.

## Configure and Start the API

Replace `Program.cs` with:

```csharp
using ChurchServe.Api.Data;
using ChurchServe.Api.ServiceEvents;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ChurchServe")
    ?? throw new InvalidOperationException("Connection string 'ChurchServe' was not configured.");

builder.Services.AddDbContext<ChurchServeDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
  await DatabaseInitializer.InitializeAsync(
      scope.ServiceProvider.GetRequiredService<ChurchServeDbContext>());
}

app.MapServiceEventEndpoints();

app.Run();

public partial class Program;
```

The partial `Program` declaration gives `WebApplicationFactory<Program>` a public type to locate during integration tests. Set the HTTP launch URL to `http://localhost:5091` in `Properties/launchSettings.json`.

## Proxy Angular Requests during Development

Create `proxy.conf.json` at the repository root:

```json
{
  "/api": {
    "target": "http://localhost:5091",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "info"
  }
}
```

Reference it in the `serve.options` section of `angular.json` while keeping the Angular port at 4201:

```json
"serve": {
  "builder": "@angular/build:dev-server",
  "options": {
    "port": 4201,
    "proxyConfig": "proxy.conf.json"
  }
}
```

The browser calls `http://localhost:4201/api/...`, and the Angular development server forwards that path to port 5091. This avoids development-only CORS configuration and keeps the relative URLs appropriate for a production reverse proxy. Restart `npm start` after adding or changing the proxy configuration; a running Angular process does not reload this setting.

## Test the API with In-Memory SQLite

The server integration tests should exercise the real EF Core SQLite provider without writing test databases to the repository. Create `ChurchServeApiFactory.cs`:

```csharp
using ChurchServe.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChurchServe.Api.Tests;

public sealed class ChurchServeApiFactory : WebApplicationFactory<Program>
{
  private SqliteConnection? connection;

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");
    builder.ConfigureServices(services =>
    {
      services.RemoveAll<DbContextOptions<ChurchServeDbContext>>();

      connection = new SqliteConnection("Data Source=:memory:");
      connection.Open();

      services.AddSingleton(connection);
      services.AddDbContext<ChurchServeDbContext>((serviceProvider, options) =>
              options.UseSqlite(serviceProvider.GetRequiredService<SqliteConnection>()));
    });
  }

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);

    if (disposing)
    {
      connection?.Dispose();
    }
  }
}
```

An in-memory SQLite database lives only while its connection remains open. The factory replaces the file-backed `DbContext` registration, opens one connection for the application lifetime, and disposes it with the test server.

Create `ServiceEventEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using ChurchServe.Api.ServiceEvents;

namespace ChurchServe.Api.Tests;

public sealed class ServiceEventEndpointsTests
{
  [Fact]
  public async Task List_returns_seeded_service_events()
  {
    using var factory = new ChurchServeApiFactory();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/api/service-events");
    var serviceEvents = await response.Content.ReadFromJsonAsync<ServiceEventResponse[]>();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(serviceEvents);
    Assert.Equal(2, serviceEvents.Length);
    Assert.Equal("sunday-2026-11-01", serviceEvents[0].Id);
  }

  [Fact]
  public async Task Detail_returns_not_found_for_an_unknown_id()
  {
    using var factory = new ChurchServeApiFactory();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/api/service-events/missing");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Create_persists_and_returns_a_service_event()
  {
    using var factory = new ChurchServeApiFactory();
    using var client = factory.CreateClient();
    var request = new CreateServiceEventRequest(
        "Christmas Eve Service",
        DateTimeOffset.Parse("2026-12-24T18:00:00-05:00"),
        DateTimeOffset.Parse("2026-12-24T19:30:00-05:00"),
        "Main Sanctuary");

    var response = await client.PostAsJsonAsync("/api/service-events", request);
    var created = await response.Content.ReadFromJsonAsync<ServiceEventResponse>();

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.NotNull(created);
    Assert.Equal("Christmas Eve Service", created.Title);
    Assert.Equal(1, created.Version);

    var stored = await client.GetFromJsonAsync<ServiceEventResponse>(
        $"/api/service-events/{created.Id}");
    Assert.Equal(created, stored);
  }

  [Fact]
  public async Task Create_rejects_an_end_time_before_the_start_time()
  {
    using var factory = new ChurchServeApiFactory();
    using var client = factory.CreateClient();
    var request = new CreateServiceEventRequest(
        "Invalid event",
        DateTimeOffset.Parse("2026-12-24T19:30:00-05:00"),
        DateTimeOffset.Parse("2026-12-24T18:00:00-05:00"),
        "Main Sanctuary");

    var response = await client.PostAsJsonAsync("/api/service-events", request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
```

These are HTTP integration tests, not mocked endpoint-unit tests. They start the application, initialize SQLite, serialize requests, execute routing and validation, persist through EF Core, and deserialize responses.

## Run the Complete Part 9 Application

Start the API from the repository root in one terminal:

```bash
dotnet run --project server/ChurchServe.Api
```

Start Angular in a second terminal:

```bash
npm start
```

Open `http://localhost:4201/schedule`. The first API run creates `server/ChurchServe.Api/churchserve.db` and seeds two visible events. Creating an event through the Angular form writes it to SQLite and navigates to the saved event's detail route.

Run both test suites and the Angular production build:

```bash
dotnet test ChurchServe.slnx
npm test -- --watch=false
npm run build
```

The completed Part 9 application has four passing API integration tests and thirty-nine passing Angular tests. The end-to-end development proxy returns the seeded schedule through Angular's origin.

## Plan the Later SQL Server Migration

SQLite is the Part 9 development database, not the final production database. A later API-focused part can replace `Microsoft.EntityFrameworkCore.Sqlite` with `Microsoft.EntityFrameworkCore.SqlServer`, change `UseSqlite` to `UseSqlServer`, move the connection string into environment-specific configuration or a secret store, and create reviewed EF Core migrations.

The migration still requires testing. SQL Server and SQLite differ in type mapping, query translation, collation, concurrency behavior, and operational characteristics. Keeping provider-specific setup in `Program.cs` and database mapping in the `DbContext` limits the change, but it does not make the providers behaviorally identical.

## Part 9-2 Checkpoint

ChurchServe now has a runnable REST vertical slice. ASP.NET Core owns validation and SQLite persistence; Angular remains storage-agnostic, validates incoming DTOs, and presents honest request states.

SQLite is the development database for this tutorial stage. A later API-focused part can move EF Core to Microsoft SQL Server without redesigning the Angular client introduced in Part 9-1.
