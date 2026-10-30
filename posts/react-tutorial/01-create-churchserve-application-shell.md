---
title: "ChurchServe React Tutorial Part 1: Create the Application Shell"
excerpt: "Start ChurchServe as a strict React and TypeScript application, add nested routes, build an accessible responsive shell, and verify navigation with user-focused tests."
category: "React Tutorial"

seo:
  focusKeyword: "React application tutorial"
  description: "Create a strict React and TypeScript application with Vite, React Router, an accessible ChurchServe shell, responsive pages, and navigation tests."
  socialTitle: "Build ChurchServe with React: Part 1"
  socialDescription: "Begin a production-oriented React series with Vite, typed route objects, an accessible application shell, and behavior-focused tests."
---

# ChurchServe React Tutorial Part 1: Create the Application Shell

ChurchServe is a volunteer and ministry scheduling application for churches. Coordinators will create services, define required ministry positions, assign available volunteers, and identify gaps before a schedule is released. Volunteers will eventually manage availability, accept assignments, and request substitutes.

This series builds that application once with React and TypeScript. The early parts establish component ownership and browser state, the intermediate parts connect the same interface to an ASP.NET Core API, and the later parts add concurrency, security, accessibility, performance, deployment, and diagnostics.

> **Quick answer:** Create a strict Vite React and TypeScript application, define its URL structure with React Router route objects, keep the root component focused on shared page chrome, and test visible navigation behavior before adding scheduling features.

## Define the Application Boundary

ChurchServe begins with four concepts:

| Concept | Purpose |
| --- | --- |
| Service event | A worship service, rehearsal, youth night, or community event |
| Ministry position | A required role such as greeter, sound operator, or vocalist |
| Volunteer | A person eligible to serve in one or more ministries |
| Assignment | A volunteer filling one position for one event |

The first checkpoint does not model those records yet. It creates the stable application frame they will inhabit: a home page, schedule and volunteer destinations, a not-found route, primary navigation, and an accessible main-content boundary.

The application intentionally excludes donations, payment processing, pastoral notes, attendance surveillance, and detailed information about children. Those areas introduce financial, privacy, and safeguarding obligations beyond a scheduling tutorial.

## Choose the React Application Shape

React's [application creation guidance](https://react.dev/learn/creating-a-react-app) recommends starting production applications with a framework. It also documents a [from-scratch path](https://react.dev/learn/build-a-react-app-from-scratch) with Vite when the application has constraints that justify choosing its routing, data, and deployment pieces directly.

ChurchServe uses that second path for a specific reason: the browser application will later be served with an ASP.NET Core API as one release artifact. It does not need a separate Node.js production server or server-rendered React layer.

The trade-off is real. Vite supplies the development server and production bundling, but ChurchServe must deliberately add routing, data fetching, caching, error handling, and hosting behavior as the series progresses. That is useful teaching material, but it would be unnecessary work for a product whose requirements fit a full-stack React framework better.

This checkpoint uses:

- React 19.3 with strict TypeScript 7.
- Vite 8 for local development and production bundling.
- React Router 8 in Data Mode.
- Vitest 5 with React Testing Library and `user-event`.
- Oxlint for static analysis.

These are the versions in the validated checkpoint, not promises that every later release will use the same major versions. Keep `package-lock.json` committed so each tutorial stage remains reproducible.

## Install the Prerequisites

Install a Node.js version supported by the selected Vite release, then confirm that Node and npm are available:

```bash
node --version
npm --version
```

The validated checkpoint used Node.js 26.8.2 and npm 11.19.1. If a future Vite template requires a different supported Node release, follow the compatibility message printed by the scaffold command rather than copying these versions blindly.

Git is also required if you want each tutorial checkpoint on its own branch:

```bash
git --version
```

## Create the Workspace

Inside an empty repository, create the React and TypeScript workspace:

```bash
npm create vite@latest . -- --template react-ts
npm install
```

Add React Router:

```bash
npm install react-router
```

Add the Part 1 test stack:

```bash
npm install --save-dev vitest jsdom \
  @testing-library/react \
  @testing-library/dom \
  @testing-library/jest-dom \
  @testing-library/user-event
```

