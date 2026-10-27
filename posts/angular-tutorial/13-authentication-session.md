---
title: "ChurchServe Angular Tutorial Part 13: Authentication and Session State"
excerpt: "Add a server-managed cookie session, antiforgery protection, sign-in UI, session restoration, and authenticated API tests to ChurchServe."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular authentication session tutorial"
  description: "Implement Angular session state with ASP.NET Core cookies, antiforgery protection, explicit transitions, sign-in UI, and integration tests."
  socialTitle: "ChurchServe Part 13: Authentication and Sessions"
  socialDescription: "Add a secure, testable cookie-session experience without treating Angular state as proof of authorization."
---

# ChurchServe Angular Tutorial Part 13: Authentication and Session State

Part 12 extracted reusable presentation components. ChurchServe still treats every visitor as a coordinator, though: the browser can load schedule and volunteer data without establishing an identity.

Part 13 adds a development sign-in experience backed by an ASP.NET Core cookie session. Angular restores the current user before rendering session-dependent navigation, passwords never enter application state, mutating requests carry an antiforgery token, and the server independently protects coordination APIs.

> **Quick answer:** Let ASP.NET Core issue an encrypted `HttpOnly` authentication cookie, expose only a current-user DTO to Angular, model checking and anonymous states separately, and pair cookie authentication with server-validated antiforgery tokens. Angular session state improves the interface; server authorization establishes trust.

## What We Are Building

The Part 13 flow is:

```text
Browser starts
  → GET /api/session
  → server issues XSRF token
  → 200 current user or 401 anonymous
  → Angular finishes session restoration

Sign in
  → Angular POSTs email and password with X-XSRF-TOKEN
  → server validates antiforgery token and credentials
  → server sets encrypted HttpOnly session cookie
  → Angular stores only the returned user profile

Protected request
  → browser sends session cookie automatically
  → ASP.NET Core authenticates before the endpoint runs
```

The main files are:

```text
server/ChurchServe.Api/
  Authentication/
    AntiforgeryEndpointExtensions.cs
    LocalAccountAuthenticator.cs
    LocalAccountOptions.cs
    SessionContracts.cs
    SessionEndpoints.cs
  appsettings.Development.json
  Program.cs
src/app/
  core/auth/
    current-user.ts
    session-dto.ts
    session-store.spec.ts
    session-store.ts
  features/sign-in/
    sign-in.html
    sign-in.scss
    sign-in.spec.ts
    sign-in.ts
  app.config.ts
  app.html
  app.routes.ts
  app.ts
tests/ChurchServe.Api.Tests/
  AuthenticationTestExtensions.cs
  SessionEndpointsTests.cs
```

This tutorial uses one local account only in Development and Testing so the project remains runnable without an external identity provider. That account is not a production identity system.

## Choose a Server-Managed Cookie Session

The browser needs a durable way to prove that the server authenticated it. ChurchServe uses ASP.NET Core cookie authentication rather than storing a bearer token in Angular storage.

An authentication cookie should be:

- `HttpOnly`, so application JavaScript cannot read it;
- `Secure` outside local HTTP development;
- assigned an appropriate `SameSite` policy;
- encrypted and validated by the server;
- short enough to limit exposure, with renewal rules chosen deliberately.

ASP.NET Core's cookie handler uses Data Protection to encrypt authentication tickets. For a multi-instance deployment, all instances need an intentionally shared Data Protection key ring and application name. See Microsoft's [cookie authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) before deploying this design.

Do not put a password or long-lived session token in `localStorage`. Any script that executes in the origin can read that storage. The browser may hold an opaque, `HttpOnly` cookie; Angular holds only display information about the signed-in user.

## Configure Cookies, Authorization, and Antiforgery

Add the authentication services in `Program.cs`:

