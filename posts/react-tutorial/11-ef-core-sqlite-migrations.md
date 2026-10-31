---
title: "ChurchServe React Tutorial Part 11: EF Core, SQLite, and Migrations"
excerpt: "Replace process-memory API storage with durable SQLite persistence using EF Core entities, migrations, asynchronous queries, idempotent seed data, and restart verification."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core EF Core SQLite migrations"
  description: "Add durable SQLite persistence to an ASP.NET Core API with EF Core migrations, UTC timestamp conversion, asynchronous endpoints, and restart testing."
  socialTitle: "ASP.NET Core EF Core Persistence with SQLite Migrations"
  socialDescription: "Move a React application's ASP.NET Core API from process memory to durable SQLite data with EF Core, migrations, safe seeding, and async queries."
---

# ChurchServe React Tutorial Part 11: EF Core, SQLite, and Migrations

[Part 10](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-10-fetch-repository-and-asp-net-core-api/) connected React to an ASP.NET Core Minimal API. Its thread-safe store was useful for proving the HTTP contract, but every API restart restored the original seed data. A second API instance would also have its own unrelated list.

Part 11 replaces that process-memory store with Entity Framework Core and SQLite. The HTTP contract stays the same, so the React repository and route components do not change. The API now uses a scoped `DbContext`, a database entity, an initial migration, asynchronous queries, and seed logic that runs only when the table is empty.

> **Quick answer:** Add the EF Core SQLite provider and a pinned local `dotnet-ef` tool, map a persistence entity through a `DbContext`, and create an initial migration. Apply migrations before the API begins serving requests, seed only an empty table, use `AsNoTracking` for read-only queries, and await database operations with the request cancellation token. For SQLite, explicitly choose a queryable representation for `DateTimeOffset`; this checkpoint stores UTC ticks so ordering occurs correctly in SQL.

## What Part 11 changes

The server gains a durable data boundary:

```text
.config/
  dotnet-tools.json
server/ChurchServe.Api/
  appsettings.json
  Data/
    ChurchServeDbContext.cs
    DatabaseInitializer.cs
    Migrations/
      20261031130519_InitialServiceEvents.cs
      20261031130519_InitialServiceEvents.Designer.cs
      ChurchServeDbContextModelSnapshot.cs
  ServiceEvents/
    ServiceEventEntity.cs
```

The old `ServiceEventStore` is removed. `Program.cs` now talks to `ChurchServeDbContext`, while the existing request validator and API response record remain at the HTTP boundary.

This separation matters: changing persistence does not require React to know whether the server uses a list, SQLite, or another database.

## Add the EF Core packages and local tool

Add the SQLite provider and design-time package to the API project:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.14">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.14" />
</ItemGroup>
```

Keep all EF Core packages on the same version. `Microsoft.EntityFrameworkCore.Sqlite` supplies the runtime provider. The design package supports migrations and other tooling but stays private instead of flowing as a dependency to consumers.

Pin `dotnet-ef` in `.config/dotnet-tools.json`:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "10.0.14",
      "commands": ["dotnet-ef"],
      "rollForward": false
    }
  }
}
```

A local manifest gives every contributor and build agent the same migration tool version:

```bash
dotnet tool restore
```

The manifest belongs in source control. The restored tool files do not.

## Keep the database entity behind the API model

Create a mutable EF Core entity:

```csharp
public sealed class ServiceEventEntity
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string Location { get; set; }
    public int RequiredPositions { get; set; }
    public int AssignedPositions { get; set; }
}
```

This is a persistence shape, not the public contract. EF Core needs to materialize and track it, while the existing immutable `ServiceEvent` response record remains what the API returns.

Map the entity before sending it across HTTP:

```csharp
static ServiceEvent ToResponse(ServiceEventEntity serviceEvent) =>
    new(
        serviceEvent.Id,
        serviceEvent.Title,
        serviceEvent.StartsAt,
        serviceEvent.EndsAt,
        serviceEvent.Location,
        serviceEvent.RequiredPositions,
        serviceEvent.AssignedPositions);
```

This explicit mapping prevents database concerns from silently becoming the client contract. A later schema change can add persistence-only columns without exposing them to the browser.

## Configure the DbContext

The context exposes the table and centralizes schema rules:

```csharp
public sealed class ChurchServeDbContext(
    DbContextOptions<ChurchServeDbContext> options) : DbContext(options)
{
    public DbSet<ServiceEventEntity> ServiceEvents => Set<ServiceEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var serviceEvent = modelBuilder.Entity<ServiceEventEntity>();
        serviceEvent.ToTable("ServiceEvents");
        serviceEvent.HasKey(item => item.Id);
        serviceEvent.Property(item => item.Id).HasMaxLength(64);
        serviceEvent.Property(item => item.Title).HasMaxLength(120).IsRequired();
        serviceEvent.Property(item => item.Location).HasMaxLength(120).IsRequired();
    }
}
```