Create explicit scripts in `package.json`:

```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "lint": "oxlint",
    "test": "vitest run",
    "test:watch": "vitest",
    "typecheck": "tsc -b --pretty false",
    "preview": "vite preview"
  }
}
```

Vite transforms TypeScript during development, but transformation is not the same as type checking. The `build` and `typecheck` scripts run TypeScript explicitly so invalid types cannot hide behind a successful development-server refresh.

Remove the generated counter, logos, demo artwork, and their unused styles. A production tutorial should begin with the application being built, not retain scaffold code as unexplained baggage.

## Organize the Part 1 Files

After removing the demo files, the source tree is small:

```text
src/
  pages/
    HomePage.tsx
    NotFoundPage.tsx
    PlaceholderPage.tsx
  styles/
    app.css
    global.css
  App.test.tsx
  App.tsx
  main.tsx
  router.tsx
  setupTests.ts
index.html
vite.config.ts
vitest.config.ts
```

This is not a claim that every React project needs folders named `pages` and `styles`. They describe the boundaries present today. Later parts will introduce feature folders when ChurchServe has actual schedule and volunteer behavior to own.

## Define the Routes Outside the Component Tree

Create `src/router.tsx`:

```tsx
import { createBrowserRouter, type RouteObject } from 'react-router'
import { App } from './App'
import { HomePage } from './pages/HomePage'
import { NotFoundPage } from './pages/NotFoundPage'
import { PlaceholderPage } from './pages/PlaceholderPage'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    Component: App,
    children: [
      { index: true, Component: HomePage },
      {
        path: 'schedule',
        element: (
          <PlaceholderPage
            eyebrow="Schedule"
            title="Service schedules are coming next"
          >
            Part 2 will turn service events into reusable components with typed
            props and explicit interaction callbacks.
          </PlaceholderPage>
        ),
      },
      {
        path: 'volunteers',
        element: (
          <PlaceholderPage
            eyebrow="Volunteers"
            title="Volunteer tools are on the way"
          >
            Later parts will model availability, ministry eligibility, search,
            and assignments without changing this application shell.
          </PlaceholderPage>
        ),
      },
      { path: '*', Component: NotFoundPage },
    ],
  },
]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
```

The root route renders `App`, while its children select the content inside that shell. The index child handles `/`; the two named paths provide working destinations before their business features exist; and `*` gives unknown URLs an explicit recovery experience.

Exporting `appRoutes` is intentional. Production creates a browser router, while tests can pass the same route objects to an in-memory router without pretending that `window.history` is the subject under test.

React Router's [Data Mode routing](https://reactrouter.com/start/data/routing) also establishes the path for later loaders, actions, cancellation, error boundaries, and route-level lazy loading. Part 1 does not invent those requirements before the application needs them.

## Bootstrap the Router Once

Replace `src/main.tsx` with:

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import './styles/global.css'
import { createAppRouter } from './router'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={createAppRouter()} />
  </StrictMode>,
)
```

`createAppRouter()` runs during application bootstrap, outside any React component. The router is therefore not recreated during component rendering. `RouterProvider` makes the active route and navigation state available to the component tree.

`StrictMode` enables additional development checks. It can expose effects that are not safe to repeat, which matters once later parts introduce browser persistence and HTTP requests. It is not a substitute for tests or production error handling.

## Build the Shared Application Shell

Replace `src/App.tsx`:

```tsx
import { NavLink, Outlet } from 'react-router'
import './styles/app.css'

function navLinkClassName({ isActive }: { isActive: boolean }) {
  return isActive ? 'active' : undefined
}

