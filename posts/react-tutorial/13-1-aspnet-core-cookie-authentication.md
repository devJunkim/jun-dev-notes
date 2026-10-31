---
title: "ChurchServe React Tutorial Part 13-1: ASP.NET Core Cookie Authentication"
excerpt: "Protect a React application's ASP.NET Core API with encrypted cookie sessions, a development-only local account, antiforgery tokens, and authenticated integration tests."
category: "React Tutorial"

seo:
  focusKeyword: "ASP.NET Core cookie authentication React antiforgery"
  description: "Add ASP.NET Core cookie authentication and antiforgery protection for a React API with secure session endpoints and integration tests."
  socialTitle: "ASP.NET Core Cookie Authentication for a React API"
  socialDescription: "Build a server-enforced cookie session with antiforgery protection, safe local credentials, protected endpoints, and real HTTP tests."
---

# ChurchServe React Tutorial Part 13-1: ASP.NET Core Cookie Authentication

[Part 12](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-12-asp-net-core-api-integration-tests/) established an automated HTTP boundary around the ASP.NET Core API. Those tests proved routing, JSON, validation, migrations, and SQLite persistence, but every caller could still read and change the schedule without establishing an identity.

Part 13 is large enough to separate by trust boundary. Part 13-1 secures the server with an encrypted cookie session, a development-only local account, explicit session endpoints, antiforgery protection, and authenticated integration tests. Part 13-2 will make React restore and present that session without treating browser state as authorization.

> **Quick answer:** Let ASP.NET Core authenticate every protected request from an encrypted `HttpOnly` cookie. Keep tutorial credentials in local secret configuration, expose only a small current-user DTO, and require an antiforgery cookie/header pair for every cookie-authenticated mutation. Protect the API group on the server and test the real anonymous, sign-in, authenticated, and sign-out transitions through `WebApplicationFactory`.

## The boundary we are adding

The server flow is:

```text
GET /api/session
  → issue an antiforgery request token
  → return 401 when anonymous
  → return the current profile when authenticated

POST /api/session/sign-in
  → validate the anonymous antiforgery token
  → validate local development credentials
  → issue an encrypted HttpOnly session cookie
  → issue a new authenticated antiforgery token

GET /api/service-events
  → authenticate the cookie before the endpoint runs

DELETE /api/session
  → require authentication and antiforgery validation
  → invalidate the session cookie
```

The new server files are:

```text
server/ChurchServe.Api/
  Authentication/
    AntiforgeryEndpointExtensions.cs
    LocalAccountAuthenticator.cs
    LocalAccountOptions.cs
    SessionContracts.cs
    SessionEndpoints.cs
  appsettings.Development.json
```

The implementation intentionally has one local account for Development and Testing. It demonstrates session mechanics without pretending to be a production identity system.

## Why use a server-managed cookie

The browser needs a durable credential for subsequent API calls. ChurchServe uses ASP.NET Core cookie authentication rather than placing a bearer token in React state or Web Storage.

The authentication cookie is:

- encrypted and validated by ASP.NET Core Data Protection;
- `HttpOnly`, so application JavaScript cannot read it;
- `SameSite=Strict` for this same-origin application;
- secure-only outside local Development and Testing;
- a browser-session cookie rather than a persistent remember-me cookie;
- limited to an eight-hour server ticket with sliding renewal.

React will receive only an ID, email address, display name, and roles. It never needs the authentication ticket.

Cookie authentication is not automatically simpler than bearer authentication. It fits ChurchServe's planned same-origin hosting model, but it creates a cross-site request forgery risk for mutations because browsers attach cookies automatically. The antiforgery design later in this article is therefore part of authentication, not optional polish.

ASP.NET Core's [cookie authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) describes the middleware and Data Protection boundary.

## Configure cookie authentication

