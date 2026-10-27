---
title: "ChurchServe Angular Tutorial Part 18-2: Versioned and Idempotent Assignments"
excerpt: "Implement the volunteer-assignment command with validation, idempotency keys, optimistic concurrency, conflict responses, and integration tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core optimistic concurrency idempotency"
  description: "Build an ASP.NET Core assignment command with SQLite transactions, event versions, idempotency keys, conflict recovery, and tests."
  socialTitle: "ChurchServe Part 18-2: Safe Assignment Commands"
  socialDescription: "Handle retries and competing coordinators with idempotency keys, event versions, and authoritative conflict responses."
---

# ChurchServe Angular Tutorial Part 18-2: Versioned and Idempotent Assignments

Part 18-1 created a persisted assignment board. Reading is straightforward; writing is where distributed-system behavior appears. A coordinator may double-click, a proxy may retry, or two coordinators may act on the same version of the schedule.

> **Quick answer:** Send both an expected event version and a unique client request ID. Execute the command in a serializable transaction, validate eligibility and overlap rules on the server, increment the version with the successful write, and return the current board with `409 Conflict` when the client is stale.

## The Command Contract

The request carries business identifiers plus two coordination values:

```csharp
public sealed record AssignVolunteerRequest(
    string? PositionId,
    string? VolunteerId,
    int ExpectedEventVersion,
    string? ClientRequestId);
```

`ExpectedEventVersion` detects a stale screen. `ClientRequestId` identifies one user intent across retries. They solve different problems and both are necessary.

## Implement the Write Endpoint

Add this `POST` route to `AssignmentEndpoints`:

```csharp
group.MapPost("/assignments", async (
    string eventId,
    AssignVolunteerRequest request,
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
  var errors = Validate(request);
  if (errors.Count > 0)
  {
    return Results.ValidationProblem(errors);
  }

  await using var transaction = await dbContext.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      cancellationToken);

  var serviceEvent = await dbContext.ServiceEvents
      .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken);
  if (serviceEvent is null)
  {
    return Results.NotFound();
  }

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

  if (serviceEvent.Version != request.ExpectedEventVersion)
  {
    return await ConflictAsync(
        dbContext,
        eventId,
        "The schedule changed after you loaded it.",
        cancellationToken);
  }

  var position = await dbContext.MinistryPositions
      .SingleOrDefaultAsync(
          item => item.Id == request.PositionId && item.ServiceEventId == eventId,
          cancellationToken);
  if (position is null)
  {
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
      ["positionId"] = ["The position does not belong to this service event."],
    });
  }

  var volunteer = await dbContext.Volunteers
      .Include(item => item.EligibleMinistries)
      .SingleOrDefaultAsync(item => item.Id == request.VolunteerId, cancellationToken);
  if (volunteer is null)
  {
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
      ["volunteerId"] = ["The volunteer was not found."],
    });
  }

  if (!volunteer.EligibleMinistries.Any(item => item.Ministry == position.Ministry))
  {
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
      ["volunteerId"] = ["The volunteer is not eligible for this ministry."],
    });
  }

  if (await dbContext.Assignments.AnyAsync(
      item => item.PositionId == position.Id && item.Status != "declined",
      cancellationToken))
  {
    return await ConflictAsync(
        dbContext,
        eventId,
        "The position was assigned by another coordinator.",
        cancellationToken);
  }

  var overlaps = await dbContext.Assignments
      .Where(item => item.VolunteerId == volunteer.Id && item.Status != "declined")
      .AnyAsync(
          item => item.Position.StartsAt < position.EndsAt &&
              item.Position.EndsAt > position.StartsAt,
          cancellationToken);
  if (overlaps)
  {
    return await ConflictAsync(
        dbContext,
        eventId,
        "The volunteer already has an overlapping assignment.",
        cancellationToken);
  }

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

  try
  {
    await dbContext.SaveChangesAsync(cancellationToken);
  }
  catch (DbUpdateConcurrencyException)
  {
    await transaction.RollbackAsync(cancellationToken);
    dbContext.ChangeTracker.Clear();
    return await ConflictAsync(
        dbContext,
        eventId,
        "The schedule changed while the assignment was being saved.",
        cancellationToken);
  }

  await transaction.CommitAsync(cancellationToken);

  return Results.Created(
      $"/api/service-events/{Uri.EscapeDataString(eventId)}/assignments/{assignment.Id}",
      new AssignVolunteerResponse(
          ToResponse(assignment),
          serviceEvent.Version,
          serviceEvent.AssignedPositions));
}).ValidateAntiforgery();
```

The idempotency lookup intentionally happens before the version comparison. A retry of an already completed request must return the original success even though that success advanced the event version.

## Validate Required Values

Keep transport validation small and explicit:

