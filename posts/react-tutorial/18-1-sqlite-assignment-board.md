---
title: "ChurchServe React Tutorial Part 18-1: Build a SQLite Assignment Board"
excerpt: "Persist ministry positions, volunteer eligibility, and assignments with EF Core, then expose one screen-shaped assignment-board response."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core SQLite assignment board"
  description: "Model ministry positions, volunteers, and assignments with EF Core and SQLite, then return a complete assignment-board read model."
  socialTitle: "ChurchServe React Part 18-1: SQLite Assignment Board"
  socialDescription: "Persist staffing positions and volunteer eligibility, migrate existing databases safely, and load a coordinator board in one request."
---

# ChurchServe React Tutorial Part 18-1: Build a SQLite Assignment Board

[Part 17](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-17-feature-architecture-and-lazy-routes/) made schedule an enforceable lazy feature. Part 18 uses that boundary for the first coordinator write workflow: assigning an eligible volunteer to an open ministry position.

The workflow spans enough persistence, concurrency, HTTP, state, and UI behavior to deserve three articles. Part 18-1 builds the persisted read side. Part 18-2 adds a safe assignment command. Part 18-3 implements optimistic React state without confusing a local prediction with server confirmation.

> **Quick answer:** Store ministry positions separately from assignments so an open position exists before a volunteer fills it. Persist volunteer eligibility as relational data, protect relationships with foreign keys and unique indexes, and return the event version, positions, eligible volunteers, and active assignments as one screen-shaped response. Use an EF Core migration with a version default of `1` so both fresh and upgraded databases begin from a valid concurrency baseline.

## What Part 18-1 adds

The API gains these areas:

```text
server/ChurchServe.Api/
  Assignments/
    Assignment.cs
    AssignmentContracts.cs
    AssignmentEndpoints.cs
    MinistryPosition.cs
  Volunteers/
    Volunteer.cs
  Data/
    ChurchServeDbContext.cs
    DatabaseInitializer.cs
    Migrations/*_Part18Assignments.cs
```

The read model supports seeded events. Creating and editing ministry positions is a separate capability; the existing create-service form still creates an event with no configured positions.

## Separate positions from assignments

A ministry position describes work that needs to be filled:

```csharp
public sealed class MinistryPosition
{
    public required string Id { get; init; }
    public required string ServiceEventId { get; init; }
    public required string Ministry { get; init; }
    public required string RoleName { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public ServiceEventEntity ServiceEvent { get; init; } = null!;
    public ICollection<Assignment> Assignments { get; } = [];
}
```

The position owns its actual working interval. A sound operator may arrive before the service starts, so future overlap checks compare positions rather than only their parent event times.

An assignment connects one position to one volunteer:

```csharp
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

`ClientRequestId` becomes the idempotency key in Part 18-2. `Status` leaves room for decline and replacement workflows even though this checkpoint creates accepted assignments only.

## Persist volunteer eligibility

The assignment board needs more than volunteer names. It must know which ministries each person can serve:

```csharp
public sealed class Volunteer
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public ICollection<VolunteerMinistry> EligibleMinistries { get; } = [];
}

public sealed class VolunteerMinistry
{
    public required string VolunteerId { get; init; }
    public required string Ministry { get; init; }
    public Volunteer Volunteer { get; init; } = null!;
}
```

The composite key `(VolunteerId, Ministry)` prevents duplicate eligibility rows. Eligibility remains server data; filtering a select in React improves the interface but never replaces server validation.

## Define one board response

The client could request positions, volunteers, and assignments separately, then join them. That creates multiple loading and failure states and risks combining records from different schedule versions. Return one coherent read model instead:

```csharp
public sealed record AssignmentBoardResponse(
    string EventId,
    int EventVersion,
    int AssignedPositions,
    IReadOnlyList<MinistryPositionResponse> Positions,
    IReadOnlyList<VolunteerResponse> Volunteers,
    IReadOnlyList<AssignmentResponse> Assignments);
```

`EventVersion` is part of the snapshot. A later command sends the version the coordinator actually saw. `AssignedPositions` preserves the event-level staffing summary, while the collections describe the configured rows on this board.

## Map relationships and database invariants

Add sets for volunteers, eligibility, positions, and assignments to `ChurchServeDbContext`. Configure the important assignment constraints:

```csharp
var assignment = modelBuilder.Entity<Assignment>();
assignment.ToTable("Assignments");
assignment.HasKey(item => item.Id);
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

