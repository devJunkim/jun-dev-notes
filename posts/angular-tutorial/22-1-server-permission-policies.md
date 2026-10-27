---
title: "ChurchServe Angular Tutorial Part 22-1: Enforce API Permission Policies"
excerpt: "Separate authentication from authorization with capability claims, ASP.NET Core policies, protected Minimal API endpoints, and denial tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "ASP.NET Core permission policies"
  description: "Build ASP.NET Core permission policies, place capabilities in cookie sessions, protect Minimal API endpoints, and test forbidden operations."
  socialTitle: "ChurchServe Part 22-1: API Permission Policies"
  socialDescription: "Move ChurchServe authorization to enforceable ASP.NET Core capability policies instead of trusting the Angular UI."
---

# ChurchServe Angular Tutorial Part 22-1: Enforce API Permission Policies

Part 21 taught ChurchServe to distinguish a forbidden response from other failures, but every authenticated person still received scheduler access. Hiding an Angular button cannot fix that problem: a user can call the API without using the application screen.

Part 22-1 creates the real authorization boundary on the server. Roles become bundles of explicit capabilities, the authenticated cookie carries those capabilities as claims, and every API operation declares the permission it requires.

> **Quick answer:** Map trusted roles to permission claims during sign-in, register one ASP.NET Core policy per permission, and apply those policies to Minimal API endpoints with `RequireAuthorization`. Test both allowed reads and denied writes through the real authentication flow.

## What Part 22-1 Changes

```text
server/ChurchServe.Api/
  Authentication/
    ChurchServePermissions.cs        # new
    SessionContracts.cs
    SessionEndpoints.cs
  Assignments/AssignmentEndpoints.cs
  ServiceEvents/ServiceEventEndpoints.cs
  Volunteers/VolunteerEndpoints.cs
  Program.cs
tests/ChurchServe.Api.Tests/
  AssignmentEndpointsTests.cs
  ChurchServeApiFactory.cs
  ServiceEventEndpointsTests.cs
  SessionEndpointsTests.cs
  VolunteerEndpointsTests.cs
```

This part keeps the cookie authentication and antiforgery design introduced earlier. Authorization is an additional decision after authentication, not a replacement for it.

## Define Capabilities Separately from Roles

Create `server/ChurchServe.Api/Authentication/ChurchServePermissions.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ChurchServe.Api.Authentication;

public static class ChurchServePermissions
{
  public const string ClaimType = "churchserve:permission";
  public const string ScheduleRead = "schedule.read";
  public const string ScheduleManage = "schedule.manage";
  public const string VolunteerRead = "volunteer.read";
  public const string VolunteerManage = "volunteer.manage";

  private static readonly string[] SchedulerPermissions =
  [
    ScheduleRead,
    ScheduleManage,
    VolunteerRead,
    VolunteerManage,
  ];

  private static readonly string[] ReaderPermissions =
  [
    ScheduleRead,
    VolunteerRead,
  ];

  public static IReadOnlyList<string> ForRole(string role) =>
      role.Trim().ToLowerInvariant() switch
      {
        "scheduler" => SchedulerPermissions,
        "reader" => ReaderPermissions,
        _ => [],
      };

  public static void Configure(AuthorizationOptions options)
  {
    foreach (var permission in SchedulerPermissions)
    {
      options.AddPolicy(
          permission,
          policy => policy.RequireClaim(ClaimType, permission));
    }
  }
}
```

A role answers “which bundle was assigned?” A permission answers “may this identity perform this operation?” Endpoint code can now require `schedule.manage` without knowing whether that permission came from a scheduler, administrator, or a future ministry-lead role.

Unknown roles map to an empty permission list. This fail-closed default prevents a misspelled or unsupported role from receiving accidental access.

Register the policies in `Program.cs`:

```csharp
builder.Services.AddAuthorization(ChurchServePermissions.Configure);
```

[ASP.NET Core authorization policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0) group requirements under names. Minimal API routes apply those policies declaratively rather than repeating manual claim checks inside handlers.

## Put Effective Permissions in the Session

Extend `SessionUserResponse` so Angular can present the capabilities granted by the current session:

```csharp
public sealed record SessionUserResponse(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
```

During sign-in, create the identity claims and add one claim per permission:

```csharp
var claims = new List<Claim>
{
  new Claim(ClaimTypes.NameIdentifier, account.Id),
  new Claim(ClaimTypes.Name, account.DisplayName),
  new Claim(ClaimTypes.Email, account.Email),
  new Claim(ClaimTypes.Role, account.Role),
};

claims.AddRange(ChurchServePermissions.ForRole(account.Role)
    .Select(permission =>
        new Claim(ChurchServePermissions.ClaimType, permission)));

var principal = new ClaimsPrincipal(
    new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme));
```