```csharp
builder.Services.Configure<LocalAccountOptions>(
    builder.Configuration.GetSection(LocalAccountOptions.SectionName));
builder.Services.AddSingleton<LocalAccountAuthenticator>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
      options.Cookie.Name = "ChurchServe.Session";
      options.Cookie.HttpOnly = true;
      options.Cookie.IsEssential = true;
      options.Cookie.SameSite = SameSiteMode.Strict;
      options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ||
          builder.Environment.EnvironmentName == "Testing"
          ? CookieSecurePolicy.SameAsRequest
          : CookieSecurePolicy.Always;
      options.ExpireTimeSpan = TimeSpan.FromHours(8);
      options.SlidingExpiration = true;
      options.Events.OnRedirectToLogin = context =>
      {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
      };
      options.Events.OnRedirectToAccessDenied = context =>
      {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
      };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
  options.HeaderName = "X-XSRF-TOKEN";
  options.Cookie.Name = "ChurchServe.Antiforgery";
  options.Cookie.HttpOnly = true;
  options.Cookie.SameSite = SameSiteMode.Strict;
  options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ||
      builder.Environment.EnvironmentName == "Testing"
      ? CookieSecurePolicy.SameAsRequest
      : CookieSecurePolicy.Always;
});
```

ASP.NET Core 10 returns 401 and 403 for endpoints it recognizes as APIs instead of redirecting them to HTML login pages. ChurchServe also configures the cookie redirect events explicitly. That keeps the JSON API contract stable even when a Minimal API handler's return shape does not receive known-API metadata. Microsoft's [.NET 10 API endpoint authentication documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0) explains the classification behavior.

Register middleware before mapping protected endpoints:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapSessionEndpoints();
app.MapServiceEventEndpoints().RequireAuthorization();
app.MapVolunteerEndpoints().RequireAuthorization();
```

Authentication constructs `HttpContext.User`. Authorization prevents anonymous requests from reaching the schedule and volunteer handlers. Part 14 adds Angular route guards for navigation experience, but those guards do not replace this server boundary.

## Add a Development-Only Local Account

The configuration shape is small:

```csharp
namespace ChurchServe.Api.Authentication;

public sealed class LocalAccountOptions
{
  public const string SectionName = "Authentication:LocalAccount";

  public string Id { get; init; } = string.Empty;
  public string Email { get; init; } = string.Empty;
  public string DisplayName { get; init; } = string.Empty;
  public string Password { get; init; } = string.Empty;
  public string Role { get; init; } = string.Empty;
}
```

`appsettings.Development.json` contains only non-secret account metadata:

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

Configure a password locally with .NET user-secrets instead of committing it:

```bash
dotnet user-secrets set --project server/ChurchServe.Api "Authentication:LocalAccount:Password" "<choose-a-local-password>"
```

An environment variable named `Authentication__LocalAccount__Password` is another option for automated local environments. The authenticator refuses to enable the account outside Development and Testing, and it hashes the configured password before comparisons:

```csharp
public sealed class LocalAccountAuthenticator
{
  private readonly LocalAccount? account;
  private readonly PasswordHasher<LocalAccount> passwordHasher = new();

  public LocalAccountAuthenticator(
      IOptions<LocalAccountOptions> options,
      IHostEnvironment environment)
  {
    var configured = options.Value;
    if ((!environment.IsDevelopment() && environment.EnvironmentName != "Testing") ||
        string.IsNullOrWhiteSpace(configured.Password))
    {
      return;
    }

    var pending = new LocalAccount(
        configured.Id,
        configured.Email.Trim().ToLowerInvariant(),
        configured.DisplayName,
        string.Empty,
        configured.Role);
    account = pending with
    {
      PasswordHash = passwordHasher.HashPassword(pending, configured.Password),
    };
  }

  public LocalAccount? Authenticate(string email, string password)
  {
    if (account is null)
    {
      return null;
    }

    var passwordResult = passwordHasher.VerifyHashedPassword(
        account,
        account.PasswordHash,
        password);
    var emailMatches = string.Equals(
        account.Email,
        email.Trim(),
        StringComparison.OrdinalIgnoreCase);

    return emailMatches && passwordResult != PasswordVerificationResult.Failed
        ? account
        : null;
  }
}
```

The password never goes into source control, SQLite, claims, the cookie, or a response. A deployed system should replace this class with ASP.NET Core Identity or a current external identity-provider integration, including registration, recovery, lockout, audit, and rate-limiting policies.

## Define the Session HTTP Contract

The request and response records are deliberately separate:

```csharp
public sealed record SignInRequest(string? Email, string? Password);

