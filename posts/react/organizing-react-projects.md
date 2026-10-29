---
title: "Organizing a React Project for Sustainable Growth"
excerpt: "Structure React startup, routing, shared UI, features, tests, and styles so ownership remains clear as the application grows."
category: "React"

seo:
  focusKeyword: "React project organization"
  description: "Organize a React project by startup, routing, shared UI, and feature ownership without creating rigid or premature architecture."
  socialTitle: "A Practical React Project Organization"
  socialDescription: "Separate startup, routes, shared UI, features, tests, and styles while keeping related React code close together."
---

# Organizing a React Project for Sustainable Growth

React does not prescribe one folder structure. That flexibility is useful, but a project can become inconsistent when every feature invents a new location for components, tests, data access, and types.

> **Quick answer:** Keep the entry point small, define routing explicitly, organize most code by feature ownership, colocate tests and feature-specific styles, reserve shared folders for proven cross-feature code, and enforce dependency direction rather than chasing a universal folder template.

## Organize Around Change

Files that change together should usually live near each other. A global folder for every component, hook, type, and test looks tidy at first but spreads one feature across the repository.

A small product application can begin with this shape:

```text
src/
  main.tsx
  router.tsx
  app/
    AppLayout.tsx
    AppLayout.test.tsx
  pages/
    HomePage.tsx
    NotFoundPage.tsx
  features/
    products/
      components/
        ProductCard.tsx
        ProductCard.test.tsx
      pages/
        ProductsPage.tsx
        ProductPage.tsx
      api/
        productsClient.ts
      model/
        product.ts
      products.css
    orders/
      pages/
        OrdersPage.tsx
  shared/
    ui/
      Button.tsx
    http/
      apiClient.ts
  styles/
    global.css
```

This is a starting point, not a framework. Do not create empty layers merely to match the diagram.

## Keep Startup Small

`main.tsx` is the composition root for browser startup. It should mount providers and global styles, not contain feature logic.

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

If the entry point grows, extract provider composition into an application module. Do not let startup become a second application component.

## Make Routing a Discoverable Boundary

Place route definitions where developers can find the product's URL surface quickly. Routes may import feature pages, but low-level components should not import the router.

```text
main.tsx -> router.tsx -> feature pages -> feature components
                              |
                              +-> shared UI and HTTP utilities
```

This direction keeps infrastructure decisions from leaking into presentational components. A product card should not need to know how the root router was constructed.

## Prefer Feature Ownership for Domain Code

The products feature can own its pages, components, API calls, and model types. That keeps a change such as adding inventory status mostly within one folder.

```ts
// features/products/model/product.ts
export type ProductSummary = {
  id: string;
  name: string;
  price: number;
};
```

```ts
// features/products/api/productsClient.ts
import type { ProductSummary } from '../model/product';

export async function getProducts(signal?: AbortSignal): Promise<ProductSummary[]> {
  const response = await fetch('/api/products', { signal });

  if (!response.ok) {
    throw new Error(`Loading products failed with ${response.status}`);
  }

  return response.json() as Promise<ProductSummary[]>;
}
```

In production, validate untrusted response data before treating it as `ProductSummary[]`; a TypeScript assertion does not validate JSON at runtime.

## Be Conservative with `shared`

`shared` should contain code with demonstrated cross-feature use and no dependency on one feature. Moving a component there too early obscures its owner and often creates generic APIs full of flags.

Useful shared candidates include:

- A visual button primitive used consistently across features
- HTTP request infrastructure
- Cross-cutting error presentation
- Stable formatting utilities

`InventoryStatusBadge` still belongs to products even if several product pages use it.

## Colocate Tests with the Code They Protect

Keeping `ProductCard.test.tsx` next to `ProductCard.tsx` makes ownership and deletion straightforward. Broader integration or end-to-end tests may live in dedicated top-level locations because they span multiple features.

Avoid a mirror-image test tree that requires developers to navigate two distant hierarchies for every change.

## Choose a Styling Ownership Rule

Global CSS should define tokens, resets, typography, and truly global layout behavior. Feature-specific selectors should stay with the feature or use the project's chosen scoped styling system.

The important rule is predictable ownership. Mixing global styles, CSS modules, inline styles, and multiple CSS-in-JS systems without a reason creates a more serious maintenance cost than any one approach.

## Avoid Ambiguous Barrel Files

An `index.ts` that re-exports an entire feature can hide dependencies and create cycles. Public feature entry points can be useful, but keep them intentional:

```ts
// features/products/index.ts
export { ProductsPage } from './pages/ProductsPage';
export type { ProductSummary } from './model/product';
```

Do not re-export every internal helper. A small public surface makes feature boundaries meaningful.

## Let the Structure Evolve

For a two-page application, `pages/` and `components/` may be enough. Introduce feature folders when related code begins to scatter. Introduce shared infrastructure when duplication is stable. Introduce stronger module enforcement when multiple teams or dependency cycles make it valuable.

Architecture should respond to real change pressure. Empty `domain`, `application`, and `infrastructure` directories do not create separation by themselves.

## Review the Dependency Rules

A practical React project can use these rules:

1. Startup imports application providers and routing.
2. Routing imports page-level feature entry points.
3. Features may use shared UI and infrastructure.
4. Shared code does not import features.
5. One feature does not reach into another feature's internal files.
6. Tests follow the same public boundaries unless they are intentionally unit testing a local module.

Lint rules or path aliases can enforce these decisions later, but aliases should not disguise a tangled dependency graph.

## Avoid Organization Failure Modes

- Do not group everything by file type once features become substantial.
- Do not create a folder for a single file without a clear growth reason.
- Do not put feature-specific code in `shared` to make imports shorter.
- Do not couple presentational components to router construction or HTTP details.
- Do not add abstractions solely to make the tree look architectural.
- Do not reorganize the whole repository for every new feature.

Good organization reduces the distance between a product change and the code that implements it. The exact folder names matter less than clear ownership, stable dependency direction, and a structure that can grow incrementally.

For tool-level project setup and build behavior, consult the official [Vite guide](https://vite.dev/guide/).
