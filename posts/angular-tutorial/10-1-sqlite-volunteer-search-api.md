---
title: "ChurchServe Angular Tutorial Part 10-1: Persist and Search Volunteers with SQLite"
excerpt: "Extend the ChurchServe API with normalized volunteer data, SQLite seeding, a cancellable search endpoint, and integration tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core SQLite search API"
  description: "Build an ASP.NET Core volunteer search API with EF Core, normalized SQLite data, safe LIKE matching, seed data, and integration tests."
  socialTitle: "ChurchServe Part 10-1: SQLite Volunteer Search API"
  socialDescription: "Persist volunteers and ministry eligibility in SQLite, then expose a tested ASP.NET Core search endpoint."
---

# ChurchServe Angular Tutorial Part 10-1: Persist and Search Volunteers with SQLite

[Part 9-2](https://dev.jun-kim.net/?p=325) introduced the ASP.NET Core API and SQLite schedule storage. Before Angular can coordinate volunteer searches, the server needs a queryable volunteer model and a stable HTTP contract.

> **Quick answer:** Store volunteers and ministry eligibility in normalized SQLite tables, project entities into response DTOs, escape SQL `LIKE` wildcard characters, pass request cancellation into EF Core, and verify the endpoint with integration tests.

## What Part 10-1 Adds

```text
server/ChurchServe.Api/
  Data/
    ChurchServeDbContext.cs
    DatabaseInitializer.cs
  Volunteers/
    Volunteer.cs
    VolunteerDirectory.cs
    VolunteerEndpoints.cs
    VolunteerResponse.cs
  Program.cs
tests/ChurchServe.Api.Tests/
  VolunteerEndpointsTests.cs
```

The endpoint is `GET /api/volunteers?query={name}`. Queries shorter than two trimmed characters return an empty array, results are ordered and capped at 20, and the API treats `%` and `_` as literal search text rather than SQL wildcards.

## Model Volunteers and Ministry Eligibility

A volunteer can serve in multiple ministries. A child table avoids comma-separated storage and remains portable when the API later moves from SQLite to Microsoft SQL Server.

```csharp
// server/ChurchServe.Api/Volunteers/Volunteer.cs
namespace ChurchServe.Api.Volunteers;

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

Keep the wire contract separate from the EF Core entity.

```csharp
// server/ChurchServe.Api/Volunteers/VolunteerResponse.cs
namespace ChurchServe.Api.Volunteers;

public sealed record VolunteerResponse(
    string Id,
    string DisplayName,
    IReadOnlyList<string> EligibleMinistries);
```

## Configure the SQLite Tables

Add the sets and mappings to `ChurchServeDbContext`:

```csharp
public DbSet<Volunteer> Volunteers => Set<Volunteer>();
public DbSet<VolunteerMinistry> VolunteerMinistries => Set<VolunteerMinistry>();

var volunteer = modelBuilder.Entity<Volunteer>();
volunteer.ToTable("Volunteers");
volunteer.HasKey(item => item.Id);
volunteer.Property(item => item.Id).HasMaxLength(64);
volunteer.Property(item => item.DisplayName).HasMaxLength(200);

var eligibility = modelBuilder.Entity<VolunteerMinistry>();
eligibility.ToTable("VolunteerMinistries");
eligibility.HasKey(item => new { item.VolunteerId, item.Ministry });
eligibility.Property(item => item.VolunteerId).HasMaxLength(64);
eligibility.Property(item => item.Ministry).HasMaxLength(100);
eligibility.HasOne(item => item.Volunteer)
    .WithMany(item => item.EligibleMinistries)
    .HasForeignKey(item => item.VolunteerId)
    .OnDelete(DeleteBehavior.Cascade);
```

Part 9-2 used `EnsureCreated`. That creates a new database but does not add tables to an existing one. `DatabaseInitializer` therefore executes idempotent SQLite `CREATE TABLE IF NOT EXISTS` statements for these two tables before seeding them. This preserves existing schedule entries.

```csharp
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS "Volunteers" (
      "Id" TEXT NOT NULL CONSTRAINT "PK_Volunteers" PRIMARY KEY,
      "DisplayName" TEXT NOT NULL
    );
    """);

await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS "VolunteerMinistries" (
      "VolunteerId" TEXT NOT NULL,
      "Ministry" TEXT NOT NULL,
      CONSTRAINT "PK_VolunteerMinistries" PRIMARY KEY ("VolunteerId", "Ministry"),
      CONSTRAINT "FK_VolunteerMinistries_Volunteers_VolunteerId"
        FOREIGN KEY ("VolunteerId") REFERENCES "Volunteers" ("Id") ON DELETE CASCADE
    );
    """);
