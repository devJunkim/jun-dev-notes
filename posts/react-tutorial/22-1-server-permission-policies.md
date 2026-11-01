---
title: "ChurchServe React Tutorial Part 22-1: Enforce API Permission Policies"
excerpt: "Separate authentication from authorization with capability claims, ASP.NET Core policies, protected Minimal API endpoints, and denial tests."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core permission policies"
  description: "Build ASP.NET Core permission policies, add capabilities to cookie sessions, protect Minimal API endpoints, and test forbidden operations."
  socialTitle: "ChurchServe React Part 22-1: API Permission Policies"
  socialDescription: "Move ChurchServe authorization to enforceable ASP.NET Core policies instead of trusting the React interface."
---

# ChurchServe React Tutorial Part 22-1: Enforce API Permission Policies

[Part 21](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-21-error-handling-and-resilience/) taught ChurchServe to distinguish `403 Forbidden` from other failures, but every authenticated person still received scheduler access. Hiding a React button cannot fix that problem because a caller can bypass the interface and send HTTP requests directly.

> **Quick answer:** Map trusted roles to permission claims during sign-in, register one ASP.NET Core policy per permission, and apply those policies to each Minimal API operation. Prove the boundary by signing in as a reader and calling both permitted reads and forbidden writes.

## Model capabilities separately from roles

Create `ChurchServePermissions.cs`:

```csharp
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
}
```

A role identifies a bundle assigned by the identity system. A permission describes an operation the current principal may perform. Endpoints can require `schedule.manage` without knowing whether it came from a scheduler, administrator, or a future ministry-lead role.

Unknown roles receive no capabilities. That fail-closed default prevents a misspelled or unsupported role from gaining access.

## Register one policy per permission

Add a configuration method:

```csharp
public static void Configure(AuthorizationOptions options)
{
    foreach (var permission in SchedulerPermissions)
    {
        options.AddPolicy(
            permission,
            policy => policy.RequireClaim(ClaimType, permission));
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddAuthorization(ChurchServePermissions.Configure);
```

[ASP.NET Core policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0) groups requirements under named policies. The endpoint declares its required capability while the authorization middleware evaluates the principal.

## Put effective permissions in the cookie principal

After authenticating the local tutorial account, create the identity claims and add one claim per granted permission:

```csharp
var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, account.Id),
    new(ClaimTypes.Name, account.DisplayName),
    new(ClaimTypes.Email, account.Email),
    new(ClaimTypes.Role, account.Role),
};

claims.AddRange(ChurchServePermissions.ForRole(account.Role)
    .Select(permission => new Claim(
        ChurchServePermissions.ClaimType,
        permission)));
```

ASP.NET Core encrypts the authenticated principal into the session cookie. Requests later reconstruct the principal and expose its permission claims to authorization policies.

Existing cookies do not contain these new claims. Restart the API and sign in again after applying Part 22.

## Return the effective permission set

Extend the session response:

```csharp
public sealed record SessionUserResponse(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
```

Build it from the authenticated principal rather than recalculating the role mapping:

```csharp
principal.FindAll(ChurchServePermissions.ClaimType)
    .Select(claim => claim.Value)
    .Distinct(StringComparer.Ordinal)
    .Order()
    .ToArray()
```

React now receives exactly what the current cookie grants. The browser does not need to duplicate the server’s role-to-permission policy.

## Protect schedule reads and writes separately

Keep the authenticated group and add narrower endpoint requirements. The following condensed form names the existing handlers so the authorization metadata stays easy to see:

```csharp
serviceEvents.MapGet("", ListEventsAsync)
    .RequireAuthorization(ChurchServePermissions.ScheduleRead);

serviceEvents.MapGet("/{eventId}", GetEventAsync)
    .RequireAuthorization(ChurchServePermissions.ScheduleRead);

serviceEvents.MapPost("", CreateEventAsync)
    .ValidateAntiforgery()
    .RequireAuthorization(ChurchServePermissions.ScheduleManage);
```

A reader can inspect the schedule but cannot create an event, even with a valid request body and knowledge of the endpoint.

## Combine requirements for assignment data

An assignment board combines schedule information with volunteer identity data. Reading it requires both capabilities; again, the handler name abbreviates the existing inline implementation:

```csharp
group.MapGet("/{eventId}/assignment-board", GetBoardAsync)
    .RequireAuthorization(
        ChurchServePermissions.ScheduleRead,
        ChurchServePermissions.VolunteerRead);
```

Changing an assignment requires management plus access to volunteer data:

```csharp
group.MapPost("/{eventId}/assignments", AssignVolunteerAsync)
    .ValidateAntiforgery()
    .RequireAuthorization(
        ChurchServePermissions.ScheduleManage,
        ChurchServePermissions.VolunteerRead);
```

The named policies are combined, so every requirement must succeed.

ChurchServe does not yet have a standalone volunteer API. Part 22 protects volunteer information where it exists today: inside the assignment-board workflow. It does not invent an endpoint merely to match a future design.

## Keep antiforgery and authorization together

The checks answer different questions:

- authorization asks whether this authenticated principal may perform the operation;
- antiforgery asks whether a cookie-authenticated mutation came through the intended request flow.

Microsoft’s [ASP.NET Core antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) warns against disabling protection for browser-accessible endpoints that rely on cookies. A valid antiforgery token does not grant permission, and a permission claim does not replace request-origin protection.

Preserve the status distinction: unauthenticated requests receive `401`; authenticated principals without the capability receive `403`.

## Test the real authentication path

Allow the integration-test factory to select a role while keeping scheduler as its default:

```csharp
public ChurchServeApiFactory(
    string? databasePath = null,
    string role = "scheduler")
{
    _role = role;
    // Existing isolated database setup remains unchanged.
}
```

Test a reader with the real sign-in cookie:

```csharp
[Fact]
public async Task Reader_can_view_but_cannot_create_service_events()
{
    var factory = new ChurchServeApiFactory(role: "reader");
    using var client = factory.CreateClient();
    await client.SignInAsync(TestContext.Current.CancellationToken);

    var listResponse = await client.GetAsync("/api/service-events");
    var createResponse = await client.PostAsJsonAsync(
        "/api/service-events",
        ValidRequest());

    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
}
```

Equivalent tests prove that a reader may view an assignment board but cannot assign a volunteer. An unsupported role receives `403` from the board. Session tests assert exact permission arrays rather than merely checking that one expected value is present.

These tests exercise authentication, encrypted cookie claims, endpoint metadata, authorization middleware, and final HTTP responses together.

## Result

ChurchServe now enforces capabilities at its trusted boundary. Roles produce permission claims, Minimal API operations declare what they require, cookie-authenticated writes retain antiforgery validation, and negative integration tests prove that hidden UI is not the security control.

Part 22-2 consumes the effective permission set in React so routing and controls accurately reflect the API boundary.