Return the effective permission claims from `GET /api/session`:

```csharp
return new SessionUserResponse(
    principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
    principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
    principal.Identity?.Name ?? string.Empty,
    principal.FindAll(ClaimTypes.Role)
        .Select(claim => claim.Value)
        .ToArray(),
    principal.FindAll(ChurchServePermissions.ClaimType)
        .Select(claim => claim.Value)
        .Distinct(StringComparer.Ordinal)
        .Order()
        .ToArray());
```

The client sees what the authenticated principal actually grants instead of reconstructing permissions from role names. That keeps the role-to-capability policy on the trusted side of the application boundary.

Existing cookies do not contain the new permission claims. After moving to Part 22-1, restart the API and sign in again.

## Protect Schedule Operations

Keep authentication on the endpoint group, then add the narrower permission to each operation:

```csharp
app.MapServiceEventEndpoints().RequireAuthorization();
```

```csharp
group.MapGet("", /* list handler */)
    .RequireAuthorization(ChurchServePermissions.ScheduleRead);

group.MapGet("/{id}", /* detail handler */)
    .RequireAuthorization(ChurchServePermissions.ScheduleRead);

group.MapPost("", /* create handler */)
    .ValidateAntiforgery()
    .RequireAuthorization(ChurchServePermissions.ScheduleManage);
```

The list and detail endpoints require read access. Creating an event requires manage access even if the caller knows the route and can construct a valid request body.

## Combine Policies for Assignments

An assignment board combines schedule information with volunteer identity data. Reading it therefore requires both `schedule.read` and `volunteer.read`. Changing it additionally requires `schedule.manage`:

```csharp
group.MapGet("/assignment-board", /* board handler */)
    .RequireAuthorization(
        ChurchServePermissions.ScheduleRead,
        ChurchServePermissions.VolunteerRead);

group.MapPost("/assignments", /* assignment handler */)
    .ValidateAntiforgery()
    .RequireAuthorization(
        ChurchServePermissions.ScheduleManage,
        ChurchServePermissions.VolunteerRead);
```

ASP.NET Core combines the named policies, so every listed requirement must succeed. Volunteer search receives its own requirement:

```csharp
group.MapGet("", /* search handler */)
    .RequireAuthorization(ChurchServePermissions.VolunteerRead);
```

## Keep Authorization and Antiforgery Together

The two checks defend against different threats:

- Authorization prevents a signed-in reader from performing a scheduler operation.
- [Antiforgery validation](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery) prevents another origin from causing an authorized scheduler's browser to submit a mutation.

Keep `ValidateAntiforgery()` on cookie-authenticated writes. A valid antiforgery token does not grant permission, and permission does not prove that the request came from the intended page.

The API also preserves the important status distinction:

- `401 Unauthorized` means no valid authenticated session was supplied.
- `403 Forbidden` means the identity is authenticated but lacks the required capability.

ChurchServe's cookie events return those status codes instead of redirecting API requests to an HTML login page.

## Test the Real Boundary

Allow the integration-test factory to select a role while preserving `scheduler` as its default:

```csharp
public sealed class ChurchServeApiFactory(string role = "scheduler")
    : WebApplicationFactory<Program>
{
  // Test configuration supplies role to the existing local account.
}
```

Test one permitted operation and one denied operation with the same reader cookie:

```csharp
[Fact]
public async Task Reader_can_view_but_cannot_create_service_events()
{
  using var factory = new ChurchServeApiFactory("reader");
  using var client = await factory.CreateAuthenticatedClientAsync();
  var request = new CreateServiceEventRequest(
      "Unauthorized change",
      DateTimeOffset.Parse("2026-12-24T18:00:00-05:00"),
      DateTimeOffset.Parse("2026-12-24T19:30:00-05:00"),
      "Room");

  var listResponse = await client.GetAsync("/api/service-events");
  var createResponse = await client.PostAsJsonAsync(
      "/api/service-events",
      request);

  Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
}
```

Add equivalent coverage for assignment-board reads and assignment writes. Test that an unknown role receives `403` from volunteer search, and verify that the session maps `reader` to exactly `schedule.read` and `volunteer.read`.

These tests are stronger than checking a policy object in isolation. They exercise sign-in, the encrypted cookie, claim creation, endpoint metadata, authorization middleware, and the final HTTP response together.

## Verify Part 22-1

```bash
dotnet test ChurchServe.slnx
```

At this checkpoint, direct calls to protected operations are enforced by the API. Part 22-2 consumes the session permissions in Angular so navigation and controls accurately reflect that server-enforced boundary.