public sealed record SessionUserResponse(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);
```

`SessionUserResponse` cannot accidentally serialize the submitted password or the internal password hash.

The endpoints are:

```text
GET    /api/session          Restore the current user and issue an XSRF token
POST   /api/session/sign-in  Validate credentials and establish the cookie session
DELETE /api/session          Invalidate the cookie session
```

## Issue an Angular-Compatible XSRF Token

Cookie authentication creates a CSRF risk for state-changing requests because browsers attach cookies automatically. Angular `HttpClient` has the client half of an XSRF defense: for relative or same-origin mutating requests, it reads `XSRF-TOKEN` and sends `X-XSRF-TOKEN`.

The server must create and validate that token. Angular's [security guide](https://angular.dev/best-practices/security#httpclient-xsrf-csrf-security) is explicit that client behavior alone is insufficient.

ChurchServe issues the request token as a JavaScript-readable cookie while keeping the antiforgery system's cookie `HttpOnly`:

```csharp
private static void IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
{
  var tokens = antiforgery.GetAndStoreTokens(context);
  context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
  {
    HttpOnly = false,
    IsEssential = true,
    SameSite = SameSiteMode.Strict,
    Secure = context.Request.IsHttps,
    Path = "/",
  });
}
```

The readable cookie is not the authentication cookie. It is a request token that must correspond to the server-managed antiforgery cookie. Microsoft's [antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) documents the same Angular cookie/header convention.

For protected mutation endpoints outside the session group, add a reusable endpoint filter:

```csharp
public static class AntiforgeryEndpointExtensions
{
  public static RouteHandlerBuilder ValidateAntiforgery(this RouteHandlerBuilder builder) =>
      builder.AddEndpointFilter(async (context, next) =>
      {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
          await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
          return Results.BadRequest(new { message = "The antiforgery token was invalid." });
        }

        return await next(context);
      });
}
```

Part 13 applies it to `POST /api/service-events`. Sign-in and sign-out validate explicitly because they also rotate session-related state.

## Restore, Sign In, and Sign Out on the Server

The current-session endpoint issues the XSRF token for both authenticated and anonymous visitors:

```csharp
group.MapGet("/", (HttpContext context, IAntiforgery antiforgery) =>
{
  IssueAntiforgeryToken(context, antiforgery);
  return context.User.Identity?.IsAuthenticated == true
      ? Results.Ok(ToResponse(context.User))
      : Results.Unauthorized();
});
```

Sign-in validates antiforgery and credentials, creates claims, and calls `SignInAsync`:

```csharp
var claims = new[]
{
  new Claim(ClaimTypes.NameIdentifier, account.Id),
  new Claim(ClaimTypes.Name, account.DisplayName),
  new Claim(ClaimTypes.Email, account.Email),
  new Claim(ClaimTypes.Role, account.Role),
};
var principal = new ClaimsPrincipal(
    new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

await context.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    principal,
    new AuthenticationProperties { IsPersistent = false });
context.User = principal;
IssueAntiforgeryToken(context, antiforgery);

return Results.Ok(ToResponse(principal));
```

The cookie is session-based because `IsPersistent` is false. Closing a browser is not a substitute for explicit sign-out, and closing one tab does not close the browser session.

Sign-out validates antiforgery, invalidates the server cookie, switches the current request to an anonymous principal, and issues a fresh anonymous request token:

```csharp
await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
context.User = new ClaimsPrincipal(new ClaimsIdentity());
IssueAntiforgeryToken(context, antiforgery);
return Results.NoContent();
```

Resetting Angular state without calling this endpoint would leave the real server session active.

## Model Session State Explicitly in Angular

Create `current-user.ts`:

```typescript
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly roles: readonly string[];
}

