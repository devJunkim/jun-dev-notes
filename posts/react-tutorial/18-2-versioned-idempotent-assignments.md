---
title: "ChurchServe React Tutorial Part 18-2: Versioned and Idempotent Assignments"
excerpt: "Implement a safe assignment command with antiforgery, idempotency keys, optimistic concurrency, eligibility rules, and conflict recovery."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core optimistic concurrency idempotency"
  description: "Build an ASP.NET Core assignment command with idempotency keys, event versions, SQLite transactions, conflict snapshots, and integration tests."
  socialTitle: "ChurchServe React Part 18-2: Safe Assignment Commands"
  socialDescription: "Handle retries and competing coordinators with antiforgery, idempotency, event versions, database constraints, and authoritative conflicts."
---

# ChurchServe React Tutorial Part 18-2: Versioned and Idempotent Assignments

Part 18-1 created a persisted assignment-board snapshot. Reading is straightforward; writing introduces distributed-system behavior. A coordinator can double-click, a browser can retry after losing a response, or another coordinator can fill the same position from an older screen.

> **Quick answer:** Send an expected event version and a unique client request ID with every assignment intent. Validate antiforgery and business rules on the server, check an existing idempotency record before checking staleness, write inside a serializable transaction, increment the event version with the assignment, and return the current board in every `409 Conflict` response so the client can reconcile immediately.

## Give each coordination value one job

The request contract is small:

```csharp
public sealed record AssignVolunteerRequest(
    string? PositionId,
    string? VolunteerId,
    int ExpectedEventVersion,
    string? ClientRequestId);
```

`ExpectedEventVersion` answers: “Was the schedule unchanged since this screen loaded?”

`ClientRequestId` answers: “Have I already processed this exact user intent?”

They are not interchangeable. A retry of an accepted request carries an old version but should still return the original success. A different request from an old screen should conflict.

## Validate transport requirements first

Reject missing identifiers and invalid versions before opening the transaction:

```csharp
private static Dictionary<string, string[]> Validate(
    AssignVolunteerRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.PositionId))
        errors["positionId"] = ["A position is required."];
    if (string.IsNullOrWhiteSpace(request.VolunteerId))
        errors["volunteerId"] = ["A volunteer is required."];
    if (request.ExpectedEventVersion < 1)
        errors["expectedEventVersion"] =
            ["The expected event version must be positive."];
    if (string.IsNullOrWhiteSpace(request.ClientRequestId))
        errors["clientRequestId"] = ["A client request ID is required."];
    return errors;
}
```

Map the write route inside the authenticated group and apply the existing antiforgery endpoint filter:

```csharp
group.MapPost("/{eventId}/assignments", async (...) =>
{
    // command handling
}).ValidateAntiforgery();
```

Cookie authentication identifies the coordinator. Antiforgery prevents another origin from using that ambient cookie to submit a command. The [ASP.NET Core antiforgery documentation](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) explains the token defense and middleware requirements. Neither mechanism replaces authorization and business validation.

## Serialize the read-check-write sequence

Begin a serializable transaction before reading command state:

```csharp
await using var transaction =
    await dbContext.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        cancellationToken);
```

The transaction groups idempotency lookup, version comparison, eligibility checks, overlap checks, assignment insertion, aggregate update, and version increment. Application checks still coexist with unique database indexes because two requests can pass a check before either writes.

## Check idempotency before staleness

Look up the client request ID before comparing event versions:

```csharp
var existingRequest = await dbContext.Assignments
    .Include(item => item.Volunteer)
    .SingleOrDefaultAsync(
        item => item.ClientRequestId == request.ClientRequestId,
        cancellationToken);

if (existingRequest is not null)
{
    if (existingRequest.PositionId != request.PositionId ||
        existingRequest.VolunteerId != request.VolunteerId)
    {
        return await ConflictAsync(
            dbContext,
            eventId,
            "The idempotency key was already used for another assignment.",
            cancellationToken);
    }

    return Results.Ok(new AssignVolunteerResponse(
        ToResponse(existingRequest),
        serviceEvent.Version,
        serviceEvent.AssignedPositions));
}
```

The original response may have been lost after the database committed. Returning `200 OK` with the existing assignment lets the retry converge without incrementing the version or staffing count twice.

Reusing the key for different position or volunteer data is not a retry. It is a client identity error, so the API returns a conflict with current state.

## Reject stale screens with recovery data

Only new request identities reach the version check:

