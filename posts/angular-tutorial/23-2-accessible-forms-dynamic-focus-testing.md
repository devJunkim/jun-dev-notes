---
title: "ChurchServe Angular Tutorial Part 23-2: Accessible Forms, Dynamic Focus, and Testing"
excerpt: "Connect Angular validation errors to fields, preserve focus during optimistic assignments, announce status changes, and add automated accessibility scans."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular accessible form validation"
  description: "Build accessible Angular forms and dynamic workflows with associated errors, predictable focus, live status, axe-core scans, and keyboard tests."
  socialTitle: "ChurchServe Part 23-2: Accessible Forms and Testing"
  socialDescription: "Keep validation and optimistic volunteer assignments understandable for keyboard and screen-reader users."
---

# ChurchServe Angular Tutorial Part 23-2: Accessible Forms, Dynamic Focus, and Testing

Part 23-1 gave ChurchServe a clear document structure and predictable focus after route navigation. Dynamic workflows need another layer of care. A visible validation message may not be connected to its field, and an optimistic update can remove the focused button before a keyboard user learns what changed.

This second half of Part 23 makes validation and volunteer assignment preserve context, then adds automated accessibility checks without pretending that automation replaces keyboard and screen-reader testing.

> **Quick answer:** Expose invalid state and descriptions on the affected controls, focus the first invalid field after Angular renders its errors, move focus to a stable row when an optimistic assignment replaces its button, announce results with restrained status regions, and run axe against meaningful component states.

## What Part 23-2 Changes

```text
src/app/
  core/forms/
    focus-first-invalid-control.ts       # new
    focus-first-invalid-control.spec.ts  # new
  features/
    schedule/
      schedule.spec.ts
      service-event-detail/
        service-event-detail.html
        service-event-detail.scss
        service-event-detail.spec.ts
        service-event-detail.ts
      service-event-form/
        service-event-form.html
        service-event-form.spec.ts
        service-event-form.ts
    sign-in/
      sign-in.html
      sign-in.spec.ts
      sign-in.ts
  testing/
    expect-no-accessibility-violations.ts  # new
package.json
package-lock.json
README.md
```

The API contract and database remain unchanged. This article improves the Angular interaction contract and its regression tests.

## Focus the First Invalid Control

Create `src/app/core/forms/focus-first-invalid-control.ts`:

```typescript
export function focusFirstInvalidControl(root: HTMLElement): void {
  root.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
}
```

Keeping this behavior in one function prevents the create-event and sign-in forms from developing different focus rules. Its unit test verifies that the first invalid control wins:

```typescript
import { focusFirstInvalidControl } from './focus-first-invalid-control';

describe('focusFirstInvalidControl', () => {
  it('focuses the first control exposed as invalid', () => {
    const root = document.createElement('form');
    root.innerHTML = `
      <input id="valid-control">
      <input id="first-invalid" aria-invalid="true">
      <input id="second-invalid" aria-invalid="true">
    `;
    document.body.append(root);

    focusFirstInvalidControl(root);

    expect(document.activeElement?.id).toBe('first-invalid');
    root.remove();
  });
});
```

The helper searches within the component host instead of the complete document, so another form elsewhere cannot capture focus.

## Expose Only Errors That Actually Exist

An `aria-describedby` reference should point to content currently present in the DOM. Add methods to `ServiceEventForm` that derive visible error state from touched and invalid controls:

```typescript
protected titleErrorVisible(): boolean {
  return this.form.controls.title.touched &&
    this.form.controls.title.invalid;
}

protected startsAtErrorVisible(): boolean {
  return this.form.controls.startsAt.touched &&
    this.form.controls.startsAt.invalid;
}

protected endsAtErrorVisible(): boolean {
  return this.form.controls.endsAt.touched &&
    this.form.controls.endsAt.invalid;
}

protected locationErrorVisible(): boolean {
  return this.form.controls.location.touched &&
    this.form.controls.location.invalid;
}

protected timeRangeErrorVisible(): boolean {
  return (
    (this.form.controls.startsAt.touched ||
      this.form.controls.endsAt.touched) &&
    this.form.hasError('timeRange')
  );
}
```

The date fields can have help text, a field-specific error, and a shared range error. Build the description list from only the active elements:

```typescript
protected startsAtDescriptionIds(): string {
  return [
    'service-time-zone',
    this.startsAtErrorVisible() ? 'service-start-error' : '',
    this.timeRangeErrorVisible() ? 'service-time-error' : '',
  ]
    .filter(Boolean)
    .join(' ');
}

protected endsAtDescriptionIds(): string {
  return [
    'service-time-zone',
    this.endsAtErrorVisible() ? 'service-end-error' : '',
    this.timeRangeErrorVisible() ? 'service-time-error' : '',
  ]
    .filter(Boolean)
    .join(' ');
}
```