The length limits agree with server validation. Validation produces helpful `400` responses; database constraints protect storage if another code path writes an entity.

Register the context with a configured connection string:

```csharp
var connectionString = builder.Configuration.GetConnectionString("ChurchServe")
    ?? throw new InvalidOperationException(
        "Connection string 'ChurchServe' is required.");

builder.Services.AddDbContext<ChurchServeDbContext>(options =>
    options.UseSqlite(connectionString));
```

The development setting in `appsettings.json` is:

```json
{
  "ConnectionStrings": {
    "ChurchServe": "Data Source=churchserve.db"
  }
}
```

Ignore `*.db`, `*.db-shm`, and `*.db-wal`. A developer database is runtime state, not source code. Production connection details should come from deployment configuration or a secret provider rather than a committed settings file.

## Store DateTimeOffset in a sortable SQLite representation

The first implementation exposed a provider-specific problem. SQLite has no native `DateTimeOffset` type. Its EF Core provider can preserve a value as text, but it cannot translate every comparison or ordering operation over that representation. The schedule query failed when SQL attempted `OrderBy(item => item.StartsAt)`.

Configure both timestamps as UTC ticks:

```csharp
serviceEvent.Property(item => item.StartsAt)
    .HasConversion(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero))
    .IsRequired();

serviceEvent.Property(item => item.EndsAt)
    .HasConversion(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero))
    .IsRequired();
```

The migration consequently creates `StartsAt` and `EndsAt` as SQLite `INTEGER` columns. Numeric ordering now matches chronological ordering and remains server-side.

This choice normalizes timestamps to UTC and does not preserve their originally submitted offsets. That is appropriate when the application cares about an instant. If the original local offset or named time zone is part of the business meaning, persist it separately. A UTC offset alone also cannot express future daylight-saving rules.

## Create and inspect the initial migration

Generate the schema from the model:

```bash
dotnet ef migrations add InitialServiceEvents \
  --project server/ChurchServe.Api
```

The important part of the generated migration is explicit and reviewable:

```csharp
migrationBuilder.CreateTable(
    name: "ServiceEvents",
    columns: table => new
    {
        Id = table.Column<string>(
            type: "TEXT", maxLength: 64, nullable: false),
        Title = table.Column<string>(
            type: "TEXT", maxLength: 120, nullable: false),
        StartsAt = table.Column<long>(type: "INTEGER", nullable: false),
        EndsAt = table.Column<long>(type: "INTEGER", nullable: false),
        Location = table.Column<string>(
            type: "TEXT", maxLength: 120, nullable: false),
        RequiredPositions = table.Column<int>(
            type: "INTEGER", nullable: false),
        AssignedPositions = table.Column<int>(
            type: "INTEGER", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_ServiceEvents", x => x.Id);
    });
```

Commit the migration and model snapshot together. They are the versioned instructions for moving an existing database forward, not disposable generated build output.

After any model change, check whether another migration is required:

```bash
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

## Apply migrations and seed an empty database

The tutorial initializes its local database before serving requests:

```csharp
public static async Task InitializeAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider
        .GetRequiredService<ChurchServeDbContext>();

    await dbContext.Database.MigrateAsync();

    if (await dbContext.ServiceEvents.AnyAsync())
    {
        return;
    }

    dbContext.ServiceEvents.AddRange(
        new ServiceEventEntity
        {
            Id = "sunday-2026-11-01",
            Title = "Sunday Morning Service",
            // remaining fields
        });

    await dbContext.SaveChangesAsync();
}
```

Call it after building the application and before mapping traffic:

```csharp
var app = builder.Build();
await DatabaseInitializer.InitializeAsync(app.Services);
```

`MigrateAsync` creates a new database and applies later migrations to an existing one. The `AnyAsync` guard makes seed insertion repeatable: restarting the API does not append duplicates or overwrite user-created records.

This is convenient for a single-instance tutorial. In a production deployment, several instances can start concurrently and race while changing the schema. Prefer a controlled deployment migration step, use database-native locking where required, and give the running application only the permissions it needs.

## Query with a scoped context

`AddDbContext` registers the context as scoped by default. Minimal API handlers can receive one directly for each request.

The list endpoint uses a read-only query:

```csharp
serviceEvents.MapGet("", async (
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entities = await dbContext.ServiceEvents
        .AsNoTracking()
        .OrderBy(item => item.StartsAt)
        .ThenBy(item => item.Id)
        .ToArrayAsync(cancellationToken);

    return Results.Ok(entities.Select(ToResponse).ToArray());
});
```

`AsNoTracking` avoids change-tracking work for entities that will only be mapped and returned. Ordering happens in SQLite before materialization. The ID provides deterministic ordering when two events share a start time.

ASP.NET Core supplies a cancellation token tied to request cancellation. Passing it to EF Core allows abandoned requests to stop waiting for database work when the provider can cancel it.

Detail lookup preserves the Part 10 `404` contract:

```csharp
var serviceEvent = await dbContext.ServiceEvents
    .AsNoTracking()
    .SingleOrDefaultAsync(
        item => item.Id == eventId,
        cancellationToken);

