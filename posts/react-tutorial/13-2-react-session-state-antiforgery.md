---
title: "ChurchServe React Tutorial Part 13-2: Session State and Antiforgery"
excerpt: "Connect React to an ASP.NET Core cookie session with explicit restoration states, validated user profiles, XSRF-aware fetch calls, and accessible sign-in and sign-out UI."
category: "React Tutorial"

seo:
  focusKeyword: "React session state ASP.NET Core cookie authentication"
  description: "Build React session state for ASP.NET Core cookie authentication with startup restoration, runtime validation, antiforgery headers, and tested UI."
  socialTitle: "React Session State with ASP.NET Core Cookies"
  socialDescription: "Restore an encrypted server session in React, model honest identity states, protect mutations with XSRF headers, and test sign-in and sign-out."
---

# ChurchServe React Tutorial Part 13-2: Session State and Antiforgery

[Part 13-1](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-13-1-asp-net-core-cookie-authentication/) protected the ASP.NET Core API with an encrypted cookie session and antiforgery validation. The server can now distinguish anonymous and authenticated requests, but React still needs to restore that server session, present its state honestly, and attach the request token to mutations.

Part 13-2 adds a validated session client, a context provider, an accessible sign-in page, session-aware navigation, sign-out behavior, and XSRF-aware service-event creation. React stores only the returned user profile. The opaque authentication cookie remains controlled by the browser and server.

> **Quick answer:** Model session restoration as `checking`, `anonymous`, `authenticated`, or `unavailable`; do not collapse a failed request into signed out. Put HTTP and cookie-header mechanics behind an injected session client, validate successful JSON at runtime, keep passwords only in the sign-in form command, and become anonymous only after the server confirms sign-out. Read the JavaScript-visible `XSRF-TOKEN` cookie for mutating requests while leaving the `HttpOnly` authentication cookie untouched.

## What the React half adds

The authentication feature is organized separately from schedule behavior:

```text
src/features/auth/
  SessionProvider.tsx
  SignInPage.tsx
  antiforgery.ts
  auth.css
  httpSessionClient.ts
  httpSessionClient.test.ts
  session.ts
  sessionClient.ts
  sessionContext.ts
```

The checkpoint also updates:

```text
src/App.tsx
src/App.test.tsx
src/router.tsx
src/styles/app.css
src/features/schedule/httpServiceEventRepository.ts
src/features/schedule/httpServiceEventRepository.test.ts
```

The schedule components still depend on the repository contract established earlier. Authentication changes composition and transport behavior without pushing cookies or session parsing into feature pages.

## Keep identity separate from credentials

Define the profile React is allowed to retain:

```typescript
export type CurrentUser = {
  readonly id: string
  readonly email: string
  readonly displayName: string
  readonly roles: readonly string[]
}
```

The sign-in command is separate:

```typescript
export type SignInCommand = {
  readonly email: string
  readonly password: string
}
```

The password exists in a controlled form and the command sent to the server. It is never copied into `CurrentUser`, context state, local storage, a URL, or a persisted cache.

React also does not read the authentication cookie. ASP.NET Core marks it `HttpOnly`; the browser attaches it automatically to same-origin requests.

## Model four honest session states

Use a discriminated union:

```typescript
export type SessionState =
  | { readonly status: 'checking' }
  | { readonly status: 'anonymous' }
  | { readonly status: 'authenticated'; readonly user: CurrentUser }
  | { readonly status: 'unavailable'; readonly message: string }
```

Each state answers a different question:

- `checking`: the browser may have a valid cookie, but the API has not answered;
- `anonymous`: the API explicitly returned `401` for the session read;
- `authenticated`: the API returned a validated user profile;
- `unavailable`: the client could not determine whether a session exists.

Treating a timeout or `503` as anonymous is misleading. It may reveal a sign-in form while an active server session still exists, and it tells the user the wrong recovery action.

## Define an injectable session client

Keep React state independent of fetch:

