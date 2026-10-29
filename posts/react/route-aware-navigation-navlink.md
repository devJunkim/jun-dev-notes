---
title: "Route-Aware Navigation in React with NavLink"
excerpt: "Use React Router NavLink to build accessible navigation that reflects active and pending route state without manual URL checks."
category: "React"

seo:
  focusKeyword: "React Router NavLink"
  description: "Build route-aware React navigation with NavLink active styles, exact matching, accessible state, and behavior-focused tests."
  socialTitle: "Route-Aware React Navigation with NavLink"
  socialDescription: "Learn active matching, exact root links, pending state, accessible navigation, and reliable NavLink tests."
---

# Route-Aware Navigation in React with NavLink

Navigation should show users where they are. Comparing `window.location.pathname` manually seems simple, but it duplicates router matching rules and quickly fails for nested paths, base paths, and pending navigation.

> **Quick answer:** Use React Router's `<NavLink>` for navigation items that need route state. Derive classes from `isActive` and `isPending`, use `end` when a link should match only its exact destination, rely on the link's accessible current-page state, and test navigation through visible links and destinations.

## Choose `NavLink` for Stateful Navigation

Use `<Link>` when a link only needs to navigate. Use `<NavLink>` when the interface also needs to represent whether that destination is active or pending.

```tsx
import { NavLink } from 'react-router';

export function PrimaryNavigation() {
  return (
    <nav aria-label="Primary navigation">
      <ul className="primary-nav">
        <li><NavLink to="/" end>Home</NavLink></li>
        <li><NavLink to="/products">Products</NavLink></li>
        <li><NavLink to="/orders">Orders</NavLink></li>
      </ul>
    </nav>
  );
}
```

The `end` prop matters on the root link. Without it, `/` is a prefix of every application path and Home can remain active everywhere.

## Derive Styles from Router State

`className` can receive the link state. Keep the function small and deterministic.

```tsx
function navigationClass({
  isActive,
  isPending,
}: {
  isActive: boolean;
  isPending: boolean;
}) {
  return [
    'primary-nav__link',
    isActive ? 'primary-nav__link--active' : '',
    isPending ? 'primary-nav__link--pending' : '',
  ].filter(Boolean).join(' ');
}

<NavLink to="/products" className={navigationClass}>
  Products
</NavLink>
```

Style the current state with more than color alone. A border, weight, or shape gives users a second visual cue.

```css
.primary-nav__link {
  color: var(--color-text-muted);
  text-decoration-thickness: 0.12em;
  text-underline-offset: 0.3em;
}

.primary-nav__link--active {
  color: var(--color-text);
  font-weight: 700;
  text-decoration-line: underline;
}

.primary-nav__link:focus-visible {
  outline: 3px solid var(--color-focus);
  outline-offset: 3px;
}

.primary-nav__link--pending {
  opacity: 0.7;
}
```

Do not remove the focus outline merely because the active state is visible. Active and focused describe different things.

## Understand Matching Before Adding Exceptions

A link to `/products` normally remains active for descendants such as `/products/desk-lamp`. That behavior is useful for a primary Products section link. Add `end` if the link represents only the products index and should not match detail routes.

Be deliberate:

- Primary section links often use prefix matching.
- Tabs for sibling pages often use `end`.
- The root link almost always needs `end`.

Avoid implementing a second matching system with `startsWith`. The router already understands its route conventions.

## Keep Navigation Semantics Intact

`NavLink` renders an anchor, which gives users expected browser behavior. Do not replace it with a styled `<button>` just to run navigation code. Buttons perform actions; links move to resources or locations.

Give each `<nav>` a useful label when more than one navigation landmark exists:

```tsx
<nav aria-label="Primary navigation">...</nav>
<nav aria-label="Product navigation">...</nav>
```

React Router applies current-page semantics to active navigation links. Retain descriptive link text so both visual users and assistive technology users can understand the destination.

## Use Pending State Carefully

Data routers can expose a pending state while the destination's data or code is being prepared. A subtle style is often enough for ordinary navigation. If navigation can take noticeable time, coordinate a broader loading indicator at the layout level instead of placing competing spinners inside every link.

Pending UI should not disable the link so aggressively that keyboard focus disappears. It also should not imply success before the destination is ready.

## Generate Navigation from Stable Configuration

Configuration can reduce repeated markup when links truly share behavior.

```tsx
const primaryItems = [
  { to: '/', label: 'Home', end: true },
  { to: '/products', label: 'Products', end: false },
  { to: '/orders', label: 'Orders', end: false },
] as const;

export function PrimaryNavigation() {
  return (
    <nav aria-label="Primary navigation">
      <ul>
        {primaryItems.map((item) => (
          <li key={item.to}>
            <NavLink to={item.to} end={item.end} className={navigationClass}>
              {item.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
```

Do not hide permission-sensitive links by filtering a global array and mistake that for authorization. The server must still authorize the destination's data and operations.

## Test the User's Navigation Contract

Start the router at a known URL and assert the accessible current state.

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { expect, test } from 'vitest';

test('marks the selected destination as current', async () => {
  const testRouter = createMemoryRouter(routes, {
    initialEntries: ['/'],
  });

  render(<RouterProvider router={testRouter} />);

  fireEvent.click(screen.getByRole('link', { name: 'Products' }));

  const productsLink = screen.getByRole('link', { name: 'Products' });
  expect(productsLink).toHaveAttribute('aria-current', 'page');
  expect(await screen.findByRole('heading', { name: 'Products' })).toBeVisible();
});
```

This test verifies the behavior a user depends on, not an internal location comparison.

## Avoid Navigation Failure Modes

- Do not use buttons for ordinary navigation.
- Do not make Home active on every route; use `end`.
- Do not communicate active state through color alone.
- Do not remove visible keyboard focus.
- Do not copy route-matching logic into components.
- Do not treat hidden links as access control.

Route-aware navigation is a small feature with broad usability impact. Let the router determine match state, then express that state through semantic links, accessible current-page information, and clear visual treatment.

See the official React Router [`NavLink` reference](https://reactrouter.com/api/components/NavLink) for the current matching and render-prop API.
