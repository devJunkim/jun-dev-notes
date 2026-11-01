---
title: "ChurchServe React Tutorial Part 23-1: Semantic Structure and Route Focus"
excerpt: "Preserve one main landmark, describe schedule collections, move focus after React Router navigation, expose visible focus, and respect reduced motion."
category: "React Tutorial"

seo:
  focusKeyword: "React Router focus management"
  description: "Improve React accessibility with semantic landmarks, labelled lists, route-aware focus management, visible focus indicators, and reduced motion."
  socialTitle: "ChurchServe React Part 23-1: Route Focus and Structure"
  socialDescription: "Make React Router navigation and schedule structure predictable for keyboard and screen-reader users."
---

# ChurchServe React Tutorial Part 23-1: Semantic Structure and Route Focus

[Part 22](https://dev.jun-kim.net/2026/10/31/churchserve-react-tutorial-part-22-3-privacy-boundaries-and-security-tests/) established permission and privacy boundaries. The interface also needs a predictable structure for people who navigate by keyboard or assistive technology.

ChurchServe already has native links, buttons, headings, a skip link, and one application-level `main`. Client-side routing still creates a gap: the URL and routed content can change while keyboard focus remains on a link that has disappeared.

> **Quick answer:** Keep one page-level `main`, expose visual collections as semantic lists, watch React Router’s pathname, move focus to the destination `h1` after a real route change, ignore query-only updates, and make every focus target visible while respecting reduced-motion preferences.

## Preserve one main landmark

The application shell owns the document’s main content region:

```tsx
<main id="main-content" ref={mainContentRef} tabIndex={-1}>
  <Outlet />
</main>
```

Routed pages use labelled `section` elements rather than adding another `main`:

```tsx
<section className="schedule-page" aria-labelledby="schedule-heading">
  <p className="eyebrow">Schedule</p>
  <h1 id="schedule-heading">Upcoming services</h1>
  {/* Schedule states */}
</section>
```

The existing skip link continues to target `#main-content`. The shell’s `tabIndex={-1}` makes `main` available as a programmatic fallback without adding it to the ordinary Tab sequence.

The application error boundary is an exception in the implementation: it replaces the failed shell and supplies its own `main`; it does not render a nested landmark.

## Describe the schedule as a collection

CSS Grid controls visual layout, but it does not communicate that the event cards form a collection. ChurchServe keeps the existing list and gives it a concise accessible name:

```tsx
<ul className="event-grid" aria-label="Service events">
  {state.events.map((event) => (
    <li key={event.id}>
      <ServiceEventCard event={event} />
    </li>
  ))}
</ul>
```

The card keeps a native `Link`. Do not replace it with a clickable `div`; links already provide keyboard activation, focus behavior, context-menu actions, and link semantics.

CSS may remove list markers without removing the HTML relationship:

```css
.event-grid {
  display: grid;
  margin: 2.5rem 0 0;
  padding: 0;
  gap: 1rem;
  list-style: none;
}
```

The test queries the list by role and name, protecting the user-facing contract rather than a CSS selector:

```typescript
expect(
  screen.getByRole('list', { name: 'Service events' }),
).toBeVisible()
```

## Track pathname changes with React Router

React Router’s [`useLocation`](https://reactrouter.com/api/hooks/useLocation) returns the current location and is designed for effects that follow navigation. Create `useRouteFocus.ts`:

```tsx
import { useEffect, useRef, type RefObject } from 'react'
import { useLocation } from 'react-router'

export function useRouteFocus(
  mainContentRef: RefObject<HTMLElement | null>,
): void {
  const { pathname } = useLocation()
  const previousPath = useRef(pathname)

  useEffect(() => {
    if (previousPath.current === pathname) return

    previousPath.current = pathname
    const mainContent = mainContentRef.current
    const browserWindow = mainContent?.ownerDocument.defaultView
    if (!mainContent || !browserWindow) return

    const pendingFocus = browserWindow.setTimeout(() => {
      const target =
        mainContent.querySelector<HTMLElement>('h1') ?? mainContent

      if (!target.hasAttribute('tabindex')) {
        target.setAttribute('tabindex', '-1')
      }
      target.focus()
    })

    return () => browserWindow.clearTimeout(pendingFocus)
  }, [mainContentRef, pathname])
}
```

This is an appropriate Effect because it synchronizes React with the browser focus API. React’s [`useEffect` documentation](https://react.dev/reference/react/useEffect) also explains why the timeout needs cleanup: development can run an extra setup-cleanup cycle, and a later navigation must cancel stale work.

## Focus the destination heading

The timeout lets React commit the destination route before the hook searches for its heading. The hook selects the first routed `h1` and falls back to `main` if a route has no heading.

Adding `tabindex="-1"` makes a static heading programmatically focusable without putting it in the normal keyboard sequence. A screen-reader or keyboard user receives context at the start of the new view instead of being left on a removed navigation control.

The effect does not run on the initial render because `previousPath` starts with the current pathname. It handles client-side transitions without unexpectedly moving focus when the user first opens or refreshes a document.

Use the hook at the shell boundary:

```tsx
function AppShell() {
  const mainContentRef = useRef<HTMLElement>(null)
  useRouteFocus(mainContentRef)

  return (
    <main id="main-content" ref={mainContentRef} tabIndex={-1}>
      <Outlet />
    </main>
  )
}
```

## Do not steal focus for query-only updates

The effect depends on `pathname`, not the complete location object. These transitions therefore behave differently:

- `/schedule` to `/schedule/sunday-2026-11-01` focuses the detail heading;
- `/schedule` to `/schedule?view=compact` leaves focus where the user placed it;
- a redirect focuses the pathname React Router ultimately commits.

Query strings often represent filtering, sorting, or pagination within the same view. Moving focus to the heading after every filter would interrupt the control the user is operating.

Protect both sides of the behavior:

```tsx
await user.click(
  await screen.findByRole('link', { name: 'View the schedule' }),
)

const heading = await screen.findByRole('heading', {
  level: 1,
  name: 'Upcoming services',
})
await waitFor(() => expect(heading).toHaveFocus())
```

```tsx
const createLink = await screen.findByRole('link', {
  name: 'Create service',
})
createLink.focus()

await router.navigate('/schedule?view=compact')

expect(createLink).toHaveFocus()
```

The second test is as important as the first: accessibility includes avoiding unnecessary context changes.

## Make keyboard focus visible

WCAG’s [Focus Visible guidance](https://www.w3.org/WAI/WCAG22/Understanding/focus-visible) explains that keyboard users need to know which element will receive their next action. Add one high-contrast baseline:

```css
:where(a, button, input, select, textarea, [tabindex]):focus-visible {
  outline: 3px solid #005a9c;
  outline-offset: 3px;
}
```

ChurchServe also updates more-specific component rules to the same color. A low-specificity `:where()` rule alone cannot override an existing component selector.

The assignment workflow introduced in Part 18 gains an explicit style for its programmatic row target:

```css
.position-list > li:focus-visible {
  outline: 3px solid #005a9c;
  outline-offset: 3px;
}
```

Do not remove outlines merely to make mouse interactions look cleaner. `:focus-visible` lets the browser apply the indicator for interaction modes that need it.

## Respect reduced-motion preferences

ChurchServe does not depend on animation, but future transitions should not override an operating-system preference:

```css
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

The near-zero duration allows animation events to finish while eliminating visible motion. Do not use a global rule like this when an interface depends on motion to communicate state; redesign that state so its meaning is also available without animation.

Part 23 also adds a reusable visually-hidden utility for text that should remain in the accessibility tree. It uses clipping rather than `display: none`, which would hide the content from assistive technology too.

## Complete a manual navigation pass

Automated DOM tests cannot judge the complete navigation experience. Check the built application with a keyboard and a screen reader:

1. Tab through the header, content, and footer and confirm every interactive element has a visible focus indicator.
2. Follow links between home, schedule, create, detail, volunteers, and unavailable pages; confirm focus reaches each destination heading.
3. Change only a query parameter and confirm focus stays on the active control.
4. Activate the skip link and confirm it reaches the single main landmark.
5. Inspect the landmark and heading lists and confirm their hierarchy is understandable.
6. Inspect the schedule and confirm it is announced as a named list with the expected item count.
7. Enable reduced motion at the operating-system level and confirm the application remains understandable.

## Result

ChurchServe now preserves one page-level main region, labels its event collection, moves focus after real React Router transitions, ignores query-only changes, exposes strong focus indicators, and respects reduced motion.

Part 23-2 applies the same discipline to invalid forms, optimistic assignments, status messages, and automated accessibility scans.
