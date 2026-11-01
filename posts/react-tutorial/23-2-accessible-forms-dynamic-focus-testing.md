---
title: "ChurchServe React Tutorial Part 23-2: Accessible Forms, Dynamic Focus, and Testing"
excerpt: "Connect React validation errors to fields, preserve focus during optimistic assignments, announce results, and scan meaningful UI states with axe-core."
category: "React Tutorial"

seo:
  focusKeyword: "React accessible form validation"
  description: "Build accessible React forms and dynamic workflows with associated errors, predictable focus, status messages, axe-core scans, and keyboard tests."
  socialTitle: "ChurchServe React Part 23-2: Accessible Forms and Testing"
  socialDescription: "Keep React validation and optimistic volunteer assignments understandable for keyboard and screen-reader users."
---

# ChurchServe React Tutorial Part 23-2: Accessible Forms, Dynamic Focus, and Testing

Part 23-1 established document structure and predictable focus after route navigation. Dynamic workflows need finer control. An error can be visible without being associated with its field, and an optimistic update can remove the focused button before a keyboard user learns what changed.

This second half connects validation messages to their controls, moves focus to useful recovery points, preserves context during volunteer assignment, and adds automated accessibility scans without treating automation as proof of conformance.

> **Quick answer:** Render `aria-invalid` and `aria-describedby` only with active errors, focus the first invalid field after React commits validation, move focus to a stable assignment row when optimistic UI removes its button, announce asynchronous results with restrained status regions, and run axe against meaningful states while retaining direct interaction and manual tests.

## Focus the first invalid control

Create a shared DOM helper:

```typescript
export function focusFirstInvalidControl(root: ParentNode): void {
  root
    .querySelector<HTMLElement>('[aria-invalid="true"]')
    ?.focus()
}
```

The supplied root is the relevant form, not the complete document. Another invalid form elsewhere cannot capture focus.

Its focused unit test establishes ordering:

```typescript
const form = document.createElement('form')
form.innerHTML = `
  <input id="valid-control">
  <input id="first-invalid" aria-invalid="true">
  <input id="second-invalid" aria-invalid="true">
`
document.body.append(form)

focusFirstInvalidControl(form)

expect(document.activeElement).toBe(
  form.querySelector('#first-invalid'),
)
```

This helper depends on the rendered accessibility state rather than duplicating the validation field order in TypeScript.

## Wait for React to render validation state

State updates do not change the DOM during the current event handler. Schedule focus after React renders the invalid attributes and messages:

```tsx
function scheduleInvalidFocus(form: HTMLFormElement | null): void {
  form?.ownerDocument.defaultView?.setTimeout(() => {
    if (form.isConnected) {
      focusFirstInvalidControl(form)
    }
  })
}
```

Keep a ref to the service-event form and call the helper after local validation fails:

```tsx
const formRef = useRef<HTMLFormElement>(null)

const result = parseServiceEventForm(values)
if (!result.valid) {
  setErrors(result.errors)
  scheduleInvalidFocus(formRef.current)
  return
}
```

The `isConnected` check prevents a queued callback from focusing a form that navigation has already removed.

## Handle server validation through the same path

The create route can receive field errors from the API after a request. Watch that prop and use the same focus rule:

```tsx
useEffect(() => {
  if (Object.keys(serverErrors ?? {}).length > 0) {
    scheduleInvalidFocus(formRef.current)
  }
}, [serverErrors])
```

Client and server validation now lead to the same recovery point. Known server fields are rendered beside their controls; unknown field names remain excluded rather than being reflected into the page.

Tests cover both paths. The API-validation test verifies the returned message, preserved value, focused Service name field, and `aria-describedby="service-title-error"` relationship.

## Associate each error with its field

The service form already exposes active errors through `aria-invalid` and `aria-describedby`:

```tsx
<input
  id="service-title"
  name="title"
  value={values.title}
  aria-invalid={Boolean(displayedErrors.title)}
  aria-describedby={
    displayedErrors.title ? 'service-title-error' : undefined
  }
  onChange={(event) => updateField('title', event.target.value)}
/>

{displayedErrors.title && (
  <p id="service-title-error" className="field-error">
    {displayedErrors.title}
  </p>
)}
```