```

Seed volunteers only when the table is empty. The repository contains the complete six-record seed and `CreateVolunteer` helper. These compatibility statements are appropriate for the evolving tutorial database, not a production migration strategy. Before the Microsoft SQL Server transition, replace `EnsureCreated` and the raw compatibility SQL with reviewed EF Core migrations.

## Query Through EF Core

`AsNoTracking` avoids change-tracking overhead for a read-only request. The projection selects only the response fields and sends the request-aborted token to SQLite.

```csharp
// server/ChurchServe.Api/Volunteers/VolunteerDirectory.cs
using ChurchServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChurchServe.Api.Volunteers;

public sealed class VolunteerDirectory(ChurchServeDbContext dbContext)
{
  public async Task<IReadOnlyList<VolunteerResponse>> SearchAsync(
      string query,
      CancellationToken cancellationToken)
  {
    var pattern = $"%{EscapeLikePattern(query)}%";
    return await dbContext.Volunteers
        .AsNoTracking()
        .Where(volunteer => EF.Functions.Like(volunteer.DisplayName, pattern, "\\"))
        .OrderBy(volunteer => volunteer.DisplayName)
        .Take(20)
        .Select(volunteer => new VolunteerResponse(
            volunteer.Id,
            volunteer.DisplayName,
            volunteer.EligibleMinistries
                .OrderBy(eligibility => eligibility.Ministry)
                .Select(eligibility => eligibility.Ministry)
                .ToArray()))
        .ToArrayAsync(cancellationToken);
  }

  private static string EscapeLikePattern(string value) => value
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("%", "\\%", StringComparison.Ordinal)
      .Replace("_", "\\_", StringComparison.Ordinal);
}
```

Without escaping, a query such as `%%` would match every row. Parameterization prevents SQL injection, but escaping is still required to preserve literal substring semantics.

## Map the Endpoint

```csharp
// server/ChurchServe.Api/Volunteers/VolunteerEndpoints.cs
namespace ChurchServe.Api.Volunteers;

public static class VolunteerEndpoints
{
  public static IEndpointRouteBuilder MapVolunteerEndpoints(this IEndpointRouteBuilder endpoints)
  {
    endpoints.MapGet("/api/volunteers", async (
        string? query,
        VolunteerDirectory directory,
        CancellationToken cancellationToken) =>
    {
      var normalizedQuery = query?.Trim() ?? string.Empty;
      return normalizedQuery.Length < 2
          ? Results.Ok(Array.Empty<VolunteerResponse>())
          : Results.Ok(await directory.SearchAsync(normalizedQuery, cancellationToken));
    });

    return endpoints;
  }
}
```

Register the directory as scoped because it depends on the scoped `ChurchServeDbContext`, then map the endpoint:

```csharp
builder.Services.AddScoped<VolunteerDirectory>();

app.MapVolunteerEndpoints();
```

## Test the Contract

`VolunteerEndpointsTests` uses the existing in-memory SQLite API factory. Its tests cover case-insensitive matching, the minimum length, and literal wildcard behavior.

```csharp
[Fact]
public async Task Search_returns_case_insensitive_name_matches()
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var response = await client.GetAsync("/api/volunteers?query=ANN");
  var volunteers = await response.Content.ReadFromJsonAsync<VolunteerResponse[]>();

  Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  Assert.NotNull(volunteers);
  Assert.Equal(["Anna Brooks", "Annette Lee"], volunteers.Select(item => item.DisplayName));
}

[Fact]
public async Task Search_treats_like_wildcards_as_literal_text()
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var volunteers = await client.GetFromJsonAsync<VolunteerResponse[]>(
      "/api/volunteers?query=%25%25");

  Assert.NotNull(volunteers);
  Assert.Empty(volunteers);
}
```

Run the server checks:

```bash
dotnet format ChurchServe.slnx --verify-no-changes
dotnet test ChurchServe.slnx --no-restore
```

## Part 10-1 Checkpoint

ChurchServe now stores volunteers and ministry eligibility in SQLite and exposes a bounded, cancellable search endpoint. Part 10-2 consumes this contract from Angular and solves the latest-query-wins problem with RxJS.