Use those values in `service-event-form.html`:

```html
<label for="service-start">Starts</label>
<input
  id="service-start"
  type="datetime-local"
  formControlName="startsAt"
  [attr.aria-describedby]="startsAtDescriptionIds()"
  [attr.aria-invalid]="
    startsAtErrorVisible() || timeRangeErrorVisible() ? 'true' : null
  "
/>

@if (startsAtErrorVisible()) {
  <p id="service-start-error" class="field-error">
    {{
      form.controls.startsAt.getError('server') ??
        'Enter a valid start date and time.'
    }}
  </p>
}

<p id="service-time-zone" class="field-help field-wide">
  Times are interpreted in this device's time zone: {{ deviceTimeZone }}.
</p>

@if (timeRangeErrorVisible()) {
  <p id="service-time-error" class="field-error field-wide" role="alert">
    End time must be after start time.
  </p>
}
```

Apply the same pattern to service name, end time, and location. Use `null` when an optional ARIA attribute should be removed:

```html
<input
  id="service-title"
  type="text"
  formControlName="title"
  maxlength="120"
  autocomplete="off"
  [attr.aria-describedby]="titleErrorVisible() ? 'service-title-error' : null"
  [attr.aria-invalid]="titleErrorVisible() ? 'true' : null"
/>
```

## Move Focus after Validation Renders

Inject the component host and schedule focus after Angular has rendered `aria-invalid` and the error messages:

```typescript
private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

private scheduleInvalidFocus(): void {
  this.host.nativeElement.ownerDocument.defaultView?.setTimeout(() =>
    focusFirstInvalidControl(this.host.nativeElement),
  );
}
```

Call it after an invalid submit:

```typescript
submit(): void {
  this.form.markAllAsTouched();
  if (this.form.invalid) {
    this.scheduleInvalidFocus();
    return;
  }

  const value = this.form.getRawValue();
  this.submitted.emit({
    title: value.title.trim(),
    startsAt: new Date(value.startsAt).toISOString(),
    endsAt: new Date(value.endsAt).toISOString(),
    location: value.location.trim(),
  });
}
```

Also call it after `applyServerErrors()` marks fields as touched. Client validation and API validation now lead users to the same recovery point.

The sign-in form uses the same behavior. First separate duplicate submission from invalid submission:

```typescript
protected signIn(): void {
  if (this.isSubmitting()) {
    return;
  }

  if (this.form.invalid) {
    this.form.markAllAsTouched();
    this.host.nativeElement.ownerDocument.defaultView?.setTimeout(() =>
      focusFirstInvalidControl(this.host.nativeElement),
    );
    return;
  }

  // Continue with the existing sign-in request.
}
```

Then connect each rendered message to its input:

```html
<input
  id="email"
  type="email"
  autocomplete="username"
  formControlName="email"
  [attr.aria-describedby]="
    form.controls.email.touched && form.controls.email.invalid
      ? 'email-error'
      : null
  "
  [attr.aria-invalid]="
    form.controls.email.touched && form.controls.email.invalid
      ? 'true'
      : null
  "
/>
@if (form.controls.email.touched && form.controls.email.invalid) {
  <p id="email-error" class="field-error">Enter a valid email address.</p>
}
```

## Preserve Focus after an Optimistic Assignment

The assignment store adds a pending assignment immediately. That replaces the select and Assign button with the assigned-volunteer summary. Without an explicit handoff, keyboard focus can fall back to the document body.

Make each position row programmatically focusable and give it a stable accessible name:

```html
<li
  [id]="'position-' + position.id"
  tabindex="-1"
  [attr.aria-labelledby]="'position-heading-' + position.id"
>
  <div class="position-copy">
    <span class="ministry">{{ position.ministry }}</span>
    <h3 [id]="'position-heading-' + position.id">
      {{ position.roleName }}
    </h3>
    <p>
      {{ position.startsAt | date: 'shortTime' }}–{{
        position.endsAt | date: 'shortTime'
      }}
    </p>
  </div>

  <!-- Existing assignment or assignment controls -->
</li>
```

The row uses `tabindex="-1"`, so it is available to code without becoming another ordinary Tab stop.

After starting the assignment, wait for the optimistic render and focus that row:

```typescript
protected assign(positionId: string): void {
  const volunteerId = this.selectedVolunteer(positionId);
  if (volunteerId) {
    this.assignmentStore.assign(positionId, volunteerId);
    this.selectedVolunteerIds.update((current) => ({
      ...current,
      [positionId]: '',
    }));

    this.host.nativeElement.ownerDocument.defaultView?.setTimeout(() => {
      this.host.nativeElement.ownerDocument
        .getElementById(`position-${positionId}`)
        ?.focus();
    });
  }
}
```

Give that programmatic target the visible focus style introduced in Part 23-1:

```scss
.position-list li:focus-visible {
  outline: 3px solid #005a9c;
  outline-offset: 3px;
}
```

The assignment test clicks the real button and verifies the replacement state and focus destination:

```typescript
button.click();
fixture.detectChanges();
await new Promise((resolve) => setTimeout(resolve));

expect(fixture.nativeElement.textContent).toContain('Saving…');
expect(document.activeElement?.id).toBe('position-sound-1');
```

## Announce Results without Excess Noise

Keep the assignment notice polite and atomic:

```html
@if (assignmentStore.notice(); as notice) {
  <p class="notice" role="status" aria-atomic="true">{{ notice }}</p>
}
```

The row focus identifies where the change occurred. The status communicates the result of the asynchronous operation. Keep `role="alert"` for failures that need immediate attention; routine loading and success messages should not interrupt the user.

## Add Automated Accessibility Scans

Install axe-core as a development dependency:

```bash
npm install --save-dev axe-core
```

Create `src/testing/expect-no-accessibility-violations.ts`:

```typescript
import axe from 'axe-core';

export async function expectNoAccessibilityViolations(
  root: Element,
): Promise<void> {
  const result = await axe.run(root, {
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
      // jsdom has no canvas implementation, so rendered color remains a browser/manual check.
      'color-contrast': { enabled: false },
      'link-in-text-block': { enabled: false },
    },
  });

  expect(
    result.violations,
    formatViolations(result.violations),
  ).toEqual([]);
}

function formatViolations(violations: axe.Result[]): string {
  return violations
    .map(
      (violation) =>
        `${violation.id}: ${violation.help}\n${violation.nodes
          .map(
            (node) =>
              `  ${node.target.join(' ')}: ${node.failureSummary ?? ''}`,
          )
          .join('\n')}`,
    )
    .join('\n\n');
}
```

Run the helper against meaningful states rather than only pristine initial forms:

```typescript
it('has no automatically detectable accessibility violations after validation', async () => {
  const fixture = TestBed.createComponent(ServiceEventForm);
  fixture.detectChanges();

  fixture.componentInstance.submit();
  fixture.detectChanges();
  await waitForScheduledFocus();

  await expectNoAccessibilityViolations(fixture.nativeElement);
});
```

Part 23 scans the ready schedule, assignment board, invalid create-event form, and invalid sign-in form. The jsdom runner disables the rules that require rendered color and a real canvas implementation. Contrast and link distinction remain browser checks rather than false automated successes.

## Keep Direct Focus Assertions

An axe scan cannot decide whether a workflow chose the right focus destination. Preserve direct interaction tests:

```typescript
it('does not emit an invalid command, reveals errors, and focuses the first field', async () => {
  const fixture = TestBed.createComponent(ServiceEventForm);
  const emitted: CreateServiceEventCommand[] = [];
  fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));

  fixture.componentInstance.submit();
  fixture.detectChanges();
  await waitForScheduledFocus();

  expect(emitted).toEqual([]);
  expect(fixture.componentInstance.form.controls.title.touched).toBe(true);
  expect(document.activeElement?.id).toBe('service-title');
  expect(document.activeElement?.getAttribute('aria-describedby'))
    .toBe('service-title-error');
});
```

Test server-returned field errors as well. The assertion should verify that the affected field receives focus and that unknown server field names are not displayed.

## Complete the Manual Accessibility Pass

Automated rules catch missing labels, invalid ARIA relationships, duplicate IDs, and many structural mistakes. They cannot judge whether the complete workflow is understandable.

1. Complete event creation, sign-in, and volunteer assignment using only the keyboard.
2. Submit invalid forms and confirm the first invalid field receives focus.
3. With a screen reader, confirm the focused field announces its label, value, invalid state, and visible error.
4. Assign a volunteer and confirm focus remains on the updated position while saving and confirmation status are announced without repetition.
5. Verify every programmatically focused element has a visible outline.
6. Zoom to 200% and 400% and confirm content reflows without losing controls or messages.
7. Check text, focus, error, and link contrast in a real browser because the jsdom scan cannot measure rendered colors.
8. Repeat the core journey with at least one screen reader and inspect headings, landmarks, links, buttons, fields, lists, and status regions.

Keyboard testing must operate the actual controls. Calling a component method directly is useful for unit isolation, but it does not prove that a control is reachable or activates correctly.

## Verify Part 23-2

Run the complete Part 23 verification set:

```bash
dotnet test ChurchServe.slnx
npm run test:ci
npm run test:coverage
npm run check:architecture
npm run build
```

The API tests confirm that the unchanged server contract remains healthy. Angular tests protect validation relationships, first-invalid focus, route focus, semantic schedule lists, assignment focus preservation, and axe scans of representative rendered states.

Part 24 can now add end-to-end coverage on top of interaction behavior that has an explicit accessible contract.
