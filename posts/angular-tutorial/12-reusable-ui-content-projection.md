---
title: "ChurchServe Angular Tutorial Part 12: Reusable UI and Content Projection"
excerpt: "Extract accessible Angular panels and empty states with named content projection while keeping ChurchServe business behavior inside its feature components."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular content projection reusable components"
  description: "Create reusable Angular UI components with named content projection, focused APIs, accessible labels, deliberate variants, and host tests."
  socialTitle: "ChurchServe Part 12: Reusable Angular UI"
  socialDescription: "Build accessible reusable panels and empty states without hiding ChurchServe business behavior inside generic components."
---

# ChurchServe Angular Tutorial Part 12: Reusable UI and Content Projection

Part 11 made the schedule URL-driven and paginated. At this point, the Schedule and Volunteers pages both contain bordered sections, headings, state messages, and actions. Copying those patterns makes later accessibility and styling changes inconsistent. Moving too much into a generic component is not better: it hides feature language and behavior behind a growing list of inputs.

Part 12 extracts two stable presentation patterns—a panel and an empty state—and uses named content projection so each feature still supplies its meaningful content.

> **Quick answer:** Use content projection when a reusable component should own structure and styling while its caller owns the words, controls, and behavior. Keep the API small, generate accessible relationships inside the shared component, and test it through a realistic host rather than snapshots.

## What Changes in Part 12

This part adds a small shared UI area:

```text
src/app/
  shared/
    ui/
      empty-state/
        empty-state.html
        empty-state.scss
        empty-state.spec.ts
        empty-state.ts
      panel/
        panel.html
        panel.scss
        panel.spec.ts
        panel.ts
  features/
    schedule/
      schedule.html
      schedule.scss
      schedule.spec.ts
      schedule.ts
    volunteers/
      volunteers.html
      volunteers.scss
      volunteers.ts
```

The visible changes are modest but useful:

- Schedule filters use the same panel structure as volunteer search and results.
- Empty schedule results use a reusable state with an optional “Show all ministries” action.
- Empty volunteer search results use the same accessible state structure with different language.
- Each rendered component receives a unique heading ID and an `aria-labelledby` relationship.
- Feature components continue to own navigation, search, retry, and data-loading decisions.

No API or database change is needed in Part 12.

## Extract a Panel, Not a Page Framework

Create `src/app/shared/ui/panel/panel.ts`:

```typescript
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let nextPanelId = 0;

@Component({
  selector: 'app-panel',
  templateUrl: './panel.html',
  styleUrl: './panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Panel {
  readonly tone = input<'default' | 'subtle'>('default');
  protected readonly headingId = `panel-heading-${nextPanelId++}`;
}
```

The public API has one styling decision: `default` or `subtle`. It does not expose padding, border color, shadow size, or heading size. Those values belong to the design component, not every caller.

The module-level counter creates a unique ID for every instance without requiring the caller to coordinate IDs. In a server-rendered application, use an ID strategy that remains stable across server rendering and hydration; ChurchServe is currently client-rendered.

Add `panel.html`:

```html
<section
  class="panel"
  [class.panel--subtle]="tone() === 'subtle'"
  [attr.aria-labelledby]="headingId"
>
  <header class="panel__header">
    <h2 [id]="headingId"><ng-content select="[panel-title]" /></h2>
    <div class="panel__actions"><ng-content select="[panel-actions]" /></div>
  </header>
  <div class="panel__body"><ng-content /></div>
</section>
```

There are three projection destinations:

- `[panel-title]` supplies the visible heading text.
- `[panel-actions]` supplies an optional feature-owned action.
- Unmatched content goes into the body.

The panel owns the `section`, `header`, `h2`, and accessible label relationship. The caller cannot accidentally point `aria-labelledby` at a missing ID.

Add `panel.scss`:

```scss
:host {
  display: block;
}

.panel {
  padding: clamp(1.25rem, 4vw, 2rem);
  border: 1px solid #d7e2e0;
  border-radius: 1rem;
  background: #fff;
  box-shadow: 0 1rem 2.5rem rgb(16 42 67 / 7%);
}

.panel--subtle {
  border-color: #d9e2ec;
  background: #f8fafc;
  box-shadow: none;
}

.panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1.25rem;
  gap: 1rem;
}

h2 {
  margin: 0;
  color: #102a43;
  font-size: 1.35rem;
}

.panel__actions:empty {
  display: none;
}
```

Styles that define the shared surface moved out of the Volunteers feature. Styles for the search input and volunteer cards remain there because they are not panel responsibilities.

## Build a Reusable Empty State

An empty state also owns a stable semantic relationship, but its message and recovery action vary by feature.

Create `empty-state.ts`:

```typescript
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let nextEmptyStateId = 0;

@Component({
  selector: 'app-empty-state',
  templateUrl: './empty-state.html',
  styleUrl: './empty-state.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly tone = input<'neutral' | 'warning'>('neutral');
  protected readonly headingId = `empty-state-heading-${nextEmptyStateId++}`;
}
```

The closed `tone` union documents the supported design variants and lets Angular catch invalid template values. A collection of unrelated CSS inputs would make visual consistency the caller's problem again.

Add `empty-state.html`:

```html
<section
  class="empty-state"
  [class.empty-state--warning]="tone() === 'warning'"
  [attr.aria-labelledby]="headingId"
>
  <h2 [id]="headingId"><ng-content select="[empty-title]" /></h2>
  <div class="empty-state__body"><ng-content /></div>
  <div class="empty-state__actions"><ng-content select="[empty-actions]" /></div>
</section>
```

Add `empty-state.scss`:

```scss
:host {
  display: block;
}

.empty-state {
  padding: 1.5rem;
  border: 1px solid #bcccdc;
  border-radius: 0.85rem;
  background: #f6f8fa;
  color: #334e68;
  text-align: center;
}

.empty-state--warning {
  border-color: #e6a5a5;
  background: #fff5f5;
  color: #7f1d1d;
}

h2 {
  margin: 0;
  color: #102a43;
  font-size: 1.35rem;
}

.empty-state--warning h2 {
  color: #7f1d1d;
}

.empty-state__body {
  margin-top: 0.4rem;
  line-height: 1.6;
}

.empty-state__actions {
  margin-top: 1rem;
}

.empty-state__actions:empty {
  display: none;
  margin: 0;
}
```

The component does not accept a callback input. A projected button remains normal Angular template code in the feature that understands the action.

## Use the Panel on the Schedule Page

Import the two shared components into `Schedule`:

```typescript
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Panel } from '../../shared/ui/panel/panel';

@Component({
  selector: 'app-schedule',
  imports: [RouterLink, ServiceEventCard, EmptyState, Panel],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Schedule {
  // Existing Part 11 query and request state remains here.
}
```

Replace the schedule's hand-built filter section with a subtle panel:

```html
<app-panel class="schedule-filters" tone="subtle">
  <span panel-title>Filter schedule</span>
  <div class="schedule-filter-fields">
    <label>
      Month
      <input
        type="month"
        [value]="query().month"
        (change)="changeMonth($any($event.target).value)"
      />
    </label>

    <label>
      Ministry
      <select
        [value]="query().ministry ?? ''"
        (change)="changeMinistry($any($event.target).value)"
      >
        <option value="">All ministries</option>
        @for (ministry of ministries; track ministry.value) {
          <option [value]="ministry.value">{{ ministry.label }}</option>
        }
      </select>
    </label>
  </div>
</app-panel>
```

The panel knows nothing about months, ministries, router state, or page resets. Those are Schedule responsibilities from Part 11.

## Give Empty Schedule Results a Useful Action

Replace the repeated empty-state section inside the event grid:

```html
@for (event of events(); track event.id) {
  <app-service-event-card [event]="event" />
} @empty {
  <app-empty-state>
    <span empty-title>No matching services</span>
    <p>Try a different month or ministry.</p>
    @if (query().ministry) {
      <button empty-actions type="button" (click)="showAllMinistries()">
        Show all ministries
      </button>
    }
  </app-empty-state>
}
```

The recovery method delegates to the existing feature behavior:

```typescript
protected showAllMinistries(): void {
  this.changeMinistry('');
}
```

That detail matters. The shared empty state does not clear filters itself. `Schedule.changeMinistry` still updates the router, resets the page, and lets the existing query stream load new data.

The action is shown only when a ministry filter is active. Without that condition, “Show all ministries” would claim to change a view that is already showing all ministries.

## Reuse the Same Structure for Volunteer Search

Import `Panel` and `EmptyState` into the Volunteers component without changing its RxJS pipeline:

```typescript
@Component({
  selector: 'app-volunteers',
  imports: [AsyncPipe, ReactiveFormsModule, EmptyState, Panel],
  templateUrl: './volunteers.html',
  styleUrl: './volunteers.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Volunteers {
  // Existing debounce, cancellation, retry, and request states stay here.
}
```

The search form and results now share the panel contract:

```html
<app-panel>
  <span panel-title>Volunteer directory</span>
  <label for="volunteer-query">Name</label>
  <input
    id="volunteer-query"
    type="search"
    autocomplete="off"
    [formControl]="query"
    aria-describedby="volunteer-search-hint"
    placeholder="Try Anna or Morgan"
  />
  <p id="volunteer-search-hint" class="hint">Enter at least two characters.</p>
</app-panel>

@if (results$ | async; as state) {
  <app-panel>
    <span panel-title>Results</span>
    <!-- Existing request-state switch remains here. -->
  </app-panel>
}
```

