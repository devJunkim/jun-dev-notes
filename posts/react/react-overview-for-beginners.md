---
title: "React Overview for Beginners: Components, Props, State, and Hooks"
excerpt: "Understand what React is, why developers use it, and how components, JSX, props, state, events, and hooks work together."
category: "React"

seo:
  focusKeyword: "React overview for beginners"
  description: "A beginner-friendly React overview covering components, JSX, props, state, events, hooks, benefits, trade-offs, and next steps."
  socialTitle: "React Overview for Beginners"
  socialDescription: "Learn the core React mental model through plain explanations and a small TypeScript example."
---

# React Overview for Beginners: Components, Props, State, and Hooks

React introduces a different way to build web interfaces. Instead of writing a sequence of commands that manually changes the page, you describe what the interface should look like for the current data. React updates the browser when that data changes.

> **Quick answer:** React is a JavaScript library for building user interfaces from components. Components receive data through props, remember changing data with state, respond to user actions through event handlers, and return JSX that describes what should appear on the screen.

## What Is React?

React is a library for building interactive user interfaces. It is commonly used for web applications, although React itself is not limited to the browser.

A React interface is assembled from **components**. A component is usually a JavaScript or TypeScript function that returns markup:

```tsx
type GreetingProps = {
  name: string;
};

export function Greeting({ name }: GreetingProps) {
  return <h1>Hello, {name}</h1>;
}
```

`Greeting` is a component. Its name starts with a capital letter, it receives a `name` value, and it returns the heading React should display.

React focuses on the interface layer. It does not prescribe one solution for routing, server communication, styling, authentication, or deployment. Those choices come from a framework, build tool, or other libraries selected for the application.

## What Are the Key Benefits of React?

React is popular because its model works well for interfaces that change in response to data and user actions.

### Reusable components

A component can be used wherever the same interface behavior is needed. Reuse can reduce duplication, but the more important benefit is ownership: each component can have a focused responsibility and an explicit contract.

### Declarative interfaces

You describe the desired result for the current state. React handles the browser updates needed to reach that result.

```tsx
function StatusMessage({ isOnline }: { isOnline: boolean }) {
  return <p>{isOnline ? 'Online' : 'Offline'}</p>;
}
```

The component describes both possible outputs. It does not search the document for a paragraph and replace its text manually.

### Composition

Small components can be combined into pages and features. A page might contain a search form, a result list, and several result cards, with each part responsible for one piece of the interface.

### One-way data flow

Data normally moves from parent components to child components through props. This direction makes it easier to identify where a value came from and which component owns a change.

### A large ecosystem

React can be combined with routing, data-fetching, testing, styling, and full-stack frameworks. That flexibility is useful, but it also means teams must make architectural choices that a more prescriptive framework might make for them.

## What Is JSX?

JSX is a syntax that lets you write markup inside JavaScript or TypeScript. Most React projects use it because it keeps the structure and display logic of a component together.

```tsx
const product = {
  name: 'Desk lamp',
  price: 39,
};

export function ProductSummary() {
  return (
    <article>
      <h2>{product.name}</h2>
      <p>${product.price.toFixed(2)}</p>
    </article>
  );
}
```

Curly braces switch from markup into a JavaScript expression. JSX looks similar to HTML, but there are differences:

- Use `className` instead of `class`.
- Close tags such as `<img />` and `<input />`.
- Return one enclosing element or a fragment from a component.
- Use camel-cased event names such as `onClick`.

JSX is transformed into JavaScript by the project's build system. Browsers do not execute TSX source files directly.

## What Are Props?

Props are values supplied by a parent component. They work like function arguments and allow one component to customize another.

```tsx
type ProductCardProps = {
  name: string;
  price: number;
};

function ProductCard({ name, price }: ProductCardProps) {
  return (
    <article>
      <h2>{name}</h2>
      <p>${price.toFixed(2)}</p>
    </article>
  );
}

export function ProductList() {
  return (
    <section>
      <h1>Products</h1>
      <ProductCard name="Desk lamp" price={39} />
      <ProductCard name="Task chair" price={249} />
    </section>
  );
}
```

Props are read-only. `ProductCard` should not change `name` or `price`. If the displayed data must change, the component that owns that data provides new props.

## What Is State?

State is data a component remembers between renders. It is useful for information that changes over time, such as an input value, selected tab, expanded panel, or request status.

```tsx
import { useState } from 'react';

export function Counter() {
  const [count, setCount] = useState(0);

  return (
    <div>
      <p>Count: {count}</p>
      <button type="button" onClick={() => setCount(count + 1)}>
        Increase
      </button>
    </div>
  );
}
```

`count` is the value for the current render. Calling `setCount` requests another render with a new value. It does not change the existing `count` variable immediately.

Keep state minimal. If a value can be calculated from existing props or state during rendering, it usually does not need to be stored separately.

## How Do Events Work?

Event handlers are functions that run in response to user actions such as clicking a button, typing in a field, or submitting a form.

```tsx
function SaveButton() {
  function handleClick() {
    console.log('Save requested');
  }

  return (
    <button type="button" onClick={handleClick}>
      Save
    </button>
  );
}
```

Pass the function as the handler: `onClick={handleClick}`. Writing `onClick={handleClick()}` would call it while rendering rather than waiting for a click.

