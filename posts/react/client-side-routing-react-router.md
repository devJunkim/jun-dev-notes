---
title: "Client-Side Routing in React with React Router"
excerpt: "Model application URLs as React Router routes, including nested pages, parameters, not-found handling, and deployment fallbacks."
category: "React"

seo:
  focusKeyword: "client-side routing in React"
  description: "Implement client-side routing in React with route objects, nested routes, parameters, error handling, and production server fallbacks."
  socialTitle: "Client-Side Routing in React with React Router"
  socialDescription: "Design durable React URLs with route objects, nested pages, parameters, navigation tests, and correct hosting behavior."
---

# Client-Side Routing in React with React Router

A single-page application still needs durable URLs. Users expect refresh, bookmarks, browser history, and shared links to return to the same screen. Treating routing as a collection of click handlers breaks those expectations.

> **Quick answer:** Define the application's URL hierarchy as React Router route objects, render it once through `RouterProvider`, use nested routes for nested UI, represent resources with URL parameters, provide an explicit not-found experience, and configure the production host to return the application entry point for client-owned routes.

## Treat the URL as Application State

If a user can return to a view, share it, or use the browser Back button to reach it, the relevant state usually belongs in the URL. Examples include:

- `/products` for the product catalog
- `/products/desk-lamp` for one product
- `/orders?status=open` for a shareable filter

Temporary presentation state, such as whether a tooltip is open, normally stays inside a component. Moving meaningful navigation state into the URL prevents invisible state from becoming the only way to reach a view.

## Define Route Objects Outside Rendering

With React Router's Data Mode, create a browser router once and describe the route tree independently from the rendered application.

```tsx
import { createBrowserRouter } from 'react-router';
import { AppLayout } from './AppLayout';
import { HomePage } from './pages/HomePage';
import { NotFoundPage } from './pages/NotFoundPage';
import { OrdersPage } from './pages/OrdersPage';
import { ProductPage } from './pages/ProductPage';
import { ProductsPage } from './pages/ProductsPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'products', element: <ProductsPage /> },
      { path: 'products/:productId', element: <ProductPage /> },
      { path: 'orders', element: <OrdersPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
```

Create the router at module scope rather than recreating it on every render. The route tree now documents the navigable product surface in one place.

## Mount the Router Once

The browser entry point should perform startup work and little else.

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router/dom';
import { router } from './router';
import './styles/global.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
);
```

Keeping route construction separate from startup makes each concern easier to change and gives tests the option to create a memory router instead of depending on the real browser location.

## Use Nested Routes for Nested UI

Child routes render through an `<Outlet />` in their parent. This relationship should reflect the interface hierarchy, not merely similar URL prefixes.

```tsx
import { Outlet } from 'react-router';

export function AppLayout() {
  return (
    <div className="app-shell">
      <header>Example Store</header>
      <main id="main-content">
        <Outlet />
      </main>
      <footer>Example Store</footer>
    </div>
  );
}
```

The index route renders at the exact parent URL. In this example, `HomePage` is the index child for `/`.

## Read and Validate Route Parameters

URL parameters arrive as strings and may be absent depending on the route context. Treat them as untrusted input.

```tsx
import { useParams } from 'react-router';

export function ProductPage() {
  const { productId } = useParams();

  if (!productId) {
    return <p>Product identifier is missing.</p>;
  }

  return (
    <>
      <h1>Product details</h1>
      <p>Loading product {productId}…</p>
    </>
  );
}
```

The API must still validate the identifier and authorization. A route parameter is navigation input, not proof that a resource exists or that the current user may access it.

## Use Links for Navigation

Use `<Link>` or `<NavLink>` for user-visible navigation. They preserve link semantics, support browser behaviors such as opening in a new tab, and let the router update the current location without a full document request.

Use `useNavigate` for imperative transitions caused by application logic, such as redirecting after a successful form submission. It should not replace ordinary links in menus or content.

## Handle Unknown URLs Intentionally

A wildcard child route provides a useful in-app not-found page.

```tsx
import { Link } from 'react-router';

export function NotFoundPage() {
  return (
    <section aria-labelledby="not-found-title">
      <h1 id="not-found-title">Page not found</h1>
      <p>The requested page does not exist.</p>
      <Link to="/">Return home</Link>
    </section>
  );
}
```

This handles a route after the React application has loaded. It does not fix the production web server by itself.

## Configure the Production Fallback

During development, Vite handles application routes for you. In production, a direct request to `/products/desk-lamp` reaches the web server first. If the server looks only for a matching physical file, it returns 404 before React starts.

Configure the static host to serve `index.html` for unknown client-owned paths while preserving real API and asset responses. The exact rule depends on the platform. Do not rewrite `/api/*` failures into HTML; API callers need proper HTTP status codes and JSON responses.

## Test Routes Without the Browser Router

Use a memory router to start a test at a known location.

```tsx
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { expect, test } from 'vitest';

test('renders a product identifier from the URL', () => {
  const testRouter = createMemoryRouter(
    [{ path: '/products/:productId', element: <ProductPage /> }],
    { initialEntries: ['/products/desk-lamp'] },
  );

  render(<RouterProvider router={testRouter} />);

  expect(screen.getByText(/desk-lamp/)).toBeInTheDocument();
});
```

This verifies the public URL-to-screen contract without mutating global browser history.

## Avoid Common Routing Mistakes

- Do not use component state as a substitute for a shareable URL.
- Do not use plain buttons for navigation that should behave like links.
- Do not trust route parameters as validated or authorized identifiers.
- Do not recreate the router during rendering.
- Do not forget the production history fallback.
- Do not make every nested URL a nested layout; nest when the UI relationship also fits.

Good client-side routing gives each important view a durable address while keeping browser behavior familiar. React Router coordinates rendering, but URL design and server configuration remain application architecture decisions.

See the official React Router documentation for [Data Mode routing](https://reactrouter.com/start/data/routing) and route objects.
