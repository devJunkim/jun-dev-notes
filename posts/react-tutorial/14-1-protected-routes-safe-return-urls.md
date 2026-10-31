---
title: "ChurchServe React Tutorial Part 14-1: Protected Routes and Safe Return URLs"
excerpt: "Protect authenticated React Router branches, preserve deep links through sign-in, and validate return URLs without creating an open redirect."
category: "React Tutorial"

seo:
  focusKeyword: "React Router protected routes safe return URL"
  description: "Build React Router protected routes with session-aware redirects, safe return URL validation, deep-link restoration, and behavior-focused tests."
  socialTitle: "Protected React Routes and Safe Return URLs"
  socialDescription: "Add an authenticated route boundary, preserve deep links through sign-in, and reject unsafe return targets in React Router."
---

# ChurchServe React Tutorial Part 14-1: Protected Routes and Safe Return URLs

[Part 13-2](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-13-2-session-state-and-antiforgery/) gave React an honest session model. The application can distinguish an authenticated user from an anonymous browser, wait while restoration is in progress, and report an unavailable session service without pretending the user signed out.

That state now needs to shape routing. An anonymous visitor who opens a schedule deep link should see sign-in, not a protected page that immediately fails. After authentication, the application should return to the original URL instead of always sending the visitor to the schedule root.

Part 14 is split by responsibility. Part 14-1 adds the authenticated route boundary and safe return URLs. Part 14-2 moves detail fetching into a route loader and protects unsaved form work.

> **Quick answer:** Put authenticated pages beneath a pathless route whose element reads the trusted session state and renders either `<Outlet />` or `<Navigate />`. Include the requested path, query, and fragment in a `returnTo` query parameter, but treat that value as untrusted input. Accept only application-local paths, reject absolute, protocol-relative, and backslash-based targets, then use `navigate(target, { replace: true })` after sign-in. Keep server authorization in place because a React route boundary is user experience, not a security control.

## Separate public and authenticated route branches

The home and sign-in routes remain public. Schedule and volunteer pages belong to one pathless protected branch:

```tsx
export function createAppRoutes(
  repository: ServiceEventRepository,
  sessionClient: SessionClient,
): RouteObject[] {
  return [
    {
      path: '/',
      element: <App repository={repository} sessionClient={sessionClient} />,
      children: [
        { index: true, Component: HomePage },
        { path: 'sign-in', Component: SignInPage },
        {
          Component: RequireAuthenticatedSession,
          children: [
            { path: 'schedule', Component: SchedulePage },
            { path: 'schedule/new', Component: ServiceEventCreatePage },
            {
              path: 'schedule/:eventId',
              Component: ServiceEventDetailPage,
            },
            { path: 'volunteers', Component: VolunteersPage },
          ],
        },
        { path: '*', Component: NotFoundPage },
      ],
    },
  ]
}
```

The route has no `path`, so it adds behavior and nesting without adding a URL segment. Every protected descendant passes through the same decision. This avoids copying authentication checks into each page.

Keeping sign-in outside the boundary is essential. Placing it inside would redirect an anonymous user from sign-in back to sign-in indefinitely.

## Build the authenticated route boundary

The boundary reads the session restored by `SessionProvider`:

```tsx
import { Navigate, Outlet, useLocation } from 'react-router'
import { useSession } from './sessionContext'

export function RequireAuthenticatedSession() {
  const { state } = useSession()
  const location = useLocation()

  if (state.status !== 'authenticated') {
    const returnTo =
      `${location.pathname}${location.search}${location.hash}`

    return (
      <Navigate
        replace
        to={`/sign-in?returnTo=${encodeURIComponent(returnTo)}`}
      />
    )
  }

  return <Outlet />
}
```

`useLocation()` supplies all three parts needed to reconstruct a deep link. `encodeURIComponent` makes that complete target one query-parameter value rather than letting its `?`, `&`, or `#` characters change the sign-in URL.

`replace` matters for history behavior. Without it, the denied protected URL remains immediately behind the sign-in page. Pressing Back would revisit the denied route and redirect again. Replacing that entry gives the user a more predictable history stack.

The application shell already withholds its `<Outlet />` while session state is `checking` or `unavailable`. The boundary therefore normally sees only `anonymous` or `authenticated`. Keeping its condition defensive still prevents protected content if its surrounding shell changes later.

## Client protection is not authorization

This component prevents protected UI from mounting for an anonymous session. It does not make the underlying data private.

A user controls the browser. They can call an API without rendering React, alter JavaScript locally, or send requests with another client. ASP.NET Core must continue applying `RequireAuthorization()` to protected endpoints and validating antiforgery tokens on cookie-authenticated mutations.

There is another data-router nuance: route loaders are selected before route elements render. A component boundary does not guarantee that a matched child loader never starts. The server must safely reject an anonymous loader request. Applications that must avoid even starting such work can move authentication into a router loader or middleware architecture, but that still complements rather than replaces server authorization.

The route boundary improves navigation and presentation. The API owns access control.

## Preserve the intended destination

An anonymous visit to this URL:

```text
/schedule/sunday-2026-11-01?view=staffing#positions
```

becomes:

```text
/sign-in?returnTo=%2Fschedule%2Fsunday-2026-11-01%3Fview%3Dstaffing%23positions
```

After successful sign-in, React can recover the original path. However, a query parameter is user-controlled even when the application generated most instances of it. This is unsafe:

```typescript
const returnTo = new URLSearchParams(location.search).get('returnTo')
await navigate(returnTo ?? '/schedule')
```