The W3C’s [`aria-invalid` technique](https://www.w3.org/WAI/WCAG22/Techniques/aria/ARIA21) pairs the invalid state with an `aria-describedby` reference to visible error text. Do not point at an error ID that is absent from the DOM.

Date fields can have help text, a field-specific error, and a shared time-range error. Build their description list from the elements that exist:

```typescript
function descriptionIds(...ids: Array<string | undefined>): string {
  return ids.filter((id): id is string => Boolean(id)).join(' ')
}
```

The visible error summary remains an alert, while focus lands on the first field requiring action. WCAG’s [Error Identification guidance](https://www.w3.org/WAI/WCAG22/Understanding/error-identification) requires useful text, not a color change alone.

## Give sign-in the same recovery behavior

Native browser validation varies in message persistence and presentation. ChurchServe uses `noValidate`, renders its own stable messages, and applies the shared focus helper:

```tsx
<input
  id="email"
  name="email"
  type="email"
  value={email}
  aria-invalid={errors.email ? true : undefined}
  aria-describedby={errors.email ? 'email-error' : undefined}
  onChange={(event) => {
    setEmail(event.target.value)
    setErrors((current) => ({ ...current, email: undefined }))
  }}
/>

{errors.email && (
  <p id="email-error" className="auth-error">
    {errors.email}
  </p>
)}
```

On submission, validation builds errors for the email and password. If either exists, React renders them and the first invalid input receives focus. Changing a field clears only that field’s message.

API authentication failures remain a form-level alert because they are not properties of one input. An incorrect password response should not claim that the password’s format is invalid.

## Preserve focus during optimistic assignment

The assignment button disappears as soon as the optimistic store adds an assignment. Without a handoff, browser focus can fall back to the document body.

Make each position row a stable programmatic target with an accessible name:

```tsx
const headingId = `position-heading-${position.id}`

<li
  id={`position-${position.id}`}
  ref={(element) => {
    if (element) {
      positionRows.current.set(position.id, element)
    } else {
      positionRows.current.delete(position.id)
    }
  }}
  tabIndex={-1}
  aria-labelledby={headingId}
>
  <div className="position-copy">
    <span>{position.ministry}</span>
    <h3 id={headingId}>{position.roleName}</h3>
  </div>
</li>
```

The row does not become another Tab stop. A ref map avoids constructing a selector from a server-provided position ID.

After a valid assignment starts, clear the selector and focus the row after the optimistic render:

```tsx
const result = store.assign(position.id, selectedVolunteer)
if (result.valid) {
  setSelectedVolunteers((current) => ({
    ...current,
    [position.id]: '',
  }))
  clickEvent.currentTarget.ownerDocument.defaultView?.setTimeout(
    () => positionRows.current.get(position.id)?.focus(),
  )
}
```

The focused row immediately contains the volunteer name and **Saving…** state. Its `:focus-visible` outline uses the Part 23-1 focus color.

## Announce the asynchronous result

Keep routine success polite and failures assertive:

```tsx
<p
  className={`assignment-notice ${board.notice.kind}`}
  role={board.notice.kind === 'success' ? 'status' : 'alert'}
  aria-atomic="true"
>
  {board.notice.message}
</p>
```

The row focus communicates where the change occurred. The live status communicates whether the request eventually succeeded or failed. `aria-atomic="true"` asks assistive technology to announce the complete message when it changes.

WCAG’s [Status Messages guidance](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html) distinguishes status announcements from focus changes. Use a polite status for routine completion; reserve alerts for failures that need immediate attention.

## Add axe-core as a test dependency

Install the rules engine for tests only:

```bash
npm install --save-dev axe-core
```

Create a small assertion helper around the official [`axe.run` API](https://github.com/dequelabs/axe-core/blob/develop/doc/API.md):

```typescript
export async function expectNoAccessibilityViolations(
  root: Element,
): Promise<void> {
  const results = await axe.run(root, {
    runOnly: {
      type: 'tag',
      values: [
        'wcag2a',
        'wcag2aa',
        'wcag21a',
        'wcag21aa',
        'best-practice',
      ],
    },
    rules: {
      'color-contrast': { enabled: false },
      'link-in-text-block': { enabled: false },
    },
  })

  expect(
    results.violations,
    formatViolations(results.violations),
  ).toEqual([])
}
```

jsdom does not calculate rendered color or layout. Those two rules stay disabled rather than producing misleading results, and `setupTests.ts` silences axe’s canvas capability probe. Contrast and link distinction remain browser and manual checks.

The formatter includes rule IDs, help text, selectors, and failure summaries so a CI failure identifies the element that needs attention.

## Scan meaningful rendered states

An empty initial component misses the states where accessibility defects appear. Part 23 scans:

- the ready schedule with named event list and cards;
- the service form after invalid submission;
- the assignment board after an optimistic assignment and confirmed status.

The axe API checks rendered content, so tests must first expose errors, load data, or activate the workflow being reviewed.

Automation catches many missing labels, broken ARIA relationships, duplicate IDs, and structural problems. It cannot determine whether the selected focus destination is useful. Keep direct assertions too:

```tsx
await user.click(screen.getByRole('button', { name: 'Assign' }))

await waitFor(() =>
  expect(
    screen.getByRole('listitem', { name: 'Sound operator' }),
  ).toHaveFocus(),
)
```

The form tests similarly assert the first invalid field and its description ID. These behavior checks protect decisions an automated rules engine cannot infer.

## Complete the manual accessibility pass

Automated scans are regression tools, not a conformance certificate. Test the built application manually:

1. Complete the sign-in and create-event forms using only the keyboard.
2. Submit each form with multiple invalid fields; confirm the first field receives focus and every error is readable and associated.
3. Trigger an API field error and confirm the entered values remain while focus reaches the affected field.
4. Assign a volunteer and confirm focus moves from the removed button to the updated position row.
5. Listen for Saving, success, conflict, and failure behavior with a screen reader; confirm announcements are informative without being repetitive.
6. Zoom to 200 and 400 percent and confirm focused controls and errors remain visible.
7. Check focus-indicator and text contrast in a real browser; jsdom cannot measure rendered colors.
8. Repeat the main journey with at least one screen reader and inspect landmarks, headings, lists, fields, buttons, links, and status messages.

## Verify Part 23

Run the complete checkpoint:

```bash
npm run verify
npm run test:coverage
dotnet test server/ChurchServe.Api.Tests/ChurchServe.Api.Tests.csproj --configuration Release
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project server/ChurchServe.Api --no-build
```

Part 23 passes the architecture rule, linter, strict TypeScript checks, production Vite build, 120 React tests, 20 API integration tests, and EF Core model-drift check. Statement coverage is 90.83 percent.

## Result

ChurchServe now gives invalid forms a deterministic recovery point, keeps error relationships programmatically available, preserves focus across optimistic replacement, announces asynchronous outcomes, and scans representative states for common accessibility regressions.

Part 24 can add browser-level end-to-end tests on top of an interaction model that now has explicit keyboard and assistive-technology contracts.
