---
title: "ChurchServe Angular Tutorial Part 22-3: Privacy Boundaries and Security Tests"
excerpt: "Minimize volunteer data, preserve Angular's safe rendering path, test hostile input and denied operations, and define production security boundaries."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular privacy security testing"
  description: "Protect volunteer data with minimal DTOs, safe Angular rendering, XSS tests, negative authorization tests, and production security controls."
  socialTitle: "ChurchServe Part 22-3: Privacy and Security Tests"
  socialDescription: "Treat volunteer privacy, hostile input, denial tests, audit design, and data retention as application architecture."
---

# ChurchServe Angular Tutorial Part 22-3: Privacy Boundaries and Security Tests

Part 22-1 enforced capabilities on the API, and Part 22-2 made Angular reflect those decisions. Authorization is necessary, but it does not answer every security question. An authorized response can still reveal too much information, a future template can introduce unsafe rendering, and a production deployment can retain sensitive records longer than the ministry needs them.

Church schedules deserve deliberate protection. A person's name beside a location, ministry, and time can reveal routines, relationships, and religious affiliation. Part 22-3 treats privacy and security verification as architecture rather than a final checklist.

> **Quick answer:** Return only fields required by the current workflow, keep untrusted content on Angular's interpolation path, test hostile-looking values and denied API calls, preserve antiforgery on cookie-authenticated writes, and document the congregation, audit, retention, and identity boundaries that still need production work.

## Start with Data Minimization

ChurchServe's assignment workflow needs a small volunteer projection:

```typescript
export interface Volunteer {
  readonly id: string;
  readonly displayName: string;
  readonly eligibleMinistries: readonly string[];
}
```

The API does not send email addresses, phone numbers, birth dates, background-check documents, pastoral notes, donation history, or children's information because the scheduling screen does not require them.

Removing a field from HTML is not privacy protection if the server still serializes it. Apply minimization before the response leaves the API:

- Query only the columns needed by the workflow.
- Use a response DTO instead of returning an EF Core entity.
- Do not place sensitive values in hidden controls or CSS-hidden elements.
- Avoid copying entire request and response bodies into logs.
- Create a separate, reviewed workflow if contact information becomes necessary.

Collecting less reduces the impact of authorization mistakes, logging mistakes, exports, backups, and compromised accounts.

## State the Current Congregation Boundary Honestly

The tutorial application currently models one congregation. Part 22 therefore does **not** claim to implement tenant isolation.

A future multi-congregation version must derive membership from the trusted identity context and scope every server query and mutation. A request body, route parameter, or query-string congregation ID is user input—not proof that the caller belongs to that congregation.

The minimum multi-tenant rule looks like this conceptually:

```text
authenticated identity
  -> trusted congregation memberships
  -> permission within the selected congregation
  -> resource query constrained to that congregation
```

Checking only the resource ID allows an insecure direct-object-reference problem: a valid user may guess or obtain an identifier belonging to another congregation. Server-side query scoping must make that record inaccessible regardless of the identifier supplied.

## Keep Untrusted Text on Angular's Safe Path

ChurchServe renders event titles and locations with interpolation:

```html
<h2>{{ event().title }}</h2>
<p>{{ event().location }}</p>
```

Angular's [security guidance](https://angular.dev/best-practices/security) explains that interpolation treats values as untrusted text and escapes them. A string that resembles markup remains visible text instead of becoming DOM elements or executable code.

Do not switch to `innerHTML` merely to support formatting. HTML binding introduces a different security context, and `bypassSecurityTrustHtml` is an escape hatch rather than a sanitizer. If ChurchServe later needs rich content, define a restricted format and review its creation, storage, sanitization, and rendering boundaries together.

Angular also recommends a restrictive Content Security Policy and Trusted Types enforcement as deployment-level defense in depth. Those controls reduce the impact of unsafe DOM operations introduced by future code or third-party libraries.

## Test Hostile-Looking Values

A focused component test protects the current rendering choice:

```typescript
it('renders hostile-looking event text as text rather than HTML', () => {
  const fixture = TestBed.createComponent(ServiceEventCard);
  fixture.componentRef.setInput('event', {
    ...event,
    title: '<img src=x onerror=alert(1)>',
    location: '<script>alert(1)</script>',
  });
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('img')).toBeNull();
  expect(fixture.nativeElement.querySelector('script')).toBeNull();
  expect(fixture.nativeElement.textContent).toContain(
    '<img src=x onerror=alert(1)>',
  );
  expect(fixture.nativeElement.textContent).toContain(
    '<script>alert(1)</script>',
  );
});
```

The assertions prove that Angular created neither an `img` nor a `script` element and retained the original text. This test does not make arbitrary future DOM manipulation safe; it detects a regression in this specific component.

## Test Permission Mapping as a Contract

Part 22-1 returns roles and effective permissions from `GET /api/session`. Protect the least-privilege mapping directly:

