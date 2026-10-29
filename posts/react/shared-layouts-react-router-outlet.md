---
title: "Shared React Layouts with React Router Outlet"
excerpt: "Build persistent application and feature layouts with nested React Router routes and Outlet without duplicating page chrome."
category: "React"

seo:
  focusKeyword: "React Router Outlet layout"
  description: "Create shared React layouts with nested routes and Outlet while keeping global chrome, feature navigation, and page content separate."
  socialTitle: "Shared React Layouts with React Router Outlet"
  socialDescription: "Use nested routes and Outlet to keep application chrome stable and compose feature-specific layouts cleanly."
---

# Shared React Layouts with React Router Outlet

Headers, navigation, footers, and feature sidebars should not be copied into every page. Duplication makes small design changes expensive and can create inconsistent landmarks across routes.

> **Quick answer:** Put persistent interface chrome in a parent route element and render the matched child route through `<Outlet />`. Add another layout route only when a group of pages genuinely shares structure or behavior, and keep page-specific headings and content in the child pages.

## Understand What `Outlet` Represents

`<Outlet />` is the rendering position for the currently matched child route. It is not a generic replacement for `children`; its content comes from the router's matched route tree.

```tsx
import { NavLink, Outlet } from 'react-router';

export function AppLayout() {
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>

      <header className="site-header">
        <NavLink to="/" end>Example Portal</NavLink>
        <nav aria-label="Primary navigation">
          <NavLink to="/reports">Reports</NavLink>
          <NavLink to="/account">Account</NavLink>
        </nav>
      </header>

      <main id="main-content" tabIndex={-1}>
        <Outlet />
      </main>

      <footer className="site-footer">Example Portal</footer>
    </div>
  );
}
```

The header and footer stay mounted while the outlet changes from one page to another. This preserves a stable application frame and a single main landmark.

## Express the Layout in the Route Tree

The layout becomes the parent route for all pages that use it.

```tsx
import { createBrowserRouter } from 'react-router';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'reports', element: <ReportsPage /> },
      { path: 'account', element: <AccountPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
```

The child paths are relative to the parent. An index child has no additional path and fills the outlet at the parent's exact URL.

## Add a Feature Layout When Structure Is Shared

Suppose report pages share local navigation and a constrained content width. Create a reports layout instead of repeating that structure.

```tsx
import { NavLink, Outlet } from 'react-router';

export function ReportsLayout() {
  return (
    <section aria-labelledby="reports-title">
      <header className="feature-header">
        <h1 id="reports-title">Reports</h1>
        <nav aria-label="Report navigation">
          <NavLink to="/reports" end>Overview</NavLink>
          <NavLink to="/reports/trends">Trends</NavLink>
        </nav>
      </header>

      <div className="feature-content">
        <Outlet />
      </div>
    </section>
  );
}
```

Nest its pages beneath it:

```tsx
{
  path: 'reports',
  element: <ReportsLayout />,
  children: [
    { index: true, element: <ReportOverviewPage /> },
    { path: 'trends', element: <ReportTrendsPage /> },
    { path: ':reportId', element: <ReportPage /> },
  ],
}
```

The application outlet renders `ReportsLayout`; the reports outlet renders the selected report page. Each level owns only its persistent structure.

## Keep Page Identity in the Page

A global layout should not guess every page title from the URL. The child page usually owns its unique heading, empty states, and actions. A feature layout may own a group heading when it remains correct across every child, as in the reports example.

Avoid multiple competing `<h1>` elements created accidentally by both layout and page. Decide whether the feature heading or child heading identifies the document's primary content, then use lower heading levels consistently beneath it.

## Share Data Deliberately

`Outlet` can provide context to child routes, but use that mechanism for data inherently owned by the layout.

```tsx
import { Outlet, useOutletContext } from 'react-router';

type ReportsContext = {
  locale: string;
};

export function ReportsLayout() {
  return <Outlet context={{ locale: 'en-CA' }} />;
}

export function useReportsContext() {
  return useOutletContext<ReportsContext>();
}
```

Do not use outlet context as an unstructured global store. Route loaders, a focused React context, or ordinary props may be clearer depending on who owns the data and how it changes.

## Separate Layouts from Authorization

Hiding a navigation item or rendering an admin layout is user-interface behavior, not a security boundary. The ASP.NET Core API must authenticate the request and authorize every protected operation. A user can call an endpoint without rendering your React layout.

Layouts can improve the signed-in experience and prevent accidental navigation. They cannot protect server data.

## Test Persistent and Changing Content

A useful layout test starts at one route, navigates, and verifies that shared chrome remains while page content changes.

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { expect, test } from 'vitest';

test('keeps the shell while child content changes', async () => {
  const testRouter = createMemoryRouter(routes, { initialEntries: ['/'] });
  render(<RouterProvider router={testRouter} />);

  expect(screen.getByRole('banner')).toHaveTextContent('Example Portal');

  fireEvent.click(screen.getByRole('link', { name: 'Reports' }));

  expect(await screen.findByRole('heading', { name: 'Reports' })).toBeVisible();
  expect(screen.getByRole('banner')).toHaveTextContent('Example Portal');
});
```

The test describes user-visible composition instead of inspecting route internals.

## Avoid Layout Traps

- Do not repeat global landmarks inside every page.
- Do not add nested layouts solely because paths share a prefix.
- Do not put page-specific state in the root layout.
- Do not make an outlet context a catch-all service locator.
- Do not assume a hidden route or menu is authorization.
- Do not forget loading and error experiences when layout-owned data is asynchronous.

Shared layouts work best when the route hierarchy mirrors the visual hierarchy. The root owns global chrome, feature layouts own durable feature structure, and leaf routes own page-specific content.

See React Router's official [`Outlet` reference](https://reactrouter.com/api/components/Outlet) and [nested routing guide](https://reactrouter.com/start/data/routing).
