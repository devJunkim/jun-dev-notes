---
title: "ChurchServe React Tutorial Part 22-3: Privacy Boundaries and Security Tests"
excerpt: "Minimize volunteer data, preserve React's safe text rendering, test hostile input and denied operations, and define production security boundaries."
category: "React Tutorial"

seo:
  focusKeyword: "React privacy security testing"
  description: "Protect volunteer data with minimal DTOs, safe React rendering, XSS regression tests, API denial tests, and production security controls."
  socialTitle: "ChurchServe React Part 22-3: Privacy and Security Tests"
  socialDescription: "Treat volunteer privacy, hostile input, denial tests, audit design, and retention as application architecture."
---

# ChurchServe React Tutorial Part 22-3: Privacy Boundaries and Security Tests

Part 22-1 enforced capabilities on the API, and Part 22-2 made React reflect those decisions. Authorization is necessary, but it does not answer every security question. An authorized response can still reveal too much, a future component can introduce unsafe HTML rendering, and a deployment can retain sensitive records longer than its ministry needs them.

Church schedules deserve deliberate protection. A person’s name beside a location, ministry, and time can reveal routines, relationships, and religious affiliation.

> **Quick answer:** Return only fields required by the workflow, keep untrusted values as ordinary React text children, test hostile-looking content and denied API operations, preserve antiforgery on cookie writes, and document the tenancy, audit, retention, identity, and revocation boundaries still required for production.

## Minimize volunteer data before serialization

The assignment workflow needs a narrow volunteer projection:

```typescript
export type Volunteer = {
  readonly id: string
  readonly displayName: string
  readonly eligibleMinistries: readonly string[]
}
```

The assignment-board endpoint selects only those values:

```csharp
.Select(item => new VolunteerResponse(
    item.Id,
    item.DisplayName,
    item.EligibleMinistries
        .OrderBy(value => value.Ministry)
        .Select(value => value.Ministry)
        .ToArray()))
```

It does not send email addresses, phone numbers, birth dates, background-check documents, pastoral notes, donation history, or children’s information because this screen does not need them.

Removing a field from JSX is not privacy protection if the API still serializes it. Minimize the query and response DTO before the data leaves the trusted process.

## State the current congregation boundary honestly

The tutorial models one congregation. Part 22 does not claim tenant isolation.

A future multi-congregation design must derive membership from trusted identity context and constrain every query and mutation to that boundary:

```text
authenticated identity
  -> trusted congregation memberships
  -> permission inside the selected congregation
  -> resource query constrained to that congregation
```

A congregation ID from a route, query string, or request body is user input. It is not proof of membership. Checking only a resource ID can create an insecure direct-object-reference problem where a valid user obtains another congregation’s record.

## Keep untrusted values on React’s text path

ChurchServe renders event data as ordinary JSX children:

```tsx
<h2>{event.title}</h2>
<span>{event.location}</span>
```