return serviceEvent is null
    ? Results.NotFound()
    : Results.Ok(ToResponse(serviceEvent));
```

## Persist creation before returning 201

The POST endpoint still validates the untrusted request before constructing an entity:

```csharp
var errors = ServiceEventValidator.Validate(request);
if (errors.Count > 0)
{
    return Results.ValidationProblem(errors);
}

var serviceEvent = new ServiceEventEntity
{
    Id = Guid.NewGuid().ToString(),
    Title = request.Title!.Trim(),
    StartsAt = request.StartsAt!.Value,
    EndsAt = request.EndsAt!.Value,
    Location = request.Location!.Trim(),
    RequiredPositions = 0,
    AssignedPositions = 0,
};

dbContext.ServiceEvents.Add(serviceEvent);
await dbContext.SaveChangesAsync(cancellationToken);
```

Return `201 Created` only after `SaveChangesAsync` succeeds:

```csharp
return Results.Created(
    $"/api/service-events/{Uri.EscapeDataString(serviceEvent.Id)}",
    ToResponse(serviceEvent));
```

A successful response now means the server committed the record to SQLite. It does not guarantee every possible durability property: storage hardware, transaction design, backup policy, and deployment topology still matter.

## Verify persistence across a restart

Start the API:

```bash
dotnet run --project server/ChurchServe.Api
```

Create a record through `POST /api/service-events`, stop the API, and start it again. Then request both the list and the created ID. The Part 11 smoke test observed three records after restart and retrieved the created `Persistent Prayer Night` record by its generated ID.

That restart is the important acceptance boundary. A successful POST followed immediately by a GET could still pass with process memory. Retrieval after a new process proves that SQLite, the migration, and the query path work together.

## Run the complete validation

Run the client checks even though the React contract did not change:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

Validate the API and its migration state:

```bash
dotnet tool restore
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The finalized checkpoint passes lint and strict TypeScript checking, runs 49 React tests across 9 files, builds the Vite bundle, builds the .NET API with zero warnings and errors, and reports no model changes missing from migrations.

## Production boundaries

Part 11 provides durable local storage, not a complete production data platform.

- SQLite is a strong fit for local development and some single-node deployments. It is not shared storage for horizontally scaled API instances.
- Schema migration during application startup is convenient here but should become a controlled deployment operation before multi-instance production hosting.
- The database file needs deliberate placement, permissions, backups, restoration testing, and monitoring.
- SQLite serializes writes. Measure actual concurrency requirements before assuming it fits a write-heavy workload.
- API authentication and authorization are still absent. Persistence must not make unauthenticated data modification acceptable.
- Creation can still be duplicated after an ambiguous client retry. Add an idempotency strategy or a business uniqueness rule.
- Concurrent updates will eventually need optimistic concurrency handling and a clear `409 Conflict` policy.
- Database exceptions should be logged with trace context and translated into safe Problem Details responses without exposing SQL or connection information.
- Keep connection strings and credentials outside committed production configuration.

Part 12 can add API integration tests that run against an isolated database, proving migrations, validation, status codes, and persistence without relying only on manual smoke tests.

## What Part 11 established

The application now retains server data across API restarts without changing its React routes.

- EF Core owns the database mapping and unit of work.
- SQLite replaces the singleton process-memory list.
- A pinned local tool makes migration commands reproducible.
- The initial migration versions the schema in source control.
- UTC ticks make timestamp ordering translatable and chronological in SQLite.
- Idempotent seed logic initializes only an empty table.
- Read endpoints use asynchronous, no-tracking queries.
- Request cancellation flows into database operations.
- Creation returns success only after persistence completes.
- A restart smoke test proves the created record survives process termination.

The server now has a durable persistence boundary that later tests, authorization rules, and concurrency controls can build upon.
