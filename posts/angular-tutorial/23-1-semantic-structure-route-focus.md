---
title: "ChurchServe Angular Tutorial Part 23-1: Semantic Structure and Route Focus"
excerpt: "Give ChurchServe one clear main landmark, semantic schedule lists, predictable focus after Angular navigation, visible focus indicators, and reduced-motion support."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular route focus accessibility"
  description: "Improve Angular accessibility with semantic landmarks, schedule lists, route-aware focus management, visible focus indicators, and reduced motion."
  socialTitle: "ChurchServe Part 23-1: Semantic Structure and Route Focus"
  socialDescription: "Make Angular navigation and schedule structure predictable for keyboard and screen-reader users."
---

# ChurchServe Angular Tutorial Part 23-1: Semantic Structure and Route Focus

ChurchServe already uses native links and buttons, but a single-page application can still be disorienting. A routed view may introduce another `main` landmark inside the application shell, a visual card grid may expose no collection semantics, and client-side navigation may leave keyboard focus on an element that no longer exists.

This first half of Part 23 fixes the page structure and navigation experience. Part 23-2 applies the same accessibility discipline to validation, optimistic assignments, status messages, and automated checks.

> **Quick answer:** Keep one page-level `main`, use sections and lists to describe feature content, move focus to the destination heading after a route-path change, leave focus alone for query-only filtering, and provide strong focus indicators while respecting reduced-motion preferences.

## What Part 23-1 Changes

```text
src/app/
  core/routing/
    route-focus.ts                       # new
    route-focus.spec.ts                  # new
  features/
    navigation-unavailable/
      navigation-unavailable.html
      navigation-unavailable.scss
    not-found/
      not-found.html
      not-found.scss
    schedule/
      schedule.html
      schedule.scss
      schedule.spec.ts
      service-event-card/service-event-card.scss
      service-event-create/
        service-event-create.html
        service-event-create.scss
      service-event-detail/
        service-event-detail.html
        service-event-detail.scss
      service-event-form/service-event-form.scss
    sign-in/
      sign-in.html
      sign-in.scss
    volunteers/
      volunteers.html
      volunteers.scss
  app.html
  app.scss
  app.ts
src/styles.scss
```

The API and database do not change. This article changes only Angular document structure, focus behavior, and presentation.

## Keep One Main Landmark

The application shell already owns the page-level `main`. Apply the new route-focus directive there:

```html
<main id="main-content" appRouteFocus tabindex="-1">
  <router-outlet />
</main>
```

Import the standalone directive in `app.ts` and add it to the component imports:

```typescript
import { RouteFocus } from './core/routing/route-focus';

@Component({
  imports: [RouteFocus, RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  // Keep the existing session, navigation, and sign-out behavior.
}
```

Feature templates should not create nested `main` landmarks. Change their outer elements to labelled sections instead:

```html
<section class="volunteer-page" aria-labelledby="volunteers-heading">
  <header>
    <p class="eyebrow">Volunteers</p>
    <h1 id="volunteers-heading">Find the right person to serve</h1>
  </header>

  <!-- Existing volunteer search and results -->
</section>
```

Apply the same replacement to the sign-in, create-event, event-detail, navigation-unavailable, and not-found views. Keep their existing heading IDs and use those IDs from `aria-labelledby`.

The result is one main content region owned by the application shell. Routed feature content still has a meaningful section and heading without competing in the screen reader's landmark list.

## Represent the Schedule as a List

CSS Grid controls visual layout; it does not describe the relationship between the items. The schedule is a collection of service events, so render the grid as a list:

```html
<ul class="event-grid" aria-label="Service events">
  @for (event of store.events(); track event.id) {
    <li><app-service-event-card [event]="event" /></li>
  } @empty {
    <li class="event-grid-empty">
      <app-empty-state>
        <span empty-title>No matching services</span>
        <p>Try a different month or ministry.</p>
        @if (query().ministry) {
          <button empty-actions type="button" (click)="showAllMinistries()">
            Show all ministries
          </button>
        }
      </app-empty-state>
    </li>
  }
</ul>
```

Keep the existing service-card link as the navigation control. Do not make the whole card a clickable `div`; native links already provide focus, keyboard activation, context-menu behavior, and an accessible role.

Reset the browser list presentation without removing the semantics:

```scss
.event-grid {
  display: grid;
  margin: 2.5rem 0 0;
  padding: 0;
  gap: 1rem;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
  list-style: none;
}

.event-grid > li {
  min-width: 0;
}

.event-grid-empty {
  grid-column: 1 / -1;
}
```

Protect this relationship with a focused assertion in `schedule.spec.ts`:

```typescript
expect(element.querySelectorAll('app-service-event-card')).toHaveLength(2);
expect(element.querySelectorAll('.event-grid > li')).toHaveLength(2);
```

## Move Focus after Angular Navigation

A traditional document navigation moves browser and assistive-technology context to the new page. Angular replaces routed content in place, so keyboard focus can remain on a link that has just been removed.

Add `src/app/core/routing/route-focus.ts`:

```typescript
import { DOCUMENT } from '@angular/common';
import { DestroyRef, Directive, ElementRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { distinctUntilChanged, filter, map } from 'rxjs';

@Directive({
  selector: '[appRouteFocus]',
})
export class RouteFocus {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly browserWindow = inject(DOCUMENT).defaultView;
  private pendingFocus?: number;

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd =>
          event instanceof NavigationEnd,
        ),
        map((event) => pathOnly(event.urlAfterRedirects)),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.scheduleFocus());

    this.destroyRef.onDestroy(() => {
      if (this.pendingFocus !== undefined) {
        this.browserWindow?.clearTimeout(this.pendingFocus);
      }
    });
  }

  private scheduleFocus(): void {
    if (this.pendingFocus !== undefined) {
      this.browserWindow?.clearTimeout(this.pendingFocus);
    }

    this.pendingFocus = this.browserWindow?.setTimeout(() => {
      const target =
        this.host.nativeElement.querySelector<HTMLElement>('h1') ??
        this.host.nativeElement;
      if (!target.hasAttribute('tabindex')) {
        target.setAttribute('tabindex', '-1');
      }
      target.focus();
      this.pendingFocus = undefined;
    });
  }
}

function pathOnly(url: string): string {
  return url.split(/[?#]/, 1)[0];
}
```

The directive waits one task so Angular can render the destination heading. It then focuses the first `h1`, falling back to the shell's `main`. `tabindex="-1"` allows programmatic focus without adding the heading to the ordinary Tab sequence.

The directive cancels a pending timeout when another navigation arrives or the directive is destroyed. That prevents a stale callback from moving focus after a newer navigation.

## Do Not Steal Focus during Filtering

The schedule writes month, ministry, and page state to query parameters. Those updates keep the user on `/schedule`, and focus should remain on the filter or pagination control they just used.

`pathOnly()` removes the query string and fragment before `distinctUntilChanged()` compares navigation events. Therefore:

- `/schedule` to `/schedule/event-1` moves focus to the new heading.
- `/schedule?month=2026-11` to `/schedule?month=2026-12` does not move focus.
- A redirect uses `urlAfterRedirects`, so focus follows the route the user actually sees.

Test both parts of the contract in `route-focus.spec.ts`:

```typescript
it('moves focus to the page heading after path navigation', async () => {
  const fixture = TestBed.createComponent(RouteFocusHost);
  const router = TestBed.inject(Router);
  fixture.detectChanges();

  await router.navigateByUrl('/schedule');
  fixture.detectChanges();
  await waitForScheduledFocus();

  expect(document.activeElement?.tagName).toBe('H1');
  expect(document.activeElement?.textContent).toContain('Schedule');
  expect(document.activeElement?.getAttribute('tabindex')).toBe('-1');
});

it('does not steal focus when only query parameters change', async () => {
  const fixture = TestBed.createComponent(RouteFocusHost);
  const router = TestBed.inject(Router);
  fixture.detectChanges();
  await router.navigateByUrl('/schedule?month=2026-11');
  fixture.detectChanges();
  await waitForScheduledFocus();

  const button = fixture.nativeElement.querySelector('button');
  button.focus();

  await router.navigateByUrl('/schedule?month=2026-12');
  fixture.detectChanges();
  await waitForScheduledFocus();

  expect(document.activeElement).toBe(button);
});
```

The first test confirms the focus handoff. The second protects the equally important rule that background or in-place updates must not move focus unexpectedly.

## Make Keyboard Focus Easy to See

Use one dark focus color against ChurchServe's light surfaces:

```scss
:where(a, button, input, select, textarea, [tabindex]):focus-visible {
  outline: 3px solid #005a9c;
  outline-offset: 3px;
}
```

Existing component-level focus selectors have greater specificity than the global `:where()` rule. Update their outline color to the same `#005a9c` value in the application shell, schedule, service card, service form, sign-in, volunteers, create-event, unavailable-navigation, and not-found styles.

Do not remove outlines to make mouse clicks look cleaner. `:focus-visible` lets the browser show the indicator when the interaction modality needs it.

Part 23-2 programmatically focuses an updated assignment row. Give that row the same visible treatment:

```scss
.position-list li:focus-visible {
  outline: 3px solid #005a9c;
  outline-offset: 3px;
}
```

## Respect Reduced-Motion Preferences

ChurchServe does not currently depend on large animations, but a global guard protects future transitions:

```scss
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    scroll-behavior: auto !important;
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Also add a reusable utility for content that needs to remain available to assistive technology without being visually displayed:

```scss
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
```

Do not use `display: none` for accessible names or live messages; it removes the content from the accessibility tree as well as the visual layout.

## Verify Part 23-1

Run the Angular tests, architecture boundary check, and production build:

```bash
npm run test:ci
npm run check:architecture
npm run build
```

Then complete a short keyboard check:

1. Tab through the application header and confirm every interactive element has a visible focus indicator.
2. Open the schedule and an event detail page; confirm focus reaches each destination heading.
3. Change the month and ministry filters; confirm focus remains on the control being used.
4. Inspect the screen-reader landmark list and confirm there is one main region.
5. Inspect the schedule with a screen reader and confirm the event count and list items are announced.
6. Enable reduced motion at the operating-system level and verify that navigation remains understandable.

Part 23-2 continues with invalid forms, optimistic volunteer assignments, status announcements, and automated accessibility scanning.