```csharp
private static Dictionary<string, string[]> Validate(AssignVolunteerRequest request)
{
  var errors = new Dictionary<string, string[]>();
  if (string.IsNullOrWhiteSpace(request.PositionId))
  {
    errors["positionId"] = ["A position is required."];
  }

  if (string.IsNullOrWhiteSpace(request.VolunteerId))
  {
    errors["volunteerId"] = ["A volunteer is required."];
  }

  if (request.ExpectedEventVersion < 1)
  {
    errors["expectedEventVersion"] = ["The expected event version must be positive."];
  }

  if (string.IsNullOrWhiteSpace(request.ClientRequestId))
  {
    errors["clientRequestId"] = ["A client request ID is required."];
  }

  return errors;
}
```

Eligibility, ownership, and overlap checks remain in the command because they require database state. Client-side filtering is only a convenience and never a security boundary.

## Return Authoritative State on Conflict

A bare `409` forces the client to make a second request before it can recover. Return the latest board directly:

```csharp
private static async Task<IResult> ConflictAsync(
    ChurchServeDbContext dbContext,
    string eventId,
    string message,
    CancellationToken cancellationToken)
{
  var board = await BuildBoardAsync(dbContext, eventId, cancellationToken);
  return Results.Json(
      new AssignmentConflictResponse(message, board!),
      statusCode: StatusCodes.Status409Conflict);
}

private static AssignmentResponse ToResponse(Assignment assignment) => new(
    assignment.Id,
    assignment.PositionId,
    assignment.VolunteerId,
    assignment.Volunteer.DisplayName,
    assignment.Status);
```

The Angular store can discard its optimistic guess and replace it with this authoritative snapshot in one operation.

## Why the Transaction and Constraints Both Matter

The serializable transaction keeps the read-check-write sequence together. The EF concurrency token detects a changed event version. The unique indexes prevent duplicate request IDs and multiple active assignments per position.

Those mechanisms overlap intentionally. Application checks produce useful responses; constraints still protect data if two requests pass a check at nearly the same time. SQLite serializes writers aggressively, while SQL Server has different locking behavior, so keeping explicit invariants matters when the production provider changes.

## Test Success, Retry, and Conflict Paths

The success test verifies both the response and the persisted board:

```csharp
[Fact]
public async Task Assign_creates_an_assignment_and_advances_the_event_version()
{
  using var factory = new ChurchServeApiFactory();
  using var client = await factory.CreateAuthenticatedClientAsync();

  var response = await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments",
      ValidRequest());
  var result = await response.Content.ReadFromJsonAsync<AssignVolunteerResponse>();
  var board = await client.GetFromJsonAsync<AssignmentBoardResponse>(
      "/api/service-events/rehearsal-2026-11-05/assignment-board");

  Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  Assert.NotNull(result);
  Assert.Equal("rehearsal-sound", result.Assignment.PositionId);
  Assert.Equal("alex-morgan", result.Assignment.VolunteerId);
  Assert.Equal(2, result.EventVersion);
  Assert.Contains(result.Assignment, board!.Assignments);
  Assert.Equal(2, board.EventVersion);
}
```

The retry test sends exactly the same request twice:

```csharp
[Fact]
public async Task Assign_is_idempotent_for_a_repeated_client_request()
{
  using var factory = new ChurchServeApiFactory();
  using var client = await factory.CreateAuthenticatedClientAsync();
  var request = ValidRequest();

  var first = await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments", request);
  var second = await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments", request);
  var board = await client.GetFromJsonAsync<AssignmentBoardResponse>(
      "/api/service-events/rehearsal-2026-11-05/assignment-board");

  Assert.Equal(HttpStatusCode.Created, first.StatusCode);
  Assert.Equal(HttpStatusCode.OK, second.StatusCode);
  Assert.Single(board!.Assignments);
  Assert.Equal(2, board.EventVersion);
}
```

And the stale-version test proves that the response contains recovery data:

```csharp
[Fact]
public async Task Assign_returns_authoritative_state_for_a_stale_version()
{
  using var factory = new ChurchServeApiFactory();
  using var client = await factory.CreateAuthenticatedClientAsync();
  await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments",
      ValidRequest());

  var response = await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments",
      new AssignVolunteerRequest("rehearsal-keys", "maria-santos", 1, "request-2"));
  var conflict = await response.Content.ReadFromJsonAsync<AssignmentConflictResponse>();

  Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  Assert.NotNull(conflict);
  Assert.Equal(2, conflict.Board.EventVersion);
  Assert.Single(conflict.Board.Assignments);
}
```

The suite should also cover authentication, antiforgery, missing values, an unknown or cross-event position, an unknown or ineligible volunteer, an occupied position, an overlapping assignment, and reuse of an idempotency key for a different command.

Part 18-3 will consume this protocol and make the assignment appear immediately without lying about whether the server confirmed it.