Use native elements for their intended behavior. Buttons perform actions, while links navigate to another location.

## What Are Hooks?

Hooks are functions that let components use React features. Their names begin with `use`.

Common hooks include:

- `useState` for component state
- `useReducer` for more structured state transitions
- `useContext` for reading context supplied above the component
- `useRef` for retaining a value or referring to a DOM element
- `useEffect` for synchronizing with systems outside React

Hooks must be called at the top level of a component or another hook, not inside conditions or loops.

Beginners often reach for `useEffect` too quickly. Rendering calculations, event handling, and state initialization usually do not require an effect. Use an effect when the component must synchronize with something external, such as a browser API, subscription, or non-React widget.

## How Does a React Update Happen?

A simplified React update follows this sequence:

1. The application renders its components.
2. Components return JSX based on their current props and state.
3. React commits the required changes to the browser interface.
4. A user action or external result triggers an event handler.
5. The handler updates state.
6. React renders the affected components again.

Rendering should remain pure: given the same props and state, a component should calculate the same JSX without changing external systems during render.

## A Small Example That Combines the Concepts

The following component combines props, state, an event, conditional rendering, and list rendering:

```tsx
import { useState } from 'react';

type Product = {
  id: string;
  name: string;
};

type ProductSearchProps = {
  products: Product[];
};

export function ProductSearch({ products }: ProductSearchProps) {
  const [query, setQuery] = useState('');
  const normalizedQuery = query.trim().toLowerCase();
  const visibleProducts = products.filter((product) =>
    product.name.toLowerCase().includes(normalizedQuery),
  );

  return (
    <section aria-labelledby="product-search-title">
      <h1 id="product-search-title">Product search</h1>

      <label htmlFor="product-query">Search products</label>
      <input
        id="product-query"
        value={query}
        onChange={(event) => setQuery(event.currentTarget.value)}
      />

      {visibleProducts.length === 0 ? (
        <p>No matching products.</p>
      ) : (
        <ul>
          {visibleProducts.map((product) => (
            <li key={product.id}>{product.name}</li>
          ))}
        </ul>
      )}
    </section>
  );
}
```

The parent supplies `products` through props. The component owns the changing search query as state. The input event updates that state. The filtered list is calculated during rendering rather than stored as duplicate state.

## What React Does Not Provide by Itself

React handles components and rendering, but a production application usually needs more:

- A build tool or React framework
- Routing and URL management
- Data fetching, caching, and server-state conventions
- Styling conventions
- Authentication and server-side authorization
- Testing tools
- Deployment and monitoring

The official React documentation recommends considering a framework for a new application. Building from scratch with a tool such as Vite is also supported, but then the team owns more decisions about routing, data loading, rendering strategies, and code splitting.

React also does not make a client application a trusted security boundary. A server must independently authenticate requests, authorize operations, validate input, and protect data.

## What Are React's Trade-offs?

React's flexibility has costs:

- There may be several reasonable libraries for the same problem.
- Poor state ownership can create unnecessary complexity.
- Large client bundles can slow startup if code splitting and dependencies are ignored.
- Effects and global state can be overused.
- Client-only rendering may not fit every SEO, performance, or content-delivery requirement.

React is not automatically the best choice for every page. A mostly static page with a small interactive control may need only ordinary HTML and JavaScript, or React can be introduced only for that interactive area.

## Common Beginner Mistakes

- Mutating props or state objects instead of creating new values
- Storing values in state that can be calculated during rendering
- Calling event handlers during render
- Using array indexes as keys for lists that can be reordered
- Using `useEffect` for ordinary calculations
- Putting all state at the application root
- Building custom clickable elements instead of semantic buttons and links
- Assuming hidden client controls protect server operations

Treat these as review prompts rather than rules to memorize. Understanding data ownership and observable behavior is more valuable than collecting patterns.

## A Practical Learning Path

After this overview, continue with the concepts that match your immediate work:

1. Learn how to create useful boundaries in [React Component Composition](https://dev.jun-kim.net/2026/10/28/react-component-composition-build-features-from-focused-parts/).
2. Make component contracts explicit with [TypeScript Boundaries in React](https://dev.jun-kim.net/2026/10/28/typescript-boundaries-in-react-props-events-and-api-data/).
3. Add durable URLs through [Client-Side Routing in React](https://dev.jun-kim.net/2026/10/28/client-side-routing-in-react-with-react-router/).
4. Build accessible interfaces with [Accessible React HTML](https://dev.jun-kim.net/2026/10/28/accessible-react-html-landmarks-keyboard-use-and-skip-links/).
5. Protect behavior with [Behavior-Focused React Testing](https://dev.jun-kim.net/2026/10/28/behavior-focused-react-testing-with-testing-library/).
6. Understand browser and API responsibilities through [React and ASP.NET Core Production Boundaries](https://dev.jun-kim.net/2026/10/28/react-and-asp-net-core-production-boundaries/).

The official [React Quick Start](https://react.dev/learn) is also an effective place to practice these fundamentals interactively.

## React in One Mental Model

React can be summarized as a loop:

```text
props + state
      ↓
component renders JSX
      ↓
user sees and interacts with the interface
      ↓
event updates state
      ↓
component renders again
```

Once this loop is clear, the rest of React becomes a set of decisions about component boundaries, state ownership, side effects, data loading, and application architecture.
