---
title: "ChurchServe Angular Tutorial Part 25-1: Runtime Configuration and One Release Artifact"
excerpt: "Load validated public configuration before Angular starts and package the optimized client with ASP.NET Core as one repeatable ChurchServe artifact."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular runtime configuration deployment"
  description: "Load safe Angular runtime configuration and package Angular with ASP.NET Core as one production release artifact."
  socialTitle: "ChurchServe Part 25-1: Runtime Configuration"
  socialDescription: "Build Angular once, validate public deployment settings, and publish the client and API as one artifact."
---

# ChurchServe Angular Tutorial Part 25-1: Runtime Configuration and One Release Artifact

[Part 24](https://dev.jun-kim.net/2026/10/25/churchserve-angular-tutorial-part-24-end-to-end-tests-for-critical-workflows/) proved ChurchServe's critical workflows in a real browser. A release still needs more than passing tests: the optimized Angular files and ASP.NET Core API must become one identifiable artifact, while environment-specific public settings must remain changeable without rebuilding the client.

> **Quick answer:** Fetch a small public JSON document before Angular bootstraps, validate it rather than trusting arbitrary deployment input, use an interceptor to translate the application's stable `/api` contract, and make `dotnet publish` include the already-built Angular output. The result is one artifact that can move between environments without placing secrets in browser code.

## What Changes in Part 25-1

```text
ChurchServe/
├── angular.json
├── package.json
├── proxy.conf.json
├── src/
│   ├── main.ts
│   └── app/
│       ├── app.config.ts
│       └── core/config/
│           ├── runtime-config.ts
│           └── runtime-config.spec.ts
└── server/ChurchServe.Api/
    ├── ChurchServe.Api.csproj
    └── appsettings.json
```

There is no new user workflow. The visible application remains the same; its startup and packaging behavior changes.

## Why Compile-Time Values Are Not Enough

Values imported into an Angular build become public JavaScript and require another build when they change. Compile-time configuration is useful for build choices, but it is neither a secret store nor a good way to promote identical bytes through development, staging, and production.

ChurchServe instead asks its host for this public document:

```json
{
  "apiBaseUrl": "/api",
  "release": "2026.10.25.1"
}
```

`release` identifies what is running. `apiBaseUrl` is restricted to a same-origin absolute path. ChurchServe uses strict same-origin authentication and antiforgery cookies, so accepting an arbitrary origin here would silently change the security and CORS model.

Database passwords, private keys, client secrets, and application passwords must remain server-side. A browser can always inspect this response.

## Load and Validate Configuration

Create `src/app/core/config/runtime-config.ts`:

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';

export interface RuntimeConfig {
  readonly apiBaseUrl: string;
  readonly release: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>(
  'ChurchServe runtime configuration',
);

export async function loadRuntimeConfig(fetchConfig: typeof fetch = fetch): Promise<RuntimeConfig> {
  const response = await fetchConfig('/runtime-config.json', {
    cache: 'no-store',
    credentials: 'same-origin',
  });

  if (!response.ok) {
    throw new Error(`Runtime configuration returned HTTP ${response.status}.`);
  }

  return parseRuntimeConfig(await response.json());
}

export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (!isRecord(value)) {
    throw new Error('Runtime configuration must be a JSON object.');
  }

  const apiBaseUrl = normalizeApiBaseUrl(value['apiBaseUrl']);
  const release = value['release'];
  if (typeof release !== 'string' || release.trim().length === 0 || release.length > 100) {
    throw new Error('Runtime configuration requires a non-empty release identifier.');
  }

  return { apiBaseUrl, release: release.trim() };
}

export const apiBaseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.url !== '/api' && !request.url.startsWith('/api/')) {
    return next(request);
  }

  const { apiBaseUrl } = inject(RUNTIME_CONFIG);
  return next(request.clone({ url: `${apiBaseUrl}${request.url.slice('/api'.length)}` }));
};

function normalizeApiBaseUrl(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.includes('?') ||
    value.includes('#')
  ) {
    throw new Error('apiBaseUrl must be a same-origin absolute path.');
  }

  const normalized = value.length > 1 ? value.replace(/\/+$/, '') : value;
  if (normalized.length === 0) {
    throw new Error('apiBaseUrl must not be empty.');
  }

  return normalized;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
```

The loader uses `fetch` because Angular's providers do not exist yet. `no-store` prevents stale deployment settings, while `same-origin` preserves the cookie boundary. The interceptor changes only `/api` requests; the runtime document and static assets pass through unchanged.

## Bootstrap Only After Configuration Succeeds

Convert `app.config.ts` to a factory:

```typescript
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { SessionStore } from './core/auth/session-store';
import { apiBaseUrlInterceptor, RUNTIME_CONFIG, RuntimeConfig } from './core/config/runtime-config';
import { GlobalErrorHandler } from './core/errors/global-error-handler';

export function createAppConfig(runtimeConfig: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      { provide: ErrorHandler, useClass: GlobalErrorHandler },
      { provide: RUNTIME_CONFIG, useValue: runtimeConfig },
      provideHttpClient(withInterceptors([apiBaseUrlInterceptor])),
      provideAppInitializer(() => firstValueFrom(inject(SessionStore).restore())),
      provideRouter(routes),
    ],
  };
}
```

Then replace `main.ts`:

```typescript
import { bootstrapApplication } from '@angular/platform-browser';
import { createAppConfig } from './app/app.config';
import { App } from './app/app';
import { loadRuntimeConfig } from './app/core/config/runtime-config';