Only the loaded-with-no-results branch changes its presentation:

```html
@if (state.volunteers.length === 0) {
  <app-empty-state>
    <span empty-title>No matching volunteers</span>
    <p>Try a different name or a broader search.</p>
  </app-empty-state>
} @else {
  <!-- Existing volunteer list. -->
}
```

The Schedule and Volunteers pages now share markup quality without sharing business vocabulary.

## Understand Projection and Style Ownership

Angular renders projected elements as content owned by the caller. With emulated view encapsulation, a feature stylesheet can continue to style its own projected controls. For example, the Schedule stylesheet owns the green “Show all ministries” button, while `EmptyState` owns the spacing around the action slot.

This is a useful division:

- Shared component styles define the wrapper and its layout.
- Feature styles define domain-specific controls and lists.
- Global styles remain reserved for truly application-wide rules.

Do not use projected HTML strings. Content projection preserves template type checking, bindings, event handlers, and Angular's normal escaping behavior.

## Test Components Through Realistic Hosts

A projection test needs a host component because projection behavior lives at the call site. The panel test renders two instances:

```typescript
@Component({
  imports: [Panel],
  template: `
    <app-panel>
      <span panel-title>Open positions</span>
      <button panel-actions type="button">Assign</button>
      <p>Two positions remain.</p>
    </app-panel>
    <app-panel tone="subtle">
      <span panel-title>Filters</span>
      <p>Filter controls</p>
    </app-panel>
  `,
})
class TestHost {}
```

Assertions verify content, semantics, IDs, and the closed variant:

```typescript
it('projects labelled headings, actions, and body content', () => {
  const fixture = TestBed.createComponent(TestHost);
  fixture.detectChanges();
  const panels = fixture.nativeElement.querySelectorAll('section') as NodeListOf<HTMLElement>;

  expect(panels[0].textContent).toContain('Open positions');
  expect(panels[0].textContent).toContain('Assign');
  expect(panels[0].textContent).toContain('Two positions remain.');
  expect(panels[0].getAttribute('aria-labelledby')).toBe(
    panels[0].querySelector('h2')?.id,
  );
});

it('assigns unique heading IDs and supports the deliberate subtle tone', () => {
  const fixture = TestBed.createComponent(TestHost);
  fixture.detectChanges();
  const panels = fixture.nativeElement.querySelectorAll('section') as NodeListOf<HTMLElement>;

  expect(panels[0].getAttribute('aria-labelledby')).not.toBe(
    panels[1].getAttribute('aria-labelledby'),
  );
  expect(panels[1].classList).toContain('panel--subtle');
});
```

The empty-state host projects a working feature action:

```typescript
@Component({
  imports: [EmptyState],
  template: `
    <app-empty-state>
      <span empty-title>No matching services</span>
      <p>Try another ministry.</p>
      <button empty-actions type="button" (click)="clear()">Show all ministries</button>
    </app-empty-state>
  `,
})
class TestHost {
  readonly clear = vi.fn();
}
```

Its test clicks the projected button and verifies that the caller's method runs. A Schedule integration test goes one step further: it starts with `ministry=technical`, renders an empty page, clicks “Show all ministries,” and verifies that the router removes the ministry parameter while keeping page 1.

These tests are more valuable than a large HTML snapshot. They protect the contract that consumers actually rely on.

## Why Part 12 Does Not Add a Confirmation Dialog

A generic confirmation dialog may sound reusable, but ChurchServe does not yet have a delete or other destructive workflow. Adding an unused dialog would force us to invent its caller, focus policy, and result contract without real requirements.

When a destructive workflow arrives, a production dialog must cover more than appearance:

- an accessible name and description;
- initial focus placement;
- focus containment while open;
- Escape behavior;
- background interaction management;
- focus return to the control that opened it;
- keyboard and assistive-technology tests.

At that point, use the native `dialog` element with carefully tested behavior or a proven accessible dialog primitive. A styled `div` with `role="dialog"` is not a complete modal. Deferring it here keeps Part 12's abstractions driven by actual usage.

## Run and Verify Part 12

Run the full checks from the ChurchServe repository root:

```bash
npm test -- --watch=false
npm run build
dotnet test ChurchServe.slnx -c Release --no-restore
```

Part 12 passes 54 Angular tests across 16 test files, including the new projection and caller-action tests. The Angular production build passes strict TypeScript and template compilation, and all 14 API integration tests continue to pass.

## A Useful Boundary

Reusable UI should make correct structure easier without erasing application language. `Panel` and `EmptyState` own semantics and layout; Schedule and Volunteers still own routes, requests, data, and decisions.

Part 13 can introduce authentication and session state on top of this boundary. Shared presentation may decide how signed-in information looks, but real authorization must remain enforced by the server.