export function App() {
  return (
    <div className="application-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>

      <header className="site-header">
        <NavLink className="brand" to="/" aria-label="ChurchServe home">
          <span className="brand-mark" aria-hidden="true">
            CS
          </span>
          <span>ChurchServe</span>
        </NavLink>

        <nav aria-label="Primary navigation">
          <NavLink className={navLinkClassName} to="/schedule">
            Schedule
          </NavLink>
          <NavLink className={navLinkClassName} to="/volunteers">
            Volunteers
          </NavLink>
        </nav>
      </header>

      <main id="main-content" tabIndex={-1}>
        <Outlet />
      </main>

      <footer className="site-footer">
        <p>Thoughtful scheduling for people who serve.</p>
      </footer>
    </div>
  )
}
```

`App` owns only the page chrome shared by every route. `Outlet` marks where the matched child renders. It does not fetch events, manage volunteers, or hold feature state.

`NavLink` receives active-route state and produces the `active` class without a separate location subscription. React Router also adds `aria-current="page"` to the active link.

The skip link and focusable `main` element establish an accessibility boundary early. Part 23 will add route-change focus management after navigation behavior becomes more complex; Part 1 does not claim that `tabIndex={-1}` alone moves focus.

## Create the Home Page

Create `src/pages/HomePage.tsx`:

```tsx
import { Link } from 'react-router'

export function HomePage() {
  return (
    <section className="hero">
      <div className="hero-copy">
        <p className="eyebrow">Volunteer and ministry scheduling</p>
        <h1>Serve together with a schedule everyone can understand.</h1>
        <p className="introduction">
          ChurchServe helps coordinators plan services, organize ministry
          positions, and see where volunteers are still needed.
        </p>

        <div className="actions">
          <Link className="primary-action" to="/schedule">
            View the schedule
          </Link>
          <Link className="secondary-action" to="/volunteers">
            View volunteers
          </Link>
        </div>
      </div>

      <aside className="preview" aria-labelledby="preview-heading">
        <p className="preview-label">Coming in this tutorial</p>
        <h2 id="preview-heading">One application, built in useful steps</h2>
        <ul>
          <li>Plan services and ministry positions</li>
          <li>Coordinate volunteer availability</li>
          <li>Find gaps before publishing a schedule</li>
        </ul>
      </aside>
    </section>
  )
}
```

Internal navigation uses `Link`, so React Router changes the active route without performing a full document request. The prominent actions remain links rather than buttons because they navigate to destinations instead of changing local state.

The `aside` has a visible label through `aria-labelledby`. Semantic elements do not remove the need for readable text, but they give assistive technology a more useful structure than generic containers alone.

## Add Typed Placeholder Pages

Create `src/pages/PlaceholderPage.tsx`:

```tsx
import type { PropsWithChildren } from 'react'

interface PlaceholderPageProps extends PropsWithChildren {
  eyebrow: string
  title: string
}

export function PlaceholderPage({
  children,
  eyebrow,
  title,
}: PlaceholderPageProps) {
  return (
    <section className="placeholder-page">
      <p className="eyebrow">{eyebrow}</p>
      <h1>{title}</h1>
      <p>{children}</p>
    </section>
  )
}
```

This is the first small props contract in the series. `eyebrow` and `title` are required strings, while `PropsWithChildren` adds optional renderable content. Part 2 will use more meaningful domain props and callbacks; Part 1 keeps this component focused on shared placeholder presentation.

Create `src/pages/NotFoundPage.tsx`:

```tsx
import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <section className="not-found-page">
      <p className="eyebrow">Page not found</p>
      <h1>This ChurchServe page does not exist.</h1>
      <p>Return to the application shell and choose an available destination.</p>
      <Link to="/">Return home</Link>
    </section>
  )
}
```

An explicit not-found page gives deep links a predictable result. It will not fix production hosting by itself: the eventual web host must still route unknown client paths to `index.html` without rewriting missing API or static-file requests. Part 25 will configure and verify that boundary.

## Add the Global and Shell Styles

Create `src/styles/global.css`:

```css
:root {
  color: #102a43;
  background: #f8fafc;
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont,
    "Segoe UI", sans-serif;
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

* {
  box-sizing: border-box;
}

html,
body {
  min-width: 20rem;
  min-height: 100%;
  margin: 0;
  background: #f8fafc;
}

button,
input,
select,
textarea {
  font: inherit;
}

a {
  text-underline-offset: 0.18em;
}
```

The global file owns document-wide defaults. Keep page-specific layout out of it so future feature styles do not become dependent on an accidental global selector.

Create `src/styles/app.css`. The complete checkpoint includes the following core layout and interaction rules:

```css
.application-shell {
  display: flex;
  min-height: 100svh;
  flex-direction: column;
}

.skip-link {
  position: fixed;
  z-index: 10;
  top: 0.75rem;
  left: 0.75rem;
  padding: 0.7rem 1rem;
  border-radius: 0.5rem;
  background: #102a43;
  color: #fff;
  font-weight: 700;
  transform: translateY(-150%);
  transition: transform 120ms ease-out;
}

.skip-link:focus {
  transform: translateY(0);
}

.site-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 2rem;
  padding: 1rem clamp(1rem, 4vw, 4rem);
  border-bottom: 1px solid #d9e2ec;
  background: rgb(255 255 255 / 96%);
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 0.75rem;
  color: #102a43;
  font-size: 1.15rem;
  font-weight: 800;
  text-decoration: none;
}