export interface SignInCommand {
  readonly email: string;
  readonly password: string;
}
```

The password exists only in the command sent to the server. It is not part of `CurrentUser` or `SessionState`.

Create the state union in `session-store.ts`:

```typescript
export type SessionState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }
  | { status: 'unavailable'; message: string };
```

`checking` is not the same as `anonymous`. During restoration, the application does not yet know whether the browser has a valid cookie. `unavailable` is also distinct: a network or server failure must not be presented as a confirmed signed-out state.

The store keeps one private writable signal and exposes a read-only view:

```typescript
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly http = inject(HttpClient);
  private readonly state = signal<SessionState>({ status: 'checking' });

  readonly session = this.state.asReadonly();

  restore(): Observable<void> {
    this.state.set({ status: 'checking' });
    return this.http.get<unknown>('/api/session').pipe(
      map(parseCurrentUser),
      tap((user) => this.state.set({ status: 'authenticated', user })),
      map(() => undefined),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.state.set({ status: 'anonymous' });
        } else {
          this.state.set({
            status: 'unavailable',
            message: 'ChurchServe could not verify your session.',
          });
        }

        return of(undefined);
      }),
    );
  }

  signIn(command: SignInCommand): Observable<void> {
    return this.http.post<unknown>('/api/session/sign-in', command).pipe(
      map(parseCurrentUser),
      tap((user) => this.state.set({ status: 'authenticated', user })),
      map(() => undefined),
    );
  }

  signOut(): Observable<void> {
    return this.http.delete<void>('/api/session').pipe(
      tap(() => this.state.set({ status: 'anonymous' })),
      map(() => undefined),
    );
  }
}
```

The store becomes anonymous only after the DELETE succeeds. If sign-out fails, the authenticated state remains honest and the shell reports the failure.

As with earlier HTTP boundaries, `parseCurrentUser` validates the unknown JSON before it enters application state. An HTTP 200 with malformed user data is not treated as a valid session.

## Restore the Session Before Initial Navigation

Register an application initializer in `app.config.ts`:

```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAppInitializer(() => firstValueFrom(inject(SessionStore).restore())),
    provideRouter(routes),
    provideServiceEventApi(),
    provideVolunteerApi(),
  ],
};
```

The initializer waits for restoration to finish. A 401 is handled as a normal anonymous result, and a server failure becomes `unavailable`; neither rejects application bootstrap.

This avoids a flash where authenticated navigation briefly looks signed out. It also prepares Part 14 route guards to make decisions from a resolved session state.

## Build the Sign-In Screen

Add a lazy route:

```typescript
{
  path: 'sign-in',
  loadComponent: () => import('./features/sign-in/sign-in').then((module) => module.SignIn),
  title: 'Sign in | ChurchServe',
},
```

The component uses a reactive form and does not copy the password into a signal or store:

```typescript
protected readonly form = new FormGroup({
  email: new FormControl('coordinator@churchserve.local', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  }),
  password: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
});