loadRuntimeConfig()
  .then((runtimeConfig) => bootstrapApplication(App, createAppConfig(runtimeConfig)))
  .catch((error: unknown) => {
    console.error('ChurchServe could not start.', error);
    document.body.replaceChildren(createStartupError());
  });

function createStartupError(): HTMLElement {
  const main = document.createElement('main');
  const heading = document.createElement('h1');
  const message = document.createElement('p');
  heading.textContent = 'ChurchServe is unavailable';
  message.textContent = 'The application configuration could not be loaded. Try again later.';
  main.append(heading, message);
  return main;
}
```

Configuration failure is a startup failure. Booting with guessed defaults could send mutations to the wrong path or hide the deployed release identity. The fallback uses fixed `textContent`, not configuration-derived HTML.

## Test Loading, Rejection, and Rewriting

The new `runtime-config.spec.ts` contains five declarations that expand to eight cases because the unsafe-URL check is parameterized. These are its essential assertions:

```typescript
const fetchConfig = vi.fn().mockResolvedValue(
  new Response(JSON.stringify({ apiBaseUrl: '/church-api/', release: '2026.10.25.1' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }),
);

await expect(loadRuntimeConfig(fetchConfig)).resolves.toEqual({
  apiBaseUrl: '/church-api',
  release: '2026.10.25.1',
});

expect(() =>
  parseRuntimeConfig({ apiBaseUrl: 'https://api.example', release: 'test' }),
).toThrow('apiBaseUrl must be a same-origin absolute path.');

TestBed.configureTestingModule({
  providers: [
    { provide: RUNTIME_CONFIG, useValue: { apiBaseUrl: '/church-api', release: 'test' } },
    provideHttpClient(withInterceptors([apiBaseUrlInterceptor])),
    provideHttpClientTesting(),
  ],
});

const client = TestBed.inject(HttpClient);
const http = TestBed.inject(HttpTestingController);
client.get('/api/session').subscribe();
http.expectOne('/church-api/session').flush({});
client.get('/runtime-config.json').subscribe();
http.expectOne('/runtime-config.json').flush({});
```

The complete test file also rejects non-success HTTP responses, blank release identifiers, protocol-relative URLs, query strings, and fragments.

## Keep Development and E2E Proxies Working

Add the new public endpoint and health path beside `/api` in `proxy.conf.json`:

```json
{
  "/api": {
    "target": "http://localhost:5091",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "info"
  },
  "/runtime-config.json": {
    "target": "http://localhost:5091",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "info"
  },
  "/health": {
    "target": "http://localhost:5091",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "info"
  }
}
```

The Playwright proxy receives the equivalent runtime-config entry targeting port `5092`. Part 24's browser journeys now exercise the production-style bootstrap before signing in.

## Generate a Script CSP During the Angular Build

Add this inside Angular's production build configuration:

```json
"security": {
  "autoCsp": true
}
```

Angular documents `security.autoCsp` in its [workspace configuration reference](https://angular.dev/reference/configs/workspace-config) and [security guidance](https://angular.dev/best-practices/security). It generates a hash-based script policy. Part 25-2 adds compatible HTTP directives for styles, connections, framing, forms, objects, and base URLs.

## Include Angular in `dotnet publish`

The API project includes the already-built browser directory during publish:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>churchserve-tutorial-api</UserSecretsId>
    <AngularDist>$(MSBuildProjectDirectory)\..\..\dist\churchserve\browser\</AngularDist>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.13" />
  </ItemGroup>

  <Target Name="IncludeAngularBuild" BeforeTargets="ComputeFilesToPublish">
    <Error Condition="!Exists('$(AngularDist)index.html')"
           Text="Angular production output is missing. Run npm run build before dotnet publish." />
    <ItemGroup>
      <AngularDistFiles Include="$(AngularDist)**\*" />
      <ResolvedFileToPublish Include="@(AngularDistFiles->'%(FullPath)')">
        <RelativePath>wwwroot\%(RecursiveDir)%(Filename)%(Extension)</RelativePath>
        <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
        <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
      </ResolvedFileToPublish>
    </ItemGroup>
  </Target>
</Project>
```

This target does not hide `npm` inside every .NET build. Tests can compile the API without rebuilding Angular, while publish fails clearly if its required client output is missing.

Add the release scripts to `package.json`:

```json
"build:release": "node tools/clean-release.mjs && npm run build && dotnet publish server/ChurchServe.Api/ChurchServe.Api.csproj --configuration Release --output artifacts/publish",
"test:release": "node tools/verify-release.mjs"
```

`tools/clean-release.mjs` removes only the previous generated publish directory before another release:

```javascript
import { rm } from 'node:fs/promises';

await rm('artifacts/publish', { recursive: true, force: true });
```

This prevents a removed hashed asset from surviving in a later local artifact. Ignore `/artifacts`, then build:

```bash
npm ci
npm run build:release
```

`dist/` is intermediate Angular output. `artifacts/publish/` is the framework-dependent release containing the API, dependencies, configuration, and optimized `wwwroot` files.

## Preserve the Authentication Boundary

The artifact does not contain production credentials, TLS certificates, durable storage, or a production identity implementation. The tutorial local account remains disabled outside Development and Testing. A public deployment must configure an appropriate identity provider rather than enable the tutorial password.

Part 25-2 makes this combined artifact behave correctly as a web host, verifies the published files through an executable smoke test, and retains only verified output in CI.
