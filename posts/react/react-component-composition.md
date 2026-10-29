---
title: "React Component Composition: Build Features from Focused Parts"
excerpt: "Learn how to divide a React interface into focused components, design clear prop contracts, and keep state in the right owner."
category: "React"

seo:
  focusKeyword: "React component composition"
  description: "Use React component composition, props, children, and explicit state ownership to build maintainable feature interfaces."
  socialTitle: "React Component Composition Without Component Sprawl"
  socialDescription: "A practical guide to component boundaries, prop contracts, children, state ownership, and avoiding premature abstractions."
---

# React Component Composition: Build Features from Focused Parts

React makes it easy to create a component, but it does not decide where one component should end and another should begin. A page can remain technically correct while becoming difficult to understand because fetching, state, event handling, and presentation all live in one function.

> **Quick answer:** Split a React feature around responsibilities that can be named and tested. Pass data down through typed props, send user intent up through callbacks, use `children` for genuine visual slots, and keep each piece of state in the closest component that must coordinate it.

## Start with Responsibilities, Not Line Counts

A long component is a signal to inspect, not an automatic reason to split. Extracting every small fragment produces a different problem: developers must jump through many files to understand one interaction.

A component boundary is useful when the extracted part has at least one of these qualities:

- It represents a meaningful interface concept, such as a product card or search-results list.
- It has a clear input and event contract.
- It owns local interaction state.
- It is reused in more than one real location.
- It can change for a different reason than its parent.

Start with a page that owns feature coordination and smaller components that render focused parts of that feature.

```tsx
type Product = {
  id: string;
  name: string;
  price: number;
};

type ProductCardProps = {
  product: Product;
  onOpen: (productId: string) => void;
};

export function ProductCard({ product, onOpen }: ProductCardProps) {
  return (
    <article>
      <h2>{product.name}</h2>
      <p>${product.price.toFixed(2)}</p>
      <button type="button" onClick={() => onOpen(product.id)}>
        View product
      </button>
    </article>
  );
}
```

`ProductCard` does not know how navigation works or where products came from. Its contract is data plus a meaningful event. That makes it reusable without making it generic.

## Pass Data Down and Intent Up

React props are read-only snapshots. A child should not mutate an object received from its parent. Instead, it reports what the user intended and lets the state owner decide what happens.

```tsx
import { useState } from 'react';

const products: Product[] = [
  { id: 'desk-lamp', name: 'Desk lamp', price: 39 },
  { id: 'task-chair', name: 'Task chair', price: 249 },
];

export function CatalogPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  return (
    <>
      <h1>Products</h1>
      <div className="card-grid">
        {products.map((product) => (
          <ProductCard
            key={product.id}
            product={product}
            onOpen={setSelectedId}
          />
        ))}
      </div>
      {selectedId !== null && <p>Selected product: {selectedId}</p>}
    </>
  );
}
```

The page owns `selectedId` because it coordinates the cards and the selected-product result. Moving that state into every card would create multiple sources of truth.

Name callbacks after domain intent. `onOpen`, `onAddToCart`, and `onRemoveItem` explain the contract better than `onClick`, which exposes the current implementation detail.

## Use `children` for Real Visual Slots

The `children` prop is useful when a component supplies structure while its caller supplies content.

```tsx
import type { ReactNode } from 'react';

type PanelProps = {
  title: string;
  actions?: ReactNode;
  children: ReactNode;
};

export function Panel({ title, actions, children }: PanelProps) {
  return (
    <section className="panel" aria-labelledby={`panel-${title}`}>
      <header className="panel__header">
        <h2 id={`panel-${title}`}>{title}</h2>
        {actions}
      </header>
      <div className="panel__content">{children}</div>
    </section>
  );
}
```

The parent controls what fills the content and action slots. The panel controls visual structure. Avoid replacing straightforward props with `children` when the child needs a precise domain value; explicit contracts are easier to understand and type.

## Put State in the Closest Coordinating Owner

Ask which component must keep an interaction consistent:

- A disclosure button can own its own expanded state.
- A group that allows only one expanded item should own the active item ID.
- A URL-worthy selection may belong in the router rather than component state.
- Server data belongs behind a data-access boundary, not copied into every child.

Do not move all state to the application root in the name of reuse. State should rise only as far as coordination requires. React's guidance calls this a single source of truth for each piece of state, not one global source for all state.

## Prefer Concrete Components Before Generic Systems

A `ProductCard` and `CustomerCard` may look similar today but have different domain actions and accessibility needs. A premature `UniversalCard<T>` often grows flags such as `compact`, `showStatus`, and `allowPurchase` until its contract becomes harder than two focused components.

Extract a shared primitive after stable repetition appears:

```tsx
type CardFrameProps = {
  children: ReactNode;
  className?: string;
};

export function CardFrame({ children, className = '' }: CardFrameProps) {
  return <div className={`card-frame ${className}`.trim()}>{children}</div>;
}
```

Keep domain components responsible for domain language. Let visual primitives handle only stable visual structure.

## Watch for Composition Failure Modes

Common warning signs include:

- A child receives many unrelated booleans that describe multiple modes.
- Several children copy the same prop into local state.
- A wrapper forwards every prop without understanding any of them.
- A parent reaches into a child's DOM instead of using a public contract.
- Context is introduced merely to avoid passing a value through one or two levels.
- Tiny components make one behavior impossible to follow without opening many files.

Composition should reduce coupling and clarify ownership. It is not a contest to produce the most components.

## Test the Public Contract

Test what the component renders and which callback it invokes. Do not test private helper names or internal state variables.

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { expect, test, vi } from 'vitest';

test('reports the selected product', () => {
  const onOpen = vi.fn();

  render(
    <ProductCard
      product={{ id: 'desk-lamp', name: 'Desk lamp', price: 39 }}
      onOpen={onOpen}
    />,
  );

  fireEvent.click(screen.getByRole('button', { name: 'View product' }));

  expect(onOpen).toHaveBeenCalledWith('desk-lamp');
});
```

This test remains useful if `ProductCard` changes its internal markup while preserving its accessible behavior.

## Use a Composition Checklist

Before extracting or expanding a component, ask:

1. Can its responsibility be described in one sentence?
2. Are its inputs and events explicit?
3. Is each state value owned in one place?
4. Does the component use domain language instead of generic flags?
5. Would the boundary still make sense if the visual design changed?
6. Can its behavior be tested through rendered output and user actions?

Good React composition is less about file size than decision ownership. Focused components make data flow visible, keep changes local, and allow features to grow without turning the component tree into an abstraction framework.

For the underlying React model, see the official guides to [passing props](https://react.dev/learn/passing-props-to-a-component), [sharing state](https://react.dev/learn/sharing-state-between-components), and [thinking in React](https://react.dev/learn/thinking-in-react).