```csharp
[Fact]
public async Task Session_maps_a_reader_role_to_least_privilege_permissions()
{
  using var factory = new ChurchServeApiFactory("reader");
  using var client = await factory.CreateAuthenticatedClientAsync();

  var user = await client.GetFromJsonAsync<SessionUserResponse>(
      "/api/session");

  Assert.Equal(["reader"], user!.Roles);
  Assert.Equal(
      ["schedule.read", "volunteer.read"],
      user.Permissions);
}
```

An exact assertion matters. `Contains` checks might pass even if a later change accidentally grants `schedule.manage` as well.

Angular's DTO tests should also reject absent or unknown permission strings. That catches an incomplete deployment and prevents the client from treating an unrecognized capability as valid application state.

## Test Denial at the API Boundary

UI tests prove presentation behavior. Authorization tests must call the server directly with an authenticated identity that lacks the required permission.

For assignments, the same reader should be able to inspect the board and receive `403 Forbidden` from the mutation:

```csharp
[Fact]
public async Task Reader_can_view_the_board_but_cannot_assign_a_volunteer()
{
  using var factory = new ChurchServeApiFactory("reader");
  using var client = await factory.CreateAuthenticatedClientAsync();

  var boardResponse = await client.GetAsync(
      "/api/service-events/rehearsal-2026-11-05/assignment-board");
  var assignResponse = await client.PostAsJsonAsync(
      "/api/service-events/rehearsal-2026-11-05/assignments",
      ValidRequest());

  Assert.Equal(HttpStatusCode.OK, boardResponse.StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);
}
```

Also retain the existing anonymous tests. They verify `401 Unauthorized`, while reader tests verify `403 Forbidden`. These are different failure modes and drive different Angular recovery behavior.

An unknown role should have no permissions and receive `403` from volunteer search. This protects the fail-closed default in the role mapping.

## Keep Antiforgery Tests beside Authorization Tests

ChurchServe uses an authentication cookie, so browser requests automatically include credentials. A malicious origin may try to cause a signed-in scheduler's browser to submit a write.

The existing integration tests remove the `X-XSRF-TOKEN` header and prove that service creation and assignment return `400 Bad Request` without mutating data:

```csharp
client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

var response = await client.PostAsJsonAsync(
    "/api/service-events/rehearsal-2026-11-05/assignments",
    ValidRequest());

Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
```

[ASP.NET Core antiforgery protection](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery) and permission policies complement one another:

- Antiforgery asks whether the cookie-authenticated mutation came through the intended request flow.
- Authorization asks whether the authenticated principal may perform the operation.

Neither check replaces the other.

## Treat Cached Data as Authorization-Bound

Part 22-2 includes effective permissions in schedule and assignment cache identity. Tests change the permission list for the same user and verify that cached pages, event details, positions, volunteers, and assignments are cleared.

This matters even when the current cookie changes only after a new sign-in. It documents the invariant needed by future session refresh or permission-revocation behavior: data loaded under one capability set must not silently survive into another.

## Design Audit Records without Creating a New Leak

Production assignment and permission changes need durable audit records, but an audit sink should not become a copy of every sensitive object.

Record a focused event:

```text
timestamp
actor identity
congregation or security boundary
action name
resource identifier
outcome
correlation identifier
```

Avoid raw authentication cookies, authorization headers, passwords, full request bodies, and entire before-and-after volunteer objects. Restrict audit access, define retention, and monitor failed authorization patterns without treating every denial as an attack.

The tutorial does not add a durable audit store in Part 22. Pretending that ordinary application logs satisfy this production requirement would create a false guarantee.

## Define Retention and Identity Operations

Privacy requires lifecycle decisions as well as field selection. Before production, decide:

- how long inactive volunteer records remain;
- who may correct, export, or delete a volunteer's data;
- which records must remain for legitimate operational or legal reasons;
- how backups follow deletion and retention policies;
- how permission changes and account removal invalidate active sessions.

The development-only local account remains intentionally limited. Replace it with ASP.NET Core Identity or an external identity provider for deployment. Persist ASP.NET Core data-protection keys when multiple API instances must decrypt the same cookies.

Claims stored in a cookie do not automatically change when an administrator edits a role. A production system with rapid revocation requirements needs a principal-refresh or session-revocation strategy rather than waiting for an eight-hour session to end.

## Production Security Checklist

Before deploying ChurchServe:

- Replace the local tutorial account with a production identity system.
- Enforce HTTPS and production cookie settings.
- Persist and protect shared data-protection keys.
- Configure a restrictive Content Security Policy and consider Trusted Types.
- Keep response DTOs minimal and logs free of secrets and unnecessary personal data.
- Add a durable, access-controlled audit sink.
- Define retention, correction, export, and deletion processes.
- Add server-side congregation scoping before supporting multiple congregations.
- Define session refresh and permission-revocation behavior.
- Keep dependencies and the hosting platform supported and patched.

## Verify Part 22-3

Run the full application checks because the security boundary crosses server and client code:

```bash
dotnet test ChurchServe.slnx
npm run test:ci
npm run test:coverage
npm run check:architecture
npm run build
```

Part 22 now has three explicit layers: the API enforces permissions, Angular presents those capabilities honestly, and privacy plus security tests constrain what the system exposes and how regressions are detected. Part 23 can build accessibility improvements on top of that boundary without creating a separate experience.