```csharp
if (serviceEvent.Version != request.ExpectedEventVersion)
{
    return await ConflictAsync(
        dbContext,
        eventId,
        "The schedule changed after you loaded it.",
        cancellationToken);
}
```

`ConflictAsync` rebuilds the board and returns it with the explanation:

```csharp
return Results.Json(
    new AssignmentConflictResponse(message, board),
    statusCode: StatusCodes.Status409Conflict);
```

The response is actionable. React does not need a second request before it can replace stale local state.

## Repeat every business rule on the server

The endpoint verifies that:

- the event exists;
- the position belongs to that event;
- the volunteer exists;
- the volunteer is eligible for the position ministry;
- the position has no active assignment;
- the volunteer has no accepted assignment whose position interval overlaps.

Overlap uses the standard half-open interval rule:

```csharp
var overlaps = await dbContext.Assignments
    .Where(item => item.VolunteerId == volunteer.Id &&
        item.Status != "declined")
    .AnyAsync(
        item => item.Position.StartsAt < position.EndsAt &&
            item.Position.EndsAt > position.StartsAt,
        cancellationToken);
```

Adjacent positions are allowed because one can end exactly when another begins. The API owns this rule even though React filters obvious invalid selections locally; browser state may be stale or manipulated.

## Insert and advance the version together

Create the assignment, increment the aggregate, and increment the concurrency token in the same unit of work:

```csharp
var assignment = new Assignment
{
    Id = Guid.NewGuid().ToString("N"),
    PositionId = position.Id,
    VolunteerId = volunteer.Id,
    ClientRequestId = request.ClientRequestId!,
    Status = "accepted",
    Position = position,
    Volunteer = volunteer,
};

dbContext.Assignments.Add(assignment);
serviceEvent.AssignedPositions++;
serviceEvent.Version++;
```

Because `Version` is an EF concurrency token, the generated event update includes the original version. Microsoft documents this comparison and application-managed tokens for SQLite in its [EF Core concurrency guidance](https://learn.microsoft.com/en-us/ef/core/saving/concurrency). Database indexes independently protect unique request IDs and active position occupancy.

Commit on success:

```csharp
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

If a database update loses the race, roll back, clear tracked stale entities, and return a newly queried board:

```csharp
catch (DbUpdateException)
{
    await transaction.RollbackAsync(cancellationToken);
    dbContext.ChangeTracker.Clear();
    return await ConflictAsync(
        dbContext,
        eventId,
        "The schedule changed while the assignment was being saved.",
        cancellationToken);
}
```

The catch covers optimistic concurrency and unique-constraint races. In a larger system, log the original exception with safe structured context before translating the expected race into the public conflict contract.

## Return confirmed state

A new assignment returns `201 Created`:

```csharp
return Results.Created(
    $"/api/service-events/{Uri.EscapeDataString(eventId)}/assignments/{assignment.Id}",
    new AssignVolunteerResponse(
        ToResponse(assignment),
        serviceEvent.Version,
        serviceEvent.AssignedPositions));
```

The response gives React the server-generated assignment ID, display name, accepted status, next event version, and confirmed staffing count. The client should replace its one matching pending item, not reload the entire page on ordinary success.

## Test protocol behavior, not only status codes

The integration suite covers the real migrated SQLite database, authentication cookie, and antiforgery flow.

A success test verifies persistence and the new version:

```csharp
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
Assert.Equal(2, result.EventVersion);
Assert.Equal(8, result.AssignedPositions);
Assert.Equal("anna-brooks", result.Assignment.VolunteerId);
Assert.Single(board.Assignments);
```

An idempotency test submits the same request twice and expects one assignment, one increment, `201` for the first response, and `200` for the retry.

A stale-version test performs one assignment, submits a different command with version `1`, and verifies `409 Conflict` contains board version `2` and the confirmed assignment.

The suite also verifies:

- anonymous reads are rejected;
- a signed-in write without an antiforgery token is rejected;
- an ineligible volunteer receives validation feedback;
- the board remains readable after a successful write.

## Part 18-2 checkpoint

The assignment command now has explicit retry and concurrency semantics:

- one client intent has one durable request ID;
- retries converge on the original assignment;
- changed payloads cannot reuse an idempotency key;
- stale versions return current recovery state;
- eligibility, occupancy, and overlap rules run on the server;
- database constraints defend the final invariants;
- assignment, staffing count, and version commit together;
- cookie writes require antiforgery validation;
- integration tests exercise the real protocol.

Part 18-3 consumes this contract from React and makes a pending assignment feel immediate while remaining honest about confirmation and failure.