An attacker could send a sign-in link containing an absolute or protocol-relative destination. Redirecting there after authentication creates an open redirect that can make a phishing destination appear to follow a legitimate sign-in flow.

## Validate the return target as a local URL

Keep validation in one small function:

```typescript
const applicationOrigin = 'https://churchserve.local'

export function safeReturnTo(search: string, fallback = '/schedule') {
  const candidate = new URLSearchParams(search).get('returnTo')

  if (
    !candidate ||
    !candidate.startsWith('/') ||
    candidate.startsWith('//') ||
    candidate.includes('\\')
  ) {
    return fallback
  }

  const target = new URL(candidate, applicationOrigin)
  if (target.origin !== applicationOrigin) {
    return fallback
  }

  return `${target.pathname}${target.search}${target.hash}`
}
```

The checks have distinct purposes:

- a valid target must begin at the application root;
- `//evil.example` is protocol-relative and therefore external;
- backslashes have historically been interpreted inconsistently across URL consumers;
- parsing against a fixed origin provides a final origin check;
- the returned value is reconstructed from parsed path, query, and fragment components.

The fixed origin is only a parsing base. It does not need to match the development port because the function returns a relative target, not that absolute URL.

For an application with a small set of destinations, an allowlist can be even stricter. For example, accept only `/schedule` and descendants. The correct policy depends on whether all local routes are legitimate post-sign-in targets.

## Navigate after the session is established

The sign-in page derives the destination during rendering:

```tsx
export function SignInPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { state, signIn } = useSession()
  const returnTo = safeReturnTo(location.search)

  // form state omitted
}
```

Successful submission updates the real session state before navigation:

```typescript
try {
  await signIn({ email, password })
  setPassword('')
  await navigate(returnTo, { replace: true })
} catch (error) {
  setErrorMessage(
    error instanceof SessionHttpError && error.status === 401
      ? 'The email or password was incorrect.'
      : 'Sign-in is unavailable. Try again.',
  )
}
```

The session client waits for ASP.NET Core to issue the authentication cookie and return the current-user profile. Only then does `SessionProvider` enter `authenticated`, allowing the protected branch to render.

Using `replace` removes the sign-in URL from the immediate history entry. Back does not take the newly authenticated user to a stale form containing a now-irrelevant `returnTo` value.

If an authenticated user opens the sign-in page directly, the same validated target can drive the Continue link:

```tsx
<Link className="primary-action auth-link" to={returnTo}>
  Continue
</Link>
```

## Test redirects as user-visible behavior

The application test starts with an anonymous session at a protected deep link:

```typescript
const sessionClient: SessionClient = {
  restore: vi.fn().mockResolvedValue(undefined),
  signIn: vi.fn().mockResolvedValue(currentUser),
  signOut: vi.fn().mockResolvedValue(undefined),
}

const router = renderRoute(
  '/schedule/sunday-2026-11-01',
  sessionClient,
)

await screen.findByRole('heading', {
  name: 'Sign in to coordinate ChurchServe',
})

expect(
  `${router.state.location.pathname}${router.state.location.search}`,
).toBe('/sign-in?returnTo=%2Fschedule%2Fsunday-2026-11-01')
```

The test then submits credentials and verifies both the rendered event and final location. That proves restoration of the actual deep link rather than merely checking that `<Navigate>` appeared.

The validator receives focused tests for normal local targets and hostile forms:

```typescript
it.each([
  '?returnTo=https%3A%2F%2Fevil.example',
  '?returnTo=%2F%2Fevil.example',
  '?returnTo=%2F%5Cevil.example',
])('rejects an external or ambiguous return target', (search) => {
  expect(safeReturnTo(search)).toBe('/schedule')
})
```

Another application test signs in with an unsafe target and confirms that the router lands on `/schedule`. Unit tests make edge cases cheap; the application test proves the fallback is wired into the real flow.

## Production boundaries

Protected routing introduces several operational considerations:

- Preserve only URLs that the current user is allowed to open. The server must still return `403` when an authenticated identity lacks a required role or policy.
- A session may expire after the guard renders. HTTP handling should recognize `401`, refresh session state appropriately, and avoid redirect loops.
- Do not place secrets, credentials, or sensitive state in `returnTo`; it appears in browser history and may enter logs or analytics.
- Validate destinations at the final navigation point even if the guard originally generated them.
- Consider an explicit destination allowlist when the application has a narrow post-authentication surface.
- Keep sign-in and access-denied behavior separate. Re-authentication cannot fix a genuine authorization failure.
- Multi-tab sign-out can leave another tab with stale client state until its next session check or failed API request.

React Router documents nested route objects and outlets in its [route object guide](https://reactrouter.com/start/data/route-object). The important architectural boundary remains independent of router API details: the browser may decide what to show, while the server decides what the caller may do.

## What Part 14-1 established

ChurchServe now handles protected navigation deliberately:

- public and authenticated routes occupy separate branches;
- one pathless boundary protects all authenticated descendants;
- anonymous deep links redirect to sign-in with path, query, and fragment preserved;
- redirects replace denied and sign-in history entries;
- return targets are parsed as untrusted input and restricted to local URLs;
- authenticated users return to their intended destination;
- tests cover deep-link restoration and malicious return targets;
- ASP.NET Core remains the authoritative security boundary.

Part 14-2 will use React Router's data APIs for detail loading and add explicit protection against discarding a dirty creation form.