```typescript
export interface SessionClient {
  restore: (signal?: AbortSignal) => Promise<CurrentUser | undefined>
  signIn: (
    command: SignInCommand,
    signal?: AbortSignal,
  ) => Promise<CurrentUser>
  signOut: (signal?: AbortSignal) => Promise<void>
}
```

`undefined` has one narrow meaning: the server answered that the caller is anonymous. Other failures reject.

The production router composes both external boundaries:

```typescript
createAppRoutes(
  new HttpServiceEventRepository(),
  new HttpSessionClient(),
)
```

Tests inject deterministic session clients instead of replacing global fetch or teaching components about a test environment.

## Restore through same-origin fetch

The session client normalizes its base URL and makes cookie intent explicit:

```typescript
const response = await this.fetcher(this.baseUrl, {
  credentials: 'same-origin',
  signal,
})

if (response.status === 401) {
  return undefined
}

await ensureSuccess(response)
return parseCurrentUser(await readJson(response))
```

`same-origin` is the fetch default, but writing it down protects the architectural assumption and makes tests verify it. If deployment later separates origins, changing this to `include` would not be sufficient by itself; the server's CORS, cookie, CSRF, and trust model would also need redesign.

Only `401` becomes anonymous. A `403`, `500`, malformed body, network rejection, or cancellation remains a different outcome.

## Validate the current-user response

HTTP data starts as `unknown`:

```typescript
function parseCurrentUser(value: unknown): CurrentUser {
  if (
    !isRecord(value) ||
    !isNonEmptyString(value.id) ||
    !isNonEmptyString(value.email) ||
    !isNonEmptyString(value.displayName) ||
    !Array.isArray(value.roles) ||
    !value.roles.every(isNonEmptyString)
  ) {
    throw new InvalidSessionResponseError()
  }

  return {
    id: value.id,
    email: value.email,
    displayName: value.displayName,
    roles: [...value.roles],
  }
}
```

The parser copies only expected properties. Even if a server accidentally adds internal data to the JSON object, it does not enter session state.

A TypeScript interface cannot validate bytes received at runtime. Authentication-adjacent data deserves the same distrust as schedule responses.

## Read only the antiforgery request cookie

ASP.NET Core issues a JavaScript-readable cookie named `XSRF-TOKEN`. Extract it from `document.cookie`:

```typescript
export function readAntiforgeryToken(cookieSource: CookieSource) {
  const cookie = cookieSource()
    .split(';')
    .map((value) => value.trim())
    .find((value) => value.startsWith('XSRF-TOKEN='))

  if (!cookie) {
    throw new MissingAntiforgeryTokenError()
  }

  const encodedToken = cookie.slice('XSRF-TOKEN='.length)
  if (!encodedToken) {
    throw new MissingAntiforgeryTokenError()
  }

  return decodeURIComponent(encodedToken)
}
```

Injecting `CookieSource` makes parsing testable without mutating `document.cookie` globally.

Do not search for `ChurchServe.Session`. It is deliberately `HttpOnly` and should remain invisible to React.

If the request token is missing, fail before sending a mutation. The application should restore the session contract rather than submit a request that the server must reject.

## Sign in with the antiforgery header

The sign-in request sends JSON, same-origin credentials, and the token header:

```typescript
const response = await this.fetcher(`${this.baseUrl}/sign-in`, {
  method: 'POST',
  credentials: 'same-origin',
  headers: {
    'Content-Type': 'application/json',
    'X-XSRF-TOKEN': readAntiforgeryToken(this.cookieSource),
  },
  body: JSON.stringify(command),
  signal,
})

await ensureSuccess(response)
return parseCurrentUser(await readJson(response))
```

The browser stores the encrypted authentication cookie from the response. React retains the validated profile only.

`SessionHttpError` preserves status so the sign-in page can distinguish incorrect credentials from general unavailability without parsing an error string.

## Sign out through the server

Sign-out is also a protected mutation:

```typescript
const response = await this.fetcher(this.baseUrl, {
  method: 'DELETE',
  credentials: 'same-origin',
  headers: {
    'X-XSRF-TOKEN': readAntiforgeryToken(this.cookieSource),
  },
  signal,
})

await ensureSuccess(response)
```