.brand-mark {
  display: grid;
  width: 2.5rem;
  height: 2.5rem;
  place-items: center;
  border-radius: 0.8rem;
  background: #087f5b;
  color: #fff;
  font-size: 0.8rem;
  letter-spacing: 0.04em;
}

.site-header nav {
  display: flex;
  gap: 0.35rem;
}

.site-header nav a {
  padding: 0.65rem 0.85rem;
  border-radius: 0.5rem;
  color: #52667a;
  font-weight: 700;
  text-decoration: none;
}

.site-header nav a:hover,
.site-header nav a.active {
  background: #e6fcf5;
  color: #087f5b;
}

.site-header nav a:focus-visible,
.brand:focus-visible {
  outline: 3px solid #1683c4;
  outline-offset: 3px;
}

main {
  flex: 1;
}

.hero,
.placeholder-page,
.not-found-page {
  width: min(76rem, 100%);
  margin: 0 auto;
  padding: clamp(4rem, 10vw, 8rem) clamp(1.25rem, 5vw, 4rem);
}

.hero {
  display: grid;
  gap: clamp(2rem, 6vw, 5rem);
  grid-template-columns: minmax(0, 1.2fr) minmax(18rem, 0.8fr);
}

.eyebrow,
.preview-label {
  margin: 0 0 1rem;
  color: #087f5b;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.hero h1,
.placeholder-page h1,
.not-found-page h1 {
  margin: 0;
  color: #102a43;
  font-size: clamp(2.6rem, 6.5vw, 5.4rem);
  letter-spacing: -0.055em;
  line-height: 0.98;
}

.introduction,
.placeholder-page > p,
.not-found-page > p {
  max-width: 42rem;
  margin: 1.75rem 0 0;
  color: #52667a;
  font-size: clamp(1.05rem, 2vw, 1.3rem);
  line-height: 1.7;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 2rem;
}

.actions a,
.not-found-page a {
  display: inline-block;
  padding: 0.85rem 1.1rem;
  border: 2px solid #087f5b;
  border-radius: 0.65rem;
  font-weight: 800;
  text-decoration: none;
}

.primary-action,
.not-found-page a {
  background: #087f5b;
  color: #fff;
}

.secondary-action {
  color: #087f5b;
}

.preview {
  padding: clamp(1.5rem, 4vw, 2.5rem);
  border: 1px solid #c3e8dd;
  border-radius: 1.5rem;
  background: #fff;
  box-shadow: 0 1.5rem 4rem rgb(16 42 67 / 10%);
}

.preview h2 {
  margin: 0;
  color: #102a43;
  font-size: 1.6rem;
  line-height: 1.2;
}

.preview ul {
  display: grid;
  margin: 1.5rem 0 0;
  padding: 0;
  gap: 0.85rem;
  list-style: none;
}

.preview li {
  padding: 0.9rem 1rem;
  border-radius: 0.75rem;
  background: #f1f8f6;
  line-height: 1.45;
}

.site-footer {
  padding: 1.5rem clamp(1rem, 4vw, 4rem);
  border-top: 1px solid #d9e2ec;
  color: #627d98;
  font-size: 0.9rem;
}

@media (max-width: 48rem) {
  .site-header {
    align-items: flex-start;
    flex-direction: column;
    gap: 0.75rem;
  }

  .hero {
    grid-template-columns: 1fr;
  }
}
```

The layout uses fluid `clamp()` values and one structural breakpoint instead of a collection of device-specific widths. The skip link remains off-screen until focused, and visible keyboard focus uses a color with stronger contrast than the decorative pale green.

## Configure the Test Environment

Create `vitest.config.ts` at the repository root:

```typescript
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
  },
})
```

`jsdom` provides DOM APIs for component tests without starting a browser. It does not prove layout, CSS rendering, or every browser behavior; Playwright will cover critical real-browser journeys later. Vitest's [testing guide](https://vitest.dev/guide/) describes the runner and its Vite integration.

Create `src/setupTests.ts`:

```typescript
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => {
  cleanup()
})
```

Explicit cleanup keeps every test isolated. Without it, rendered trees can accumulate in the shared test document and make accessible queries report duplicate elements.

## Test Visible Navigation Behavior

Create `src/App.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { appRoutes } from './router'