The filtered unique index is the final defense against two active assignments occupying one position. Application checks provide better messages, but only the database can protect the invariant across competing requests.

Mark the event version as a concurrency token and give upgraded rows a valid default:

```csharp
serviceEvent.Property(item => item.Version)
    .HasDefaultValue(1)
    .IsConcurrencyToken();
```

The default matters. An initially generated migration used `0`; that would have made every existing database inconsistent with the API's positive-version contract. Reviewing generated migrations is part of implementing the model, not an optional formatting pass.

## Generate a real EF Core migration

ChurchServe already uses migrations, so Part 18 does not add hand-written `CREATE TABLE` startup statements. Generate the schema change with the repository-local tool:

```bash
dotnet tool restore
dotnet ef migrations add Part18Assignments \
  --project server/ChurchServe.Api \
  --output-dir Data/Migrations
```

The migration:

- adds `ServiceEvents.Version` with default `1`;
- creates `Volunteers` and `VolunteerMinistries`;
- creates `MinistryPositions` and its event foreign key;
- creates `Assignments` and both foreign keys;
- adds unique indexes for request identity and active position occupancy.

Then verify the model and migration still match:

```bash
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The command reports no pending changes at this checkpoint.

## Seed complete sample relationships

`DatabaseInitializer` now seeds each area independently:

```csharp
if (!await dbContext.ServiceEvents.AnyAsync())
{
    dbContext.ServiceEvents.AddRange(/* sample events */);
}

if (!await dbContext.Volunteers.AnyAsync())
{
    dbContext.Volunteers.AddRange(
        CreateVolunteer("alex-morgan", "Alex Morgan", "technical", "worship"),
        CreateVolunteer("anna-brooks", "Anna Brooks", "children", "welcome"));
}

if (!await dbContext.MinistryPositions.AnyAsync())
{
    dbContext.MinistryPositions.AddRange(/* sample positions */);
}

await dbContext.SaveChangesAsync();
```

Do not return merely because events already exist. An upgraded Part 17 database contains events but no Part 18 volunteers or positions. Independent existence checks let the migration preserve existing schedule data and let the initializer add only the new tutorial reference data.

The rehearsal sample now reports four of six positions assigned, matching its two newly seeded open positions. Keeping aggregates consistent prevents a later successful assignment from exceeding `RequiredPositions` and violating the client response validator.

## Query one authoritative board

Map the read route inside the existing authenticated service-event group:

```csharp
group.MapGet("/{eventId}/assignment-board", async (
    string eventId,
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var board = await BuildBoardAsync(
        dbContext,
        eventId,
        cancellationToken);
    return board is null ? Results.NotFound() : Results.Ok(board);
});
```

`BuildBoardAsync` projects each set instead of serializing EF entities:

```csharp
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
        item.StartsAt,
        item.EndsAt))
    .ToArrayAsync(cancellationToken);
```

Assignments are limited to those positions and exclude declined records. Volunteers are limited to people eligible for at least one ministry on the board. React will narrow that smaller set per row, but the response remains useful without exposing an unrelated organization-wide directory.

## Verify the read side

An API integration test signs in through the real cookie and antiforgery flow, requests the board, and asserts the complete shape:

```csharp
var board = await client.GetFromJsonAsync<AssignmentBoardResponse>(
    "/api/service-events/sunday-2026-11-01/assignment-board");

Assert.NotNull(board);
Assert.Equal(1, board.EventVersion);
Assert.Equal(7, board.AssignedPositions);
Assert.Equal(2, board.Positions.Count);
Assert.Contains(board.Positions,
    item => item.Id == "sunday-welcome-host");
Assert.Contains(board.Volunteers,
    item => item.Id == "alex-morgan");
Assert.Empty(board.Assignments);
```

A separate test verifies that an anonymous request receives `401 Unauthorized`. The board contains coordinator scheduling and eligibility data, so it belongs behind the same authenticated boundary as service events.

## Part 18-1 checkpoint

The read side now has a durable foundation:

- positions exist independently from assignments;
- volunteer eligibility is relational and queryable;
- database constraints protect request identity and position occupancy;
- existing databases upgrade with event version `1`;
- initializers seed new reference data without deleting schedules;
- one authenticated request returns a coherent board snapshot;
- integration tests verify shape, version, and access control.

Part 18-2 uses that versioned snapshot to make retries and competing coordinators safe.