Register the cookie handler in `Program.cs`:

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ChurchServe.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment() ||
            builder.Environment.EnvironmentName == "Testing"
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
```

Local development currently uses HTTP, so `SameAsRequest` permits its cookie. Any environment other than Development or Testing requires HTTPS for the authentication cookie.

The tutorial configures redirect events explicitly:

```csharp
options.Events.OnRedirectToLogin = context =>
{
    context.Response.Headers.Location = context.RedirectUri;
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    return Task.CompletedTask;
};

options.Events.OnRedirectToAccessDenied = context =>
{
    context.Response.Headers.Location = context.RedirectUri;
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    return Task.CompletedTask;
};
```

ASP.NET Core 10 already returns `401` and `403` for endpoints it recognizes as APIs instead of redirecting them to HTML login pages. Explicit events keep the `/api` contract stable even if a future Minimal API handler lacks the metadata used for automatic detection. Microsoft's [.NET 10 API endpoint authentication documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0) explains that behavior.

Add middleware before mapping protected endpoints:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapSessionEndpoints();

var serviceEvents = app.MapGroup("/api/service-events")
    .RequireAuthorization();
```

Authentication reconstructs `HttpContext.User`. Authorization prevents an anonymous request from reaching a schedule handler. Hiding React navigation later improves the experience but cannot replace this server check.

## Configure a development-only account

The local account configuration has no behavior of its own:

```csharp
public sealed class LocalAccountOptions
{
    public const string SectionName = "Authentication:LocalAccount";

    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
```

`appsettings.Development.json` contains only non-secret metadata:

```json
{
  "Authentication": {
    "LocalAccount": {
      "Id": "coordinator-1",
      "Email": "coordinator@churchserve.local",
      "DisplayName": "Demo Coordinator",
      "Role": "scheduler"
    }
  }
}
```

Enable user-secrets in the API project and set the password locally:

```bash
dotnet user-secrets set \
  --project server/ChurchServe.Api \
  "Authentication:LocalAccount:Password" \
  "<choose-a-local-password>"
```

The equivalent environment variable is `Authentication__LocalAccount__Password`. Do not commit the password to `appsettings.Development.json`, a launch profile, a Markdown article, or a test snapshot.

Register the options and authenticator:

```csharp
builder.Services.Configure<LocalAccountOptions>(
    builder.Configuration.GetSection(LocalAccountOptions.SectionName));
builder.Services.AddSingleton<LocalAccountAuthenticator>();
```

## Refuse to enable the local account in production

The authenticator checks both environment and configuration before creating an account:

```csharp
if ((!environment.IsDevelopment() &&
     environment.EnvironmentName != "Testing") ||
    string.IsNullOrWhiteSpace(configured.Id) ||
    string.IsNullOrWhiteSpace(configured.Email) ||
    string.IsNullOrWhiteSpace(configured.DisplayName) ||
    string.IsNullOrWhiteSpace(configured.Password) ||
    string.IsNullOrWhiteSpace(configured.Role))
{
    return;
}
```

It normalizes the email and hashes the configured password in memory:

```csharp
var account = new LocalAccount(
    configured.Id.Trim(),
    configured.Email.Trim().ToLowerInvariant(),
    configured.DisplayName.Trim(),
    string.Empty,
    configured.Role.Trim());

_account = account with
{
    PasswordHash = _passwordHasher.HashPassword(
        account,
        configured.Password),
};
```

Authentication verifies both inputs without placing the password or hash into claims:

```csharp
var passwordResult = _passwordHasher.VerifyHashedPassword(
    _account,
    _account.PasswordHash,
    password);
var emailMatches = string.Equals(
    _account.Email,
    email.Trim(),
    StringComparison.OrdinalIgnoreCase);

return emailMatches &&
       passwordResult != PasswordVerificationResult.Failed
    ? _account
    : null;
```

Hashing here avoids retaining the configured clear-text value inside the account record. It does not turn one configuration account into a deployable identity store. Production still needs a supported identity provider or ASP.NET Core Identity with credential recovery, lockout, multifactor authentication, audit, rate limiting, and account lifecycle policies.