protected signIn(): void {
  if (this.form.invalid || this.isSubmitting()) {
    this.form.markAllAsTouched();
    return;
  }

  this.isSubmitting.set(true);
  this.errorMessage.set('');
  this.sessionStore.signIn(this.form.getRawValue()).subscribe({
    next: () => {
      this.form.controls.password.reset();
      void this.router.navigateByUrl('/schedule');
    },
    error: (error: unknown) => {
      this.isSubmitting.set(false);
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 401
          ? 'The email or password was incorrect.'
          : 'Sign-in is unavailable. Try again.',
      );
    },
  });
}
```

The password field uses `autocomplete="current-password"`, is reset after success, and remains only in the form after a failure so the UI does not pretend authentication succeeded.

The page shows the local tutorial account explicitly. Remove this development-only helper when replacing the local authenticator with a real identity system.

## Make the Application Shell Session-Aware

The shell derives the authenticated user from the discriminated session union:

```typescript
protected readonly session = this.sessionStore.session;
protected readonly authenticatedUser = computed(() => {
  const session = this.session();
  return session.status === 'authenticated' ? session.user : null;
});
```

Its header renders one honest state:

```html
@if (authenticatedUser(); as user) {
  <nav aria-label="Primary navigation">
    <a routerLink="/schedule">Schedule</a>
    <a routerLink="/volunteers">Volunteers</a>
  </nav>
  <span class="current-user">{{ user.displayName }}</span>
  <button type="button" class="sign-out" (click)="signOut()">Sign out</button>
} @else if (session().status === 'checking') {
  <span class="session-status" role="status">Checking session…</span>
} @else {
  <a class="sign-in" routerLink="/sign-in">Sign in</a>
}
```

Hiding navigation is a presentation choice, not authorization. A user can type any URL manually; the API still authenticates every protected request. Part 14 will coordinate route navigation and return URLs.

## Test Real Authentication Transitions

Angular store tests use `HttpTestingController` to verify the state machine. The 401 case is intentionally different from other failures:

```typescript
it('treats an unauthorized restore as anonymous', async () => {
  const result = firstValueFrom(store.restore());
  http.expectOne('/api/session').flush(null, {
    status: 401,
    statusText: 'Unauthorized',
  });
  await result;

  expect(store.session()).toEqual({ status: 'anonymous' });
});

it('keeps a non-401 restore failure distinct from anonymous', async () => {
  const result = firstValueFrom(store.restore());
  http.expectOne('/api/session').flush(null, {
    status: 503,
    statusText: 'Unavailable',
  });
  await result;

  expect(store.session()).toMatchObject({ status: 'unavailable' });
});
```

Another test serializes the authenticated state and verifies that the submitted password is absent. The sign-out test checks that the store remains authenticated until the server confirms deletion.

API integration tests do not bypass authentication. Their helper performs the real sequence:

1. GET the anonymous session endpoint.
2. Read the issued `XSRF-TOKEN` cookie.
3. POST the test credential with `X-XSRF-TOKEN`.
4. Keep the encrypted session cookie in the test client's cookie container.
5. Replace the request token with the post-authentication token.

Session tests cover:

- anonymous restoration and token issuance;
- 401 for a protected endpoint without a session;
- successful sign-in and returned roles;
- invalid credentials;
- rejection when the antiforgery token is missing;
- server-side session invalidation after logout.

Existing schedule and volunteer integration tests now authenticate through that same helper before calling protected endpoints. This proves the new boundary without weakening the earlier feature coverage.

## Run and Verify Part 13

Run the API and Angular app in separate terminals:

```bash
dotnet run --project server/ChurchServe.Api
npm start
```

Before starting the API, set a local password as shown earlier. Then open `http://localhost:4201/` and select Sign in. Use:

```text
Email: coordinator@churchserve.local
Password: the value stored in your local user-secrets
```

After sign-in, the header displays Demo Coordinator and exposes Schedule, Volunteers, and Sign out. Restarting or refreshing Angular restores the user from the server cookie. Signing out invalidates the cookie before the interface becomes anonymous.

Run the complete verification:

```bash
npm test -- --watch=false
npm run build
dotnet test ChurchServe.slnx -c Release --no-restore
```

Part 13 passes 61 Angular tests across 18 test files, the production Angular build, and 20 API integration tests. A manual runtime check also confirms the real transition from anonymous 401, through antiforgery-protected sign-in, to a 200 response from the protected schedule API.

## Security Boundary

This part establishes authentication but intentionally does not claim to finish production identity or authorization. A deployed system still needs an appropriate identity provider or ASP.NET Core Identity, HTTPS termination, shared Data Protection keys when scaled out, credential recovery, rate limiting, lockout policy, audit events, secret management, and role/policy design.

Angular state can make the session experience clear. It cannot prove identity to the server. Every protected API operation must authenticate and authorize independently.

Part 14 adds route guards and resolvers so navigation waits for the session and returns anonymous users to the page they originally requested—without mistaking a client guard for a security boundary.
