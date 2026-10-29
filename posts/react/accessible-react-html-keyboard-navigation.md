---
title: "Accessible React HTML: Landmarks, Keyboard Use, and Skip Links"
excerpt: "Build accessible React pages with semantic landmarks, native controls, visible focus, logical headings, labels, and skip navigation."
category: "React"

seo:
  focusKeyword: "accessible React HTML"
  description: "Create accessible React interfaces with semantic HTML, landmarks, keyboard support, visible focus, form labels, and skip links."
  socialTitle: "Accessible React Starts with Semantic HTML"
  socialDescription: "Use landmarks, native controls, skip links, focus styles, headings, and labels to make React interfaces work for more users."
---

# Accessible React HTML: Landmarks, Keyboard Use, and Skip Links

React can render accessible HTML, but JSX does not make an interface accessible automatically. A clickable `<div>` remains a poor button, and a visually polished page can still be difficult to navigate by keyboard or assistive technology.

> **Quick answer:** Start with semantic HTML, provide one clear main landmark, use links for navigation and buttons for actions, label controls, preserve visible keyboard focus, add a skip link before repeated navigation, and test the interface through its accessible roles and names.

## Build the Page from Landmarks

Landmarks let users understand and move through the page structure.

```tsx
import { Outlet } from 'react-router';

export function AppLayout() {
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>

      <header className="site-header">
        <p className="site-name">Example Portal</p>
        <PrimaryNavigation />
      </header>

      <main id="main-content" tabIndex={-1}>
        <Outlet />
      </main>

      <footer className="site-footer">
        <small>Example Portal</small>
      </footer>
    </div>
  );
}
```

Use one non-hidden `<main>` for the page's primary content. A feature page can use `<section>`, `<article>`, and `<aside>` inside it as their meanings require.

## Make the Skip Link Work Visually

Keyboard users should be able to bypass repeated site navigation. Keep the skip link first in the focus order and reveal it when focused.

```css
.skip-link {
  left: 1rem;
  position: fixed;
  top: 1rem;
  transform: translateY(-200%);
  z-index: 1000;
}

.skip-link:focus {
  background: white;
  color: black;
  padding: 0.75rem 1rem;
  transform: translateY(0);
}
```

`tabIndex={-1}` lets the main element receive programmatic or fragment focus without placing it in the normal Tab sequence. Verify the behavior in the browsers and assistive technology combinations your product supports.

## Use Native Interactive Elements

Choose elements by behavior:

- Use `<a>` or router links to move to a location.
- Use `<button>` to perform an action.
- Use `<input>`, `<select>`, and `<textarea>` for form values.

Avoid rebuilding keyboard and accessibility behavior on generic elements.

```tsx
// Navigation
<NavLink to="/reports">Reports</NavLink>

// Action
<button type="button" onClick={exportReport}>
  Export report
</button>
```

A `div` with `onClick` is not automatically keyboard operable, focusable, or exposed with the correct role. Native elements provide those semantics before custom ARIA is considered.

## Preserve Visible Keyboard Focus

Never remove outlines globally without an equally strong replacement.

```css
:focus-visible {
  outline: 3px solid #1458d4;
  outline-offset: 3px;
}
```

Focus must remain visible against every background and should not be obscured by sticky headers or overlays. Test the complete flow with Tab, Shift+Tab, Enter, Space, and Escape where relevant.

Keyboard accessibility is not limited to adding `tabIndex`. Positive tab indices create a second navigation order that becomes hard to maintain. Prefer natural DOM order.

## Keep Headings Logical

Headings describe content structure, not visual size. Each page should have a clear primary heading, and subsections should descend in a meaningful hierarchy.

```tsx
export function ProductsPage() {
  return (
    <>
      <h1>Products</h1>
      <section aria-labelledby="featured-title">
        <h2 id="featured-title">Featured products</h2>
        <ProductList />
      </section>
    </>
  );
}
```

Use CSS to change appearance. Do not choose `<h3>` because its default font size looks convenient.

## Label Form Controls

Placeholders are examples or hints, not durable labels.

```tsx
export function ProductSearch() {
  return (
    <form role="search">
      <label htmlFor="product-search">Search products</label>
      <input
        id="product-search"
        name="query"
        type="search"
        autoComplete="off"
      />
      <button type="submit">Search</button>
    </form>
  );
}
```

Associate error text with the control using `aria-describedby`, and use `aria-invalid` when the value is invalid. Do not rely on red color alone.

## Manage SPA Route Changes Deliberately

Client-side routing changes content without loading a new document, so focus does not automatically move to the new page. Consider moving focus to the main container or page heading after a pathname change, especially when navigation replaces most content.

Do not move focus for every query-string update. A user adjusting a filter should not lose their place after each selection. Focus management must follow the interaction, not a blanket router event.

Announce asynchronous status with restraint. Use `role="status"` for polite progress and `role="alert"` for urgent errors, but avoid live regions that repeat every minor change.

## Test Semantics and Keyboard Behavior

React Testing Library queries can expose semantic problems early.

```tsx
import { render, screen } from '@testing-library/react';
import { expect, test } from 'vitest';

test('provides navigation and main landmarks', () => {
  render(<AppLayoutForTest />);

  expect(
    screen.getByRole('navigation', { name: 'Primary navigation' }),
  ).toBeVisible();
  expect(screen.getByRole('main')).toBeVisible();
  expect(screen.getByRole('link', { name: 'Skip to main content' }))
    .toHaveAttribute('href', '#main-content');
});
```

Automated checks cannot judge every focus flow, announcement, contrast context, or cognitive burden. Add manual keyboard and assistive-technology review to the quality gate.

## Avoid Accessibility Failure Modes

- Do not use clickable generic elements instead of links or buttons.
- Do not remove focus indicators.
- Do not add ARIA when native HTML already provides the semantics.
- Do not use placeholders as labels.
- Do not communicate state through color alone.
- Do not force focus after minor in-page updates.
- Do not assume passing an automated audit means the experience is accessible.

Accessible React begins with the browser platform. Semantic HTML gives React components durable behavior, then focused ARIA and JavaScript can fill the gaps that real interactions require.

Review MDN's guidance on [keyboard accessibility](https://developer.mozilla.org/en-US/docs/Web/Accessibility/Guides/Understanding_WCAG/Keyboard) and the [`main` element](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/main).