## Keep request and response contracts separate

The sign-in request accepts nullable fields because the endpoint must handle malformed input safely:

```csharp
public sealed record SignInRequest(string? Email, string? Password);
```

The response cannot contain a password:

```csharp
public sealed record SessionUserResponse(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);
```

The three endpoints are:

```text
GET    /api/session
POST   /api/session/sign-in
DELETE /api/session
```

The current-session read is public because anonymous React startup must determine that no session exists. Sign-out requires authentication. Sign-in and sign-out both require antiforgery validation.

## Issue a JavaScript-readable request token

Register antiforgery services with an explicit header contract:

```csharp
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "ChurchServe.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy =
        builder.Environment.IsDevelopment() ||
        builder.Environment.EnvironmentName == "Testing"
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
});
```

ASP.NET Core's antiforgery cookie remains `HttpOnly`. React also needs the paired request token so it can send the configured header:

```csharp
private static void IssueAntiforgeryToken(
    HttpContext context,
    IAntiforgery antiforgery)
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append(
        "XSRF-TOKEN",
        tokens.RequestToken!,
        new CookieOptions
        {
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
}
```

`XSRF-TOKEN` is not the authentication cookie. Reading it does not reveal the session ticket. An attacker must still provide a valid token that corresponds to the protected antiforgery cookie and the current identity.

Microsoft's [antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) documents the same cookie-and-header pattern for JavaScript applications.

## Validate mutations with an endpoint filter

Create one reusable filter:

```csharp
public static RouteHandlerBuilder ValidateAntiforgery(
    this RouteHandlerBuilder builder) =>
    builder.AddEndpointFilter(async (context, next) =>
    {
        var antiforgery = context.HttpContext.RequestServices
            .GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new
            {
                message = "The antiforgery token was invalid.",
            });
        }

        return await next(context);
    });
```

Apply it to session mutations and schedule creation:

```csharp
group.MapPost("/sign-in", SignInAsync)
    .ValidateAntiforgery();

group.MapDelete("", SignOutAsync)
    .RequireAuthorization()
    .ValidateAntiforgery();

serviceEvents.MapPost("", async (
    CreateServiceEventRequest request,
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
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
    };

    dbContext.ServiceEvents.Add(serviceEvent);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Created(
        $"/api/service-events/{serviceEvent.Id}",
        ToResponse(serviceEvent));
})
.ValidateAntiforgery();
```

A missing or mismatched token returns `400` before credentials are checked or the database changes.

## Restore an existing session

The GET endpoint always issues a request token:

```csharp
private static IResult GetSession(
    HttpContext context,
    IAntiforgery antiforgery)
{
    IssueAntiforgeryToken(context, antiforgery);

    return context.User.Identity?.IsAuthenticated == true
        ? Results.Ok(ToResponse(context.User))
        : Results.Unauthorized();
}
```

`401` is a normal anonymous result for this endpoint. A server failure is different; React Part 13-2 will preserve that distinction instead of labeling every failed restore as signed out.

## Establish the cookie session

After credential verification, create a minimal claims identity:

```csharp
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, account.Id),
    new Claim(ClaimTypes.Name, account.DisplayName),
    new Claim(ClaimTypes.Email, account.Email),
    new Claim(ClaimTypes.Role, account.Role),
};
var principal = new ClaimsPrincipal(
    new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme));
```

Sign in and issue a token for the new identity:

```csharp
await context.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    principal,
    new AuthenticationProperties { IsPersistent = false });
context.User = principal;
IssueAntiforgeryToken(context, antiforgery);

return Results.Ok(ToResponse(principal));
```

`IsPersistent = false` creates a browser-session cookie. The server ticket can still expire, and closing one tab does not end the browser session.

## Invalidate the server session

Sign-out removes the real credential before reporting success:

