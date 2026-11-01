---
title: "ChurchServe React Tutorial Part 25-1: Runtime Configuration and One Release Artifact"
excerpt: "Load validated public configuration before React starts and package the optimized Vite client with ASP.NET Core as one repeatable ChurchServe artifact."
category: "React Tutorial"

seo:
  focusKeyword: "React runtime configuration deployment"
  description: "Load safe React runtime configuration and package a Vite client with ASP.NET Core as one production release artifact."
  socialTitle: "ChurchServe React Part 25-1: Runtime Configuration"
  socialDescription: "Build React once, validate public deployment settings, and publish the Vite client and ASP.NET Core API as one artifact."
---

# ChurchServe React Tutorial Part 25-1: Runtime Configuration and One Release Artifact

[Part 24](https://dev.jun-kim.net/2026/11/01/churchserve-react-tutorial-part-24-end-to-end-tests-for-critical-workflows/) proved ChurchServe’s critical workflows in Chromium. Passing tests still does not produce deployable bytes. The optimized React files and ASP.NET Core API need to become one identifiable artifact, while safe environment-specific settings must remain changeable without rebuilding the browser bundle.

> **Quick answer:** Fetch a small public JSON document before calling React’s `createRoot`, reject unsafe values, pass the validated API path through the router’s existing composition boundary, and make `dotnet publish` include the already-built Vite output. One artifact can then move between environments without pretending browser configuration can hold secrets.

## What Part 25-1 Changes

```text
ChurchServeReact/
├── package.json
├── src/
│   ├── main.tsx
│   ├── router.tsx
│   └── core/config/
│       ├── runtimeConfig.ts
│       └── runtimeConfig.test.ts
├── server/ChurchServe.Api/
│   ├── ChurchServe.Api.csproj
│   └── appsettings.json
└── tools/
    └── clean-release.mjs
```

There is no new user workflow. This checkpoint changes startup, dependency composition, and packaging.

## Separate Build Choices from Deployment Settings

Vite replaces `import.meta.env` values when it builds the browser application. Its [environment variable guidance](https://vite.dev/guide/env-and-mode) also makes two security facts explicit: `VITE_*` values become client code, and they must not contain secrets.

Build-time substitution is useful for compile-time behavior. It is less useful when the same reviewed artifact must move from test to staging to production. Rebuilding for each environment creates different bytes and weakens the connection between what CI verified and what operators deployed.

ChurchServe instead asks its host for this public document:

```json
{
  "apiBaseUrl": "/api",
  "release": "2026.11.01.1"
}
```

`release` identifies the deployed artifact. `apiBaseUrl` is limited to a same-origin absolute path because ChurchServe uses same-origin cookies and antiforgery protection. Database passwords, signing keys, identity-provider secrets, and connection strings remain server-side. Anyone with a browser can inspect this response. [React and ASP.NET Core Production Boundaries](https://dev.jun-kim.net/2026/10/28/react-and-asp-net-core-production-boundaries/) explains the broader trust model; this tutorial implements its deployment seam in executable code.

## Load and Validate Before React Starts

Create `src/core/config/runtimeConfig.ts`:

```typescript
export type RuntimeConfig = {
  readonly apiBaseUrl: string
  readonly release: string
}

type Fetch = typeof fetch

export async function loadRuntimeConfig(
  fetchConfig: Fetch = globalThis.fetch.bind(globalThis),
): Promise<RuntimeConfig> {
  const response = await fetchConfig('/runtime-config.json', {
    cache: 'no-store',
    credentials: 'same-origin',
  })

  if (!response.ok) {
    throw new Error(`Runtime configuration returned HTTP ${response.status}.`)
  }

  return parseRuntimeConfig(await response.json())
}
```

The loader does not guess when the server returns an error. `no-store` avoids retaining a previous deployment’s settings, and `same-origin` preserves the established cookie boundary.

Treat parsed JSON as `unknown`:

```typescript
export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (!isRecord(value)) {
    throw new Error('Runtime configuration must be a JSON object.')
  }

  const apiBaseUrl = normalizeApiBaseUrl(value.apiBaseUrl)
  const release = value.release
  if (
    typeof release !== 'string' ||
    release.trim().length === 0 ||
    release.length > 100
  ) {
    throw new Error('Runtime configuration requires a non-empty release identifier.')
  }

  return { apiBaseUrl, release: release.trim() }
}
```

The path validator rejects full origins, protocol-relative URLs, query strings, fragments, backslashes, and surrounding whitespace. It removes trailing slashes after validation. This is narrower than accepting an arbitrary URL by design.

Joining a configured root path also needs care:

```typescript
export function appendApiPath(apiBaseUrl: string, path: `/${string}`) {
  return `${apiBaseUrl === '/' ? '' : apiBaseUrl}${path}`
}
```

Without the root-path case, concatenating `'/'` and `'/session'` would produce `//session`, which browsers interpret as a protocol-relative URL.

## Compose HTTP Clients from Validated Configuration

React does not provide Angular-style HTTP interceptors, and ChurchServe does not need one. Part 10 already put concrete HTTP clients at the router composition boundary. Part 25 passes the API path through that seam:

```typescript
export function createAppRouter(runtimeConfig: RuntimeConfig) {
  return createBrowserRouter(
    createAppRoutes({
      apiBaseUrl: runtimeConfig.apiBaseUrl,
      sessionClient: new HttpSessionClient(
        appendApiPath(runtimeConfig.apiBaseUrl, '/session'),
      ),
    }),
  )
}
```

The schedule feature remains lazy. When React Router requests it, the loader passes the same validated base path into the feature factory:

```typescript
const loadProductionScheduleFeature: ScheduleFeatureLoader = async () => {
  const feature = await import('./features/schedule/scheduleFeature')
  return feature.createScheduleFeatureForApi(apiBaseUrl)
}
```

The feature factory constructs both repositories with one service-event URL:

```typescript
export function createScheduleFeatureForApi(apiBaseUrl: string) {
  const serviceEventsUrl = appendApiPath(apiBaseUrl, '/service-events')
  return createScheduleFeature(
    new HttpServiceEventRepository(serviceEventsUrl),
    new HttpAssignmentRepository(serviceEventsUrl),
  )
}
```

Tests that inject fake clients and repositories remain independent of deployment configuration. Production dependencies receive the validated value once.

## Bootstrap Only After Configuration Succeeds

Update `main.tsx` so configuration is a startup dependency:

```tsx
loadRuntimeConfig()
  .then((runtimeConfig) => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <RouterProvider router={createAppRouter(runtimeConfig)} />
      </StrictMode>,
    )
  })
  .catch((error: unknown) => {
    console.error('ChurchServe could not start.', error)
    document.body.replaceChildren(createStartupError())
  })
```

React’s [`createRoot` reference](https://react.dev/reference/react-dom/client/createRoot) describes the root as the point where React takes ownership of a browser DOM node. Delaying that call means no component can issue an API request before configuration is known.

The failure branch renders fixed text with DOM APIs:

```typescript
function createStartupError() {
  const main = document.createElement('main')
  const heading = document.createElement('h1')
  const message = document.createElement('p')
  heading.textContent = 'ChurchServe is unavailable'
  message.textContent =
    'The application configuration could not be loaded. Try again later.'
  main.append(heading, message)
  return main
}
```

Configuration failure is not an anonymous session or a recoverable page request. Booting with a guessed API path could direct mutations to the wrong endpoint. The fallback therefore stays outside React and does not interpolate untrusted response content.

## Test the Public Contract

`runtimeConfig.test.ts` verifies successful loading, `no-store`, same-origin credentials, HTTP failures, missing release IDs, unsafe paths, normalization, and root-path joining. The unsafe path test is parameterized:

```typescript
it.each([
  'https://api.example',
  '//api.example',
  '/api?tenant=one',
  '/api#fragment',
  '/api\\nested',
  '/api ',
])('rejects unsafe API base URL %s', (apiBaseUrl) => {
  expect(() => parseRuntimeConfig({ apiBaseUrl, release: 'test' })).toThrow(
    'apiBaseUrl must be a same-origin absolute path.',
  )
})
```

These tests protect the browser boundary. Part 25-2 validates the corresponding server settings at startup.

## Keep Development and Browser Tests on the Same Startup Path

Vite must proxy the runtime document in addition to API traffic:

```typescript
server: {
  proxy: {
    '/api': process.env.CHURCHSERVE_API_URL ?? 'http://localhost:5100',
    '/health': process.env.CHURCHSERVE_API_URL ?? 'http://localhost:5100',
    '/runtime-config.json':
      process.env.CHURCHSERVE_API_URL ?? 'http://localhost:5100',
  },
},
```

Part 24’s Playwright suite now reaches the same configuration loader used by the final artifact. It does not bypass production startup with a test-only constant.

## Include the Vite Build in `dotnet publish`

The API project names the optimized client directory:

```xml
<ReactDist>$(MSBuildProjectDirectory)\..\..\dist\</ReactDist>
```

Then a publish target requires `dist/index.html` and places every Vite file under the published web root:

```xml
<Target Name="IncludeReactBuild" BeforeTargets="ComputeFilesToPublish">
  <Error Condition="!Exists('$(ReactDist)index.html')"
         Text="React production output is missing. Run npm run build before dotnet publish." />
  <ItemGroup>
    <ReactDistFiles Include="$(ReactDist)**\*" />
    <ResolvedFileToPublish Include="@(ReactDistFiles->'%(FullPath)')">
      <RelativePath>wwwroot\%(RecursiveDir)%(Filename)%(Extension)</RelativePath>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </ResolvedFileToPublish>
  </ItemGroup>
</Target>
```

This target does not run npm during every .NET build. Unit and API tests can compile normally, while publishing fails clearly if its required optimized client is missing.

Add repeatable release commands:

```json
{
  "scripts": {
    "build:release": "node tools/clean-release.mjs && npm run build && dotnet publish server/ChurchServe.Api/ChurchServe.Api.csproj --configuration Release --output artifacts/publish",
    "test:release": "node tools/verify-release.mjs"
  }
}
```

`clean-release.mjs` removes only the previous generated publish directory. That prevents an obsolete hashed asset from surviving into the next local artifact. Both `/artifacts` and Vite’s `/dist` output remain ignored by Git.

Run the packaging step with:

```bash
npm ci
npm run build:release
```

`dist` is intermediate Vite output. `artifacts/publish` is the framework-dependent release containing the API, dependencies, server configuration, and optimized React application.

Part 25-2 makes this combined artifact behave like a production web host and verifies the published output rather than trusting source-level tests alone.
