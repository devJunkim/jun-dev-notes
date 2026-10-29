---
title: "TypeScript Boundaries in React: Props, Events, and API Data"
excerpt: "Use TypeScript to make React component contracts, UI states, events, and external data boundaries explicit without hiding uncertainty."
category: "React"

seo:
  focusKeyword: "TypeScript React props"
  description: "Design reliable TypeScript boundaries for React props, events, UI states, children, and untrusted API responses under strict checking."
  socialTitle: "TypeScript Boundaries for Reliable React Components"
  socialDescription: "Model React props, events, UI states, and API data precisely while keeping runtime validation at external boundaries."
---

# TypeScript Boundaries in React: Props, Events, and API Data

TypeScript adds the most value where one part of an application hands data or control to another. Vague types at component, event, and network boundaries allow invalid states to travel deep into the interface.

> **Quick answer:** Enable strict checking, give components explicit prop contracts, model mutually exclusive UI states with discriminated unions, type events at the point they enter your code, use `ReactNode` only for intentional composition slots, and validate external JSON at runtime before trusting its shape.

## Enable Strict Checking First

Use the compiler as a quality gate rather than a documentation generator.

```json
{
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "exactOptionalPropertyTypes": true,
    "jsx": "react-jsx"
  }
}
```

The `strict` option enables the strict-mode family of checks. Additional options can make indexing and optional-property semantics more explicit. Adopt them deliberately because they may expose existing assumptions.

## Define Props as a Public Contract

Props describe what a caller must supply and what a component promises to understand.

```tsx
type UserSummary = {
  id: string;
  displayName: string;
  role: 'admin' | 'editor' | 'viewer';
};

type UserCardProps = {
  user: UserSummary;
  selected?: boolean;
  onSelect: (userId: string) => void;
};

export function UserCard({
  user,
  selected = false,
  onSelect,
}: UserCardProps) {
  return (
    <article aria-current={selected ? 'true' : undefined}>
      <h2>{user.displayName}</h2>
      <p>{user.role}</p>
      <button type="button" onClick={() => onSelect(user.id)}>
        Select user
      </button>
    </article>
  );
}
```

Prefer a domain event signature such as `(userId: string) => void` over exposing the browser click event when the parent does not need it.

## Model Valid UI States, Not Boolean Combinations

Three booleans can represent contradictory combinations such as loading and loaded at the same time. A discriminated union makes valid states explicit.

```tsx
type Report = {
  id: string;
  title: string;
};

type ReportsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; reports: Report[] };

type ReportsViewProps = {
  state: ReportsState;
};

export function ReportsView({ state }: ReportsViewProps) {
  switch (state.status) {
    case 'loading':
      return <p>Loading reports…</p>;
    case 'error':
      return <p role="alert">{state.message}</p>;
    case 'ready':
      return <ReportList reports={state.reports} />;
  }
}
```

Narrowing gives each branch exactly the data it can safely use. Adding a new status also creates a visible compiler task at every exhaustive consumer.

## Type Browser Events at the Boundary

When a component genuinely handles a DOM event, use the matching React event type.

```tsx
import type { ChangeEvent, FormEvent } from 'react';
import { useState } from 'react';

export function UserSearchForm() {
  const [query, setQuery] = useState('');

  function handleQueryChange(event: ChangeEvent<HTMLInputElement>) {
    setQuery(event.currentTarget.value);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // Submit the normalized query through the feature boundary.
  }

  return (
    <form onSubmit={handleSubmit}>
      <label htmlFor="user-query">Search users</label>
      <input
        id="user-query"
        value={query}
        onChange={handleQueryChange}
      />
      <button type="submit">Search</button>
    </form>
  );
}
```

`currentTarget` has the type declared by the handler. Avoid casting an untyped event to make an error disappear.

## Type Composition Slots Intentionally

Use `ReactNode` for content that may contain ordinary renderable React values.

```tsx
import type { ReactNode } from 'react';

type EmptyStateProps = {
  title: string;
  action?: ReactNode;
  children: ReactNode;
};

export function EmptyState({ title, action, children }: EmptyStateProps) {
  return (
    <section>
      <h2>{title}</h2>
      <div>{children}</div>
      {action}
    </section>
  );
}
```

Do not add `children` to every component automatically. A component with no composition slot should reject unexpected nested content.

## Keep Domain Types Separate from Transport Details

An API response may use nullable fields, strings for dates, or versioned wire names that should not leak throughout the UI. Parse it into a domain-facing model at the API boundary.

```ts
type ProductDto = {
  id: unknown;
  price: unknown;
};

type Product = {
  id: string;
  price: number;
};

function parseProduct(value: ProductDto): Product {
  if (typeof value.id !== 'string' || typeof value.price !== 'number') {
    throw new Error('The product response has an invalid shape.');
  }

  if (!Number.isFinite(value.price) || value.price < 0) {
    throw new Error('The product price is invalid.');
  }

  return { id: value.id, price: value.price };
}
```

In a larger application, a schema validation library can centralize this work. The important distinction is that TypeScript types disappear at runtime and cannot validate a server response by themselves.

## Avoid Type Assertions as Validation

This is convenient but unsafe:

```ts
const products = await response.json() as Product[];
```

The assertion tells the compiler to trust the developer. It performs no runtime check. Prefer `unknown`, validate, then return the trusted model.

Also avoid broad `any`, non-null assertions scattered throughout components, and types that merely reproduce uncertain server data without narrowing it.

## Use Type-Only Imports

When an import exists only for type checking, make that intent visible:

```ts
import type { ReactNode } from 'react';
import type { Product } from './model/product';
```

This keeps runtime dependencies clear and works well with modern module settings.

## Test Both Runtime and Compile-Time Boundaries

Component tests verify runtime behavior. The TypeScript compiler verifies that callers respect contracts. Run both in the quality gate:

```json
{
  "scripts": {
    "typecheck": "tsc --noEmit",
    "test": "vitest run",
    "build": "tsc -b && vite build"
  }
}
```

A passing test suite does not prove every component call is well typed, and a passing type check does not prove that untrusted JSON has the promised shape.

## Review the Boundaries

- Are required and optional props genuinely different?
- Can the type represent contradictory UI states?
- Do callbacks describe domain intent?
- Are browser events contained near the DOM?
- Is external data validated before entering trusted application code?
- Are assertions documenting proven invariants or hiding uncertainty?

Good React TypeScript code does not attempt to type everything with maximum cleverness. It makes uncertainty explicit at boundaries and keeps the trusted interior simple.

See the official TypeScript documentation for [JSX type checking](https://www.typescriptlang.org/docs/handbook/jsx.html) and the [`strict` compiler option](https://www.typescriptlang.org/tsconfig/strict.html).