```csharp
await context.SignOutAsync(
    CookieAuthenticationDefaults.AuthenticationScheme);
context.User = new ClaimsPrincipal(new ClaimsIdentity());
IssueAntiforgeryToken(context, antiforgery);

return Results.NoContent();
```

Clearing React state alone would be cosmetic. The encrypted cookie would remain usable until expiration.

## Authenticate integration tests through the real flow

The test factory supplies a known account only in its Testing host:

```csharp
services.PostConfigure<LocalAccountOptions>(options =>
{
    options.Id = "test-coordinator";
    options.Email = AuthenticationTestExtensions.Email;
    options.DisplayName = "Test Coordinator";
    options.Password = AuthenticationTestExtensions.Password;
    options.Role = "scheduler";
});
```

The helper performs the browser sequence rather than bypassing authentication:

```csharp
var sessionResponse = await client.GetAsync(
    "/api/session",
    cancellationToken);
ApplyAntiforgeryToken(client, ReadAntiforgeryToken(sessionResponse));

var signInResponse = await client.PostAsJsonAsync(
    "/api/session/sign-in",
    new { Email, Password },
    cancellationToken);
signInResponse.EnsureSuccessStatusCode();
ApplyAntiforgeryToken(client, ReadAntiforgeryToken(signInResponse));
```

The factory client retains cookies, while the helper reads the `XSRF-TOKEN` value from `Set-Cookie` and sends it as `X-XSRF-TOKEN`.

Session tests verify:

- anonymous restore returns `401` and issues a request token;
- the protected schedule rejects anonymous access;
- sign-in without antiforgery returns `400`;
- incorrect credentials return `401`;
- successful sign-in restores the expected profile and roles;
- authenticated schedule access returns `200`;
- sign-out returns `204` and later protected access returns `401`.

Existing service-event tests call the same sign-in helper before exercising their original behavior. The suite did not weaken earlier coverage to accommodate the new security boundary.

## Run the server checkpoint

Configure a local password, then run the API:

```bash
dotnet user-secrets set \
  --project server/ChurchServe.Api \
  "Authentication:LocalAccount:Password" \
  "<choose-a-local-password>"

dotnet run --project server/ChurchServe.Api
```

Run server validation:

```bash
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj \
  --configuration Release
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The finalized checkpoint builds with zero warnings and errors, passes nine API integration tests, and reports no EF Core model drift.

A live local smoke test also verified this status sequence:

```text
anonymous session       401
antiforgery sign-in     200
authenticated schedule 200
sign-out                204
schedule after sign-out 401
```

## Production boundaries

This part establishes an authentication mechanism, not production identity readiness.

- Replace the local account with ASP.NET Core Identity or a suitable external identity provider.
- Add credential lockout, recovery, multifactor authentication, audit events, and rate limits.
- Persist and protect Data Protection keys across restarts and share them intentionally across instances.
- Terminate HTTPS correctly and configure forwarded headers before relying on `Request.IsHttps` behind a proxy.
- Keep the React application and API on the intended same origin or redesign cookie, CORS, and CSRF policies together.
- Choose session duration and renewal based on actual risk rather than tutorial convenience.
- Authentication identifies a caller; later authorization policies must decide what that identity may do.
- Never log passwords, authentication cookies, antiforgery tokens, or full sign-in bodies.

## What Part 13-1 established

The server now owns a real identity boundary:

- ASP.NET Core issues encrypted, `HttpOnly` session cookies.
- the local tutorial account is limited to Development and Testing;
- secret credentials stay outside source control;
- React receives only a bounded current-user profile;
- session restore distinguishes anonymous callers with `401`;
- server endpoint groups enforce authentication;
- antiforgery protects cookie-authenticated mutations;
- sign-out invalidates the server credential;
- integration tests exercise the actual cookie and token exchange.

Part 13-2 connects React to this contract with explicit session states, runtime response validation, XSRF-aware mutations, and accessible sign-in and sign-out behavior.