Do not replace this with raw HTML merely to add formatting. React’s documentation warns that [`dangerouslySetInnerHTML`](https://react.dev/reference/react-dom/components/common#dangerously-setting-the-inner-html) can introduce cross-site scripting when content is not trusted and sanitized.

If rich content becomes a requirement, define the allowed format, sanitization policy, authoring boundary, storage rules, and rendering sink together. A method name containing “dangerously” is not itself a sanitizer.

## Protect the current rendering choice with a test

Add a hostile-looking event test:

```tsx
it('renders hostile-looking values as text rather than markup', () => {
  renderCard({
    ...event,
    title: '<img src=x onerror=alert(1)>',
    location: '<script>alert(1)</script>',
  })

  expect(document.querySelector('.event-card img')).toBeNull()
  expect(document.querySelector('.event-card script')).toBeNull()
  expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeVisible()
  expect(screen.getByText('<script>alert(1)</script>')).toBeVisible()
})
```

This proves that the current component created neither an image nor a script element. It does not make arbitrary future DOM manipulation safe.

## Assert exact least privilege

The session integration test signs in as a reader and compares the complete permission set:

```csharp
Assert.Equal(["reader"], currentUser.Roles);
Assert.Equal(
    ["schedule.read", "volunteer.read"],
    currentUser.Permissions);
```

An exact assertion matters. `Contains("schedule.read")` would still pass if a later mistake also granted `schedule.manage`.

Client DTO tests reject missing or unknown permission strings. That protects the deployment contract and stops React from accepting an unrecognized capability as valid application state.

## Test denial at the API boundary

UI tests prove presentation. Authorization tests must call the API with an authenticated identity that lacks the capability:

```csharp
[Fact]
public async Task Reader_can_view_the_board_but_cannot_assign_a_volunteer()
{
    var factory = new ChurchServeApiFactory(role: "reader");
    using var client = factory.CreateClient();
    await client.SignInAsync(TestContext.Current.CancellationToken);

    var boardResponse = await client.GetAsync(
        "/api/service-events/sunday-2026-11-01/assignment-board");
    var assignResponse = await client.PostAsJsonAsync(
        "/api/service-events/sunday-2026-11-01/assignments",
        WelcomeRequest());

    Assert.Equal(HttpStatusCode.OK, boardResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);
}
```

An unsupported role has no permission claims and receives `403` from the assignment board. Anonymous tests continue to expect `401`. Those are separate conditions and drive different recovery UI.

## Keep antiforgery regression tests beside policy tests

The existing mutation tests remove `X-XSRF-TOKEN`, submit a cookie-authenticated write, and assert `400 Bad Request` without persisted data.

Permission and antiforgery tests complement each other:

- authorization blocks an authenticated reader from a scheduler operation;
- antiforgery blocks an unintended browser-origin mutation for an authorized scheduler.

Keep both when endpoints evolve. A policy refactor should not accidentally remove request-origin protection, and an antiforgery change should not weaken least privilege.

## Treat caches as authorization-bound data

Part 22-2 includes effective permissions in schedule-feature cache identity. A test loads both stores, changes the same user from scheduler permissions to reader permissions, and asserts both snapshots return to `idle`.

That invariant matters even though the current app normally changes permissions through a new sign-in and route remount. It prepares the application for future session refresh and revocation behavior.

## Design audits without creating another leak

Production assignment and permission changes need durable audit records, but an audit store should not become a duplicate of every sensitive object.

Record focused events:

```text
timestamp
actor identity
congregation or security boundary
action name
resource identifier
outcome
correlation identifier
```

Avoid authentication cookies, authorization headers, passwords, full request bodies, and complete before-and-after volunteer objects. Restrict access to audit data and define its retention separately.

Part 22 does not add an audit database. Ordinary application logs are not presented as a substitute for that production design.

## Define retention, identity, and revocation

Before production, decide:

- how long inactive volunteer and assignment records remain;
- who may correct, export, or delete personal data;
- how backups follow deletion and retention policy;
- which operational or legal requirements justify retained records;
- how role changes and account removal invalidate active sessions.

The development-only local account must be replaced by ASP.NET Core Identity or an external provider. Multi-instance deployments must persist and protect shared data-protection keys so every instance can decrypt the same cookies.

Permission claims inside a cookie do not change immediately when an administrator edits a role. A deployment requiring fast revocation needs principal refresh or session invalidation rather than waiting for the eight-hour cookie lifetime.

## Production security checklist

- Replace the local tutorial account with a production identity system.
- Enforce HTTPS and production cookie settings.
- Persist and protect shared data-protection keys.
- Configure a restrictive Content Security Policy and consider Trusted Types.
- Keep DTOs minimal and logs free of secrets and unnecessary personal data.
- Add a durable, access-controlled audit sink.
- Define retention, correction, export, and deletion processes.
- Add server-side congregation scoping before supporting multiple congregations.
- Define session refresh and permission-revocation behavior.
- Keep dependencies and the hosting platform supported and patched.

## Verify the complete boundary

Part 22 passes the architecture rule, linter, strict TypeScript checks, production Vite build, 117 React tests, 20 API integration tests, and the EF Core model-drift check. Statement coverage is 90.29 percent.

## Result

Part 22 now has three explicit layers: ASP.NET Core enforces capabilities, React presents them honestly, and privacy plus negative tests constrain what the system exposes. The chapter also states what remains outside the tutorial instead of implying production guarantees that do not exist.

Part 23 can build accessibility improvements on this boundary without creating a separate experience.
