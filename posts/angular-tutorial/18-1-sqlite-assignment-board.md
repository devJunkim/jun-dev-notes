---
title: "ChurchServe Angular Tutorial Part 18-1: Build a SQLite Assignment Board"
excerpt: "Add persisted ministry positions and assignments to ChurchServe, then expose one read model for the service-event assignment screen."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core SQLite assignment board"
  description: "Model ministry positions and volunteer assignments with EF Core and SQLite, then return an assignment-board read model from ASP.NET Core."
  socialTitle: "ChurchServe Part 18-1: SQLite Assignment Board"
  socialDescription: "Persist staffing positions and assignments and load the complete coordinator board in one API request."
---

# ChurchServe Angular Tutorial Part 18-1: Build a SQLite Assignment Board

[Part 17](https://dev.jun-kim.net/2026/10/17/churchserve-angular-tutorial-part-17-feature-architecture-and-lazy-routes/) gave ChurchServe enforceable feature boundaries. Part 18 begins the first coordinator write workflow: assigning an eligible volunteer to an open ministry position.

That feature spans enough database, API, state-management, and UI code to deserve three focused articles. Part 18-1 builds the persisted assignment board. Part 18-2 adds the write command, idempotency, and version conflicts. Part 18-3 adds the optimistic Angular experience.

> **Quick answer:** Store positions and assignments as separate relational records, protect the important rules with database constraints, and return the event version, positions, eligible volunteers, and current assignments as one screen-shaped read model. This tutorial seeds positions for the existing sample events; creating positions through the UI is intentionally not implemented yet.

## What This Part Adds

The new server structure is:

```text
server/ChurchServe.Api/
├── Assignments/
│   ├── Assignment.cs
│   ├── AssignmentContracts.cs
│   ├── AssignmentEndpoints.cs
│   └── MinistryPosition.cs
├── Data/
│   ├── ChurchServeDbContext.cs
│   └── DatabaseInitializer.cs
└── Program.cs
```

`MinistryPosition` describes one role to fill for one event. `Assignment` connects that position to a volunteer. Keeping them separate lets the UI show an open position before anyone has been assigned.

## Add the Persistence Models

Create `server/ChurchServe.Api/Assignments/MinistryPosition.cs`:

```csharp
using ChurchServe.Api.ServiceEvents;

namespace ChurchServe.Api.Assignments;

public sealed class MinistryPosition
{
  public required string Id { get; init; }
  public required string ServiceEventId { get; init; }
  public required string Ministry { get; init; }
  public required string RoleName { get; init; }
  public DateTime StartsAt { get; init; }
  public DateTime EndsAt { get; init; }
  public ServiceEvent ServiceEvent { get; init; } = null!;
  public ICollection<Assignment> Assignments { get; } = [];
}
```

The position owns its actual working interval. A sound operator may need to arrive before the service begins, so conflict detection should compare position times rather than only the parent event times.

Create `server/ChurchServe.Api/Assignments/Assignment.cs`:

```csharp
using ChurchServe.Api.Volunteers;

namespace ChurchServe.Api.Assignments;

public sealed class Assignment
{
  public required string Id { get; init; }
  public required string PositionId { get; init; }
  public required string VolunteerId { get; init; }
  public required string ClientRequestId { get; init; }
  public required string Status { get; init; }
  public MinistryPosition Position { get; init; } = null!;
  public Volunteer Volunteer { get; init; } = null!;
}
```

`ClientRequestId` will become the idempotency key in Part 18-2. `Status` leaves room for a later decline workflow while this part creates accepted assignments only.

## Define the API Read Model

Create `server/ChurchServe.Api/Assignments/AssignmentContracts.cs`:

```csharp
using ChurchServe.Api.Volunteers;

namespace ChurchServe.Api.Assignments;

public sealed record AssignVolunteerRequest(
    string? PositionId,
    string? VolunteerId,
    int ExpectedEventVersion,
    string? ClientRequestId);

public sealed record MinistryPositionResponse(
    string Id,
    string EventId,
    string Ministry,
    string RoleName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record AssignmentResponse(
    string Id,
    string PositionId,
    string VolunteerId,
    string VolunteerDisplayName,
    string Status);

public sealed record AssignmentBoardResponse(
    string EventId,
    int EventVersion,
    int AssignedPositions,
    IReadOnlyList<MinistryPositionResponse> Positions,
    IReadOnlyList<VolunteerResponse> Volunteers,
    IReadOnlyList<AssignmentResponse> Assignments);

public sealed record AssignVolunteerResponse(
    AssignmentResponse Assignment,
    int EventVersion,
    int AssignedPositions);

public sealed record AssignmentConflictResponse(
    string Message,
    AssignmentBoardResponse Board);
```

The board is a screen-shaped response rather than three client-side joins. It also carries `EventVersion`; the next article uses that value as an optimistic concurrency token.

## Map Relationships and Constraints

Add these sets to `ChurchServeDbContext`:

```csharp
public DbSet<MinistryPosition> MinistryPositions => Set<MinistryPosition>();
public DbSet<Assignment> Assignments => Set<Assignment>();
```

Then add the following mappings to `OnModelCreating`:

```csharp
serviceEvent.Property(item => item.Version).IsConcurrencyToken();

var position = modelBuilder.Entity<MinistryPosition>();
position.ToTable("MinistryPositions");
position.HasKey(item => item.Id);
position.Property(item => item.Id).HasMaxLength(64);
position.Property(item => item.ServiceEventId).HasMaxLength(64);
position.Property(item => item.Ministry).HasMaxLength(100);
position.Property(item => item.RoleName).HasMaxLength(200);
position.HasOne(item => item.ServiceEvent)
    .WithMany()
    .HasForeignKey(item => item.ServiceEventId)
    .OnDelete(DeleteBehavior.Cascade);

var assignment = modelBuilder.Entity<Assignment>();
assignment.ToTable("Assignments");
assignment.HasKey(item => item.Id);
assignment.Property(item => item.Id).HasMaxLength(64);
assignment.Property(item => item.PositionId).HasMaxLength(64);
assignment.Property(item => item.VolunteerId).HasMaxLength(64);
assignment.Property(item => item.ClientRequestId).HasMaxLength(64);
assignment.Property(item => item.Status).HasMaxLength(32);
assignment.HasIndex(item => item.ClientRequestId).IsUnique();
assignment.HasIndex(item => item.PositionId)
    .IsUnique()
    .HasFilter("Status <> 'declined'");
assignment.HasOne(item => item.Position)
    .WithMany(item => item.Assignments)
    .HasForeignKey(item => item.PositionId)
    .OnDelete(DeleteBehavior.Cascade);
assignment.HasOne(item => item.Volunteer)
    .WithMany()
    .HasForeignKey(item => item.VolunteerId)
    .OnDelete(DeleteBehavior.Restrict);
```

The filtered unique index is an important last line of defense: two requests cannot leave two active assignments on the same position. Application validation improves the error message, but the database protects the invariant.

## Upgrade the Tutorial Database Safely

Earlier tutorial parts used `EnsureCreated`, which does not evolve an existing schema. Add idempotent SQLite statements after `EnsureCreatedAsync()` so readers can keep their local data:

```csharp
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS "MinistryPositions" (
      "Id" TEXT NOT NULL CONSTRAINT "PK_MinistryPositions" PRIMARY KEY,
      "ServiceEventId" TEXT NOT NULL,
      "Ministry" TEXT NOT NULL,
      "RoleName" TEXT NOT NULL,
      "StartsAt" TEXT NOT NULL,
      "EndsAt" TEXT NOT NULL,
      CONSTRAINT "FK_MinistryPositions_ServiceEvents_ServiceEventId"
        FOREIGN KEY ("ServiceEventId") REFERENCES "ServiceEvents" ("Id") ON DELETE CASCADE
    );
    """);
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE INDEX IF NOT EXISTS "IX_MinistryPositions_ServiceEventId"
      ON "MinistryPositions" ("ServiceEventId");
    """);
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS "Assignments" (
      "Id" TEXT NOT NULL CONSTRAINT "PK_Assignments" PRIMARY KEY,
      "PositionId" TEXT NOT NULL,
      "VolunteerId" TEXT NOT NULL,
      "ClientRequestId" TEXT NOT NULL,
      "Status" TEXT NOT NULL,
      CONSTRAINT "FK_Assignments_MinistryPositions_PositionId"
        FOREIGN KEY ("PositionId") REFERENCES "MinistryPositions" ("Id") ON DELETE CASCADE,
      CONSTRAINT "FK_Assignments_Volunteers_VolunteerId"
        FOREIGN KEY ("VolunteerId") REFERENCES "Volunteers" ("Id") ON DELETE RESTRICT
    );
    """);
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Assignments_ClientRequestId"
      ON "Assignments" ("ClientRequestId");
    """);
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Assignments_PositionId"
      ON "Assignments" ("PositionId") WHERE "Status" <> 'declined';
    """);
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE INDEX IF NOT EXISTS "IX_Assignments_VolunteerId"
      ON "Assignments" ("VolunteerId");
    """);
```

For a production system, use EF Core migrations instead. These statements exist because this tutorial already shipped an `EnsureCreated` database and should not require readers to delete it.

Seed the positions only when none exist:

```csharp
if (!await dbContext.MinistryPositions.AnyAsync())
{
  dbContext.MinistryPositions.AddRange(
      CreatePosition(
          "sunday-welcome-host",
          "sunday-2026-11-01",
          "welcome",
          "Welcome host",
          "2026-11-01T09:30:00-05:00",
          "2026-11-01T11:30:00-05:00"),
      CreatePosition(
          "sunday-worship-vocal",
          "sunday-2026-11-01",
          "worship",
          "Vocalist",
          "2026-11-01T09:00:00-05:00",
          "2026-11-01T11:30:00-05:00"),
      CreatePosition(
          "rehearsal-sound",
          "rehearsal-2026-11-05",
          "technical",
          "Sound operator",
          "2026-11-05T18:45:00-05:00",
          "2026-11-05T20:30:00-05:00"),
      CreatePosition(
          "rehearsal-keys",
          "rehearsal-2026-11-05",
          "worship",
          "Keys player",
          "2026-11-05T18:45:00-05:00",
          "2026-11-05T20:30:00-05:00"));
}

static MinistryPosition CreatePosition(
    string id,
    string serviceEventId,
    string ministry,
    string roleName,
    string startsAt,
    string endsAt) => new()
    {
      Id = id,
      ServiceEventId = serviceEventId,
      Ministry = ministry,
      RoleName = roleName,
      StartsAt = DateTimeOffset.Parse(startsAt).UtcDateTime,
      EndsAt = DateTimeOffset.Parse(endsAt).UtcDateTime,
    };
```

## Return the Assignment Board

Add a protected endpoint under the existing service-event route group:

```csharp
group.MapGet("/assignment-board", async (
    string eventId,
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
  var board = await BuildBoardAsync(dbContext, eventId, cancellationToken);
  return board is null ? Results.NotFound() : Results.Ok(board);
});
```

The query projects persistence entities into the API contract:

```csharp
private static async Task<AssignmentBoardResponse?> BuildBoardAsync(
    ChurchServeDbContext dbContext,
    string eventId,
    CancellationToken cancellationToken)
{
  var serviceEvent = await dbContext.ServiceEvents
      .AsNoTracking()
      .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken);
  if (serviceEvent is null)
  {
    return null;
  }

  var positions = await dbContext.MinistryPositions
      .AsNoTracking()
      .Where(item => item.ServiceEventId == eventId)
      .OrderBy(item => item.StartsAt)
      .ThenBy(item => item.RoleName)
      .Select(item => new MinistryPositionResponse(
          item.Id,
          item.ServiceEventId,
          item.Ministry,
          item.RoleName,
          AsUtcOffset(item.StartsAt),
          AsUtcOffset(item.EndsAt)))
      .ToArrayAsync(cancellationToken);

  var positionIds = positions.Select(item => item.Id).ToArray();
  var assignments = await dbContext.Assignments
      .AsNoTracking()
      .Where(item => positionIds.Contains(item.PositionId) && item.Status != "declined")
      .OrderBy(item => item.PositionId)
      .Select(item => new AssignmentResponse(
          item.Id,
          item.PositionId,
          item.VolunteerId,
          item.Volunteer.DisplayName,
          item.Status))
      .ToArrayAsync(cancellationToken);

  var ministries = positions.Select(item => item.Ministry).Distinct().ToArray();
  var volunteers = await dbContext.Volunteers
      .AsNoTracking()
      .Where(item => item.EligibleMinistries.Any(value => ministries.Contains(value.Ministry)))
      .OrderBy(item => item.DisplayName)
      .Select(item => new VolunteerResponse(
          item.Id,
          item.DisplayName,
          item.EligibleMinistries
              .OrderBy(value => value.Ministry)
              .Select(value => value.Ministry)
              .ToArray()))
      .ToArrayAsync(cancellationToken);

  return new AssignmentBoardResponse(
      serviceEvent.Id,
      serviceEvent.Version,
      serviceEvent.AssignedPositions,
      positions,
      volunteers,
      assignments);
}

private static DateTimeOffset AsUtcOffset(DateTime value) =>
    new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
```

Only volunteers eligible for at least one position ministry are returned. The server remains authoritative; the Angular client will filter that smaller set per row for presentation.

Finally, map `MapAssignmentEndpoints()` inside the already authenticated API group in `Program.cs`.

## Verify the Read Side

An integration test should assert the complete shape, not merely `200 OK`:

```csharp
[Fact]
public async Task Board_returns_positions_eligible_volunteers_and_current_version()
{
  using var factory = new ChurchServeApiFactory();
  using var client = await factory.CreateAuthenticatedClientAsync();

  var response = await client.GetAsync(
      "/api/service-events/sunday-2026-11-01/assignment-board");
  var board = await response.Content.ReadFromJsonAsync<AssignmentBoardResponse>();

  Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  Assert.NotNull(board);
  Assert.Equal(1, board.EventVersion);
  Assert.Equal(7, board.AssignedPositions);
  Assert.Equal(2, board.Positions.Count);
  Assert.Contains(board.Positions, item => item.Id == "sunday-welcome-host");
  Assert.Contains(board.Volunteers, item => item.Id == "alex-morgan");
  Assert.Empty(board.Assignments);
}
```

Also cover unknown events and anonymous requests. The board contains scheduling and volunteer data, so it must follow the same authentication boundary as the rest of the coordinator API.

## Current Limitation

The create-service form still creates only the event. It does not create ministry positions, so a newly created event correctly shows “No assignable positions are configured.” The sample events work because the initializer seeds their positions. A future position-management workflow must add, edit, and remove positions and keep `RequiredPositions` consistent; silently manufacturing generic positions during event creation would lose ministry, role, and time information.

Part 18-2 will add the assignment command and defend it against retries and concurrent coordinators.