function renderRoute(path = '/') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  render(<RouterProvider router={router} />)
}

describe('ChurchServe application shell', () => {
  it('renders the application name, primary navigation, and home introduction', () => {
    renderRoute()

    expect(screen.getByRole('link', { name: 'ChurchServe home' })).toBeVisible()
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeVisible()
    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'Serve together with a schedule everyone can understand.',
      }),
    ).toBeVisible()
  })

  it('navigates to the schedule without replacing the application shell', async () => {
    const user = userEvent.setup()
    renderRoute()

    await user.click(screen.getByRole('link', { name: 'View the schedule' }))

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: 'Service schedules are coming next',
      }),
    ).toBeVisible()
    expect(screen.getByRole('link', { name: 'ChurchServe home' })).toBeVisible()
  })

  it('provides a recovery link for unknown routes', () => {
    renderRoute('/missing')

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'This ChurchServe page does not exist.',
      }),
    ).toBeVisible()
    expect(screen.getByRole('link', { name: 'Return home' })).toHaveAttribute(
      'href',
      '/',
    )
  })
})
```

The tests query links, landmarks, and headings by their accessible roles and names. They do not inspect component instances or private implementation details. The navigation test clicks the same link a reader uses and confirms that routed content changes while the application shell remains.

The in-memory router receives the production route objects, so the test covers the actual nesting and not-found configuration without making browser history global across tests.

## Run the Part 1 Quality Gate

Run every check from the repository root:

```bash
npm run lint
npm run typecheck
npm test
npm run build
```

The validated Part 1 checkpoint completed with:

- No Oxlint findings.
- A successful strict TypeScript project build.
- Three passing Vitest tests in one test file.
- A successful Vite production build containing 98 transformed modules.
- No reported npm vulnerabilities in the installed dependency tree.

Run the application locally with:

```bash
npm run dev
```

Open the URL printed by Vite, normally `http://localhost:5173`. Use `Ctrl+C` to stop the development server.

## Production Boundaries

Part 1 is a client-only shell. It does not yet provide persistent data, authentication, authorization, service-event validation, error recovery, or browser telemetry.

The placeholder routes are honest about that boundary. They do not simulate successful scheduling with hard-coded forms or imply that client-side navigation protects data. Later API endpoints will validate input, enforce permissions, assign durable identifiers, and remain authoritative even if a user bypasses the React interface.

The not-found route is also only half of deep-link support. Production hosting must serve the SPA entry document for valid client routes while preserving real `404` responses for missing API and asset requests. That distinction will be implemented and smoke-tested with the final release artifact.

## Part 1 Checkpoint

ChurchServe now has a strict React and TypeScript workspace, one shared application shell, nested route objects, working schedule and volunteer destinations, an explicit not-found experience, responsive presentation, keyboard-visible navigation, and tests focused on what a user can observe.

Part 2 will introduce the first domain model and build reusable service-event cards with typed props, callback ownership, responsive styling, and interaction-focused tests.