The provider becomes anonymous only after this promise resolves. If the request fails, the UI keeps the authenticated profile and reports that sign-out did not complete. Clearing the profile optimistically would imply that the server cookie was invalidated when it may still be active.

## Restore once at the provider boundary

`SessionProvider` begins in `checking` and starts one abort-aware restore operation:

```typescript
useEffect(() => {
  const controller = new AbortController()
  activeRestore.current = controller

  void client
    .restore(controller.signal)
    .then((user) => {
      if (!controller.signal.aborted) {
        setState(
          user
            ? { status: 'authenticated', user }
            : { status: 'anonymous' },
        )
      }
    })
    .catch(() => {
      if (!controller.signal.aborted) {
        setState({
          status: 'unavailable',
          message: 'ChurchServe could not verify your session. Try again.',
        })
      }
    })

  return () => controller.abort()
}, [client])
```

Cleanup supports React Strict Mode's development checks and prevents an obsolete result from changing state after unmount.

The provider exposes four operations through context:

```typescript
type SessionContextValue = {
  readonly state: SessionState
  readonly restore: () => Promise<void>
  readonly signIn: (command: SignInCommand) => Promise<void>
  readonly signOut: () => Promise<void>
}
```

Components consume the selected client indirectly. They do not construct fetch adapters or handle cookies.

## Block route content while identity is unknown

The application shell remains visible during restoration, but it does not mount the active route:

```tsx
{state.status === 'checking' ? (
  <p className="session-panel" role="status">
    Restoring your ChurchServe session…
  </p>
) : state.status === 'unavailable' ? (
  <section className="session-panel" role="alert">
    <h1>We could not verify your session</h1>
    <p>{state.message}</p>
    <button
      type="button"
      onClick={() => void restore().catch(() => undefined)}
    >
      Try again
    </button>
  </section>
) : (
  <Outlet />
)}
```

Without this gate, a schedule route could start its protected API request while session restoration was still pending, causing a temporary `401` error state.

An unavailable session is not offered a sign-in link. Retry is the honest action because the application does not know whether the caller is already authenticated.

## Make navigation session-aware

Authenticated users see navigation, their display name, and sign-out:

```tsx
{state.status === 'authenticated' ? (
  <>
    <nav aria-label="Primary navigation">
      <NavLink to="/schedule">Schedule</NavLink>
      <NavLink to="/volunteers">Volunteers</NavLink>
    </nav>
    <span className="current-user">{state.user.displayName}</span>
    <button type="button" onClick={endSession}>
      Sign out
    </button>
  </>
) : state.status === 'anonymous' ? (
  <NavLink to="/sign-in">Sign in</NavLink>
) : (
  <span className="session-status">Session unavailable</span>
)}
```

Hiding links improves clarity. It does not secure routes or data. A user can type `/schedule` directly, and Part 14 will improve that navigation workflow. The API from Part 13-1 remains the actual security boundary.

## Build an accessible sign-in page

The form controls email and password locally:

```tsx
<label htmlFor="email">Email</label>
<input
  id="email"
  name="email"
  type="email"
  autoComplete="username"
  required
  value={email}
  onChange={(event) => setEmail(event.target.value)}
/>

<label htmlFor="password">Password</label>
<input
  id="password"
  name="password"
  type="password"
  autoComplete="current-password"
  required
  value={password}
  onChange={(event) => setPassword(event.target.value)}
/>
```

Submission waits for the server before navigating:

```typescript
try {
  await signIn({ email, password })
  setPassword('')
  await navigate('/schedule')
} catch (error) {
  setErrorMessage(
    error instanceof SessionHttpError && error.status === 401
      ? 'The email or password was incorrect.'
      : 'Sign-in is unavailable. Try again.',
  )
}
```

The button is disabled while pending, errors use `role="alert"`, and the password is cleared after success. On failure it remains in the form so the page does not erase the user's work or imply that authentication succeeded.

The local-account helper text is explicitly tutorial-only. Remove it with the local authenticator.

## Add XSRF to schedule creation

The session endpoints are not the only mutations. Update the HTTP service-event repository:

```typescript
const response = await this.fetcher(this.baseUrl, {
  method: 'POST',
  credentials: 'same-origin',
  headers: {
    'Content-Type': 'application/json',
    'X-XSRF-TOKEN': readAntiforgeryToken(this.cookieSource),
  },
  body: JSON.stringify(command),
  signal,
})
```

List and detail requests also specify `credentials: 'same-origin'` so their cookie boundary is visible in code and tests, but safe reads do not need an XSRF header.

Future update, delete, assignment, and publication commands must follow the same mutation rule. Centralizing request construction may become worthwhile when more feature repositories exist; Part 13 keeps the dependency explicit in the two current adapters.

## Test transport and visible behavior

`HttpSessionClient` tests inject both fetch and a cookie source. They verify:

- authenticated restoration and same-origin credentials;
- `401` restoration becoming anonymous;
- successful responses with invalid user JSON being rejected;
- decoded XSRF headers on sign-in;
- XSRF headers on sign-out;
- missing token cookies preventing network mutations.

The service-event repository test now verifies same-origin credentials and the creation token. A separate test proves that `create` refuses to call fetch when the token is absent.

Application tests inject a session client and verify user behavior:

- authenticated restoration exposes navigation and the display name;
- anonymous sign-in submits the command and navigates after success;
- an unavailable restore shows retry rather than sign-in;
- retry can restore an authenticated session;
- failed sign-out reports an alert and preserves authenticated UI;
- existing schedule, detail, creation, and not-found routes still work.

These tests assert state transitions users can observe rather than the provider's internal hook calls.

## Run the complete Part 13 checkpoint

Set the local API password once:

```bash
dotnet user-secrets set \
  --project server/ChurchServe.Api \
  "Authentication:LocalAccount:Password" \
  "<choose-a-local-password>"
```

Run API and React in separate terminals:

```bash
npm run api
npm run dev
```

Open the Vite URL and sign in with:

```text
Email: coordinator@churchserve.local
Password: the value stored in user-secrets
```

Run the full quality gate:

```bash
npm run lint
npm run typecheck
npm test
npm run build
dotnet build server/ChurchServe.Api/ChurchServe.Api.csproj \
  --configuration Release
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj \
  --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project server/ChurchServe.Api
```

The finalized checkpoint passes lint and strict TypeScript checking, runs 59 React tests across 10 files, builds the Vite production bundle, passes nine API integration tests, builds the .NET API with zero warnings and errors, and reports no missing migration.

## Security and production boundaries

React now represents the session honestly, but several boundaries remain:

- React state is presentation state, not proof of identity or permission.
- Client route protection in Part 14 will improve navigation, not replace server authorization.
- The same-origin cookie design depends on production routing and HTTPS configuration.
- XSS can perform actions as the current user even when it cannot read an `HttpOnly` cookie. Keep dependencies, rendering, and content policies disciplined.
- Antiforgery mitigates cross-site request forgery; it does not mitigate XSS or stolen sessions.
- Avoid logging sign-in commands, token headers, cookie values, or complete session responses.
- Multiple browser tabs can observe session changes at different times. A future invalidation strategy may refresh on focus or after `401` responses.
- Roles are displayed data until server authorization policies enforce capabilities.
- A production identity provider may change sign-in UX while the `checking`, `anonymous`, `authenticated`, and `unavailable` distinctions remain useful.

## What Part 13-2 established

React now participates in the session without becoming the authority:

- the session state distinguishes checking, anonymous, authenticated, and unavailable;
- successful user JSON is validated before entering context;
- fetch sends the opaque authentication cookie through same-origin browser behavior;
- React reads only the antiforgery request-token cookie;
- sign-in keeps credentials outside durable application state;
- sign-out changes state only after server confirmation;
- route content waits until initial restoration resolves;
- navigation and feedback reflect the current session honestly;
- schedule mutations carry the XSRF header;
- tests cover transport, validation, retry, sign-in, and failed sign-out.

Part 14 can add route guards and return URLs so anonymous users are redirected before protected pages mount and return to their intended destination after sign-in—while the server remains the final security boundary.
