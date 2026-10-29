---
title: "React and ASP.NET Core Production Boundaries"
excerpt: "Separate React browser responsibilities from ASP.NET Core authentication, authorization, validation, data, and operational ownership."
category: "React"

seo:
  focusKeyword: "React ASP.NET Core architecture"
  description: "Define secure production boundaries between a React client and ASP.NET Core API for authentication, authorization, validation, and deployment."
  socialTitle: "React and ASP.NET Core: Draw the Production Boundary"
  socialDescription: "Keep browser UX in React and server authority in ASP.NET Core with explicit contracts, security, configuration, and operations."
---

# React and ASP.NET Core Production Boundaries

A React interface and an ASP.NET Core API run in different trust environments. React code executes on a user's device and can be inspected or bypassed. The API runs on infrastructure you control and must protect data and business invariants independently.

> **Quick answer:** Let React own browser rendering, interaction, navigation, and temporary UI state. Let ASP.NET Core own identity enforcement, authorization, validation, business rules, persistence, and trusted integration. Connect them through explicit HTTP contracts, and design authentication, errors, configuration, observability, and deployment as one production system.

## Draw the Trust Boundary First

```text
Untrusted user environment                 Trusted server environment

Browser / React application  -- HTTPS -->  ASP.NET Core API
  - renders UI                              - authenticates requests
  - gathers input                           - authorizes operations
  - manages navigation                      - validates commands
  - improves user experience                - enforces business rules
                                             - owns persistence
```

The browser is not trusted merely because your team shipped its JavaScript. A user can alter client state, call endpoints directly, or replay requests.

## Give React Browser Responsibilities

React should own concerns that belong to the interactive client:

- Components and page composition
- Client-side routes and navigation
- Form interaction and immediate feedback
- Loading, empty, error, and optimistic states
- Temporary view preferences
- Accessibility behavior

Client validation improves feedback but does not replace server validation. Disabling an Assign button also does not prevent a direct HTTP request.

## Give ASP.NET Core Server Authority

The API should own decisions that must remain true regardless of the client:

- Authentication and session validation
- Role, policy, and resource-based authorization
- Command and model validation
- Inventory and order-total rules
- Database transactions and concurrency control
- Audit records and trusted timestamps
- Access to secrets and external service credentials

Authentication answers who the caller is. Authorization answers what that caller may do. Configuring sign-in alone does not automatically protect every endpoint; authorization policy must be applied intentionally.

## Design Explicit HTTP Contracts

Use request and response models shaped for the interaction instead of exposing database entities.

```json
POST /api/orders
{
  "productId": "desk-lamp",
  "quantity": 2,
  "expectedUnitPrice": 39.00
}
```

```json
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{
  "type": "https://api.example.com/problems/inventory-conflict",
  "title": "The requested quantity is unavailable",
  "status": 409,
  "detail": "Only one item remains in inventory."
}
```

Status codes and Problem Details let the React client distinguish validation, authentication, authorization, conflict, and unexpected failures without parsing ad hoc strings.

Version contracts when compatibility requires it, and evolve them additively where practical. A coordinated monorepo does not guarantee that every browser refreshes at deployment time.

## Choose Authentication with the Deployment Model

For a browser application, secure HTTP-only cookies can keep session credentials unavailable to ordinary JavaScript. They require deliberate CSRF defenses and cookie settings. Bearer tokens have different storage, refresh, and exposure trade-offs.

Do not store long-lived credentials in source code, Vite environment variables, or browser storage merely for convenience. Values bundled into the React application are public to its users.

Whichever approach you choose:

- Use HTTPS.
- Keep server secrets only on the server.
- Apply least privilege.
- Handle session expiry as an expected state.
- Protect state-changing requests against the relevant threats.
- Authorize the requested resource, not just the route name.

## Decide Same-Origin and Cross-Origin Hosting

Serving React and the API from one origin simplifies cookies and removes many CORS concerns:

```text
https://app.example.com/        React static files
https://app.example.com/api/*   ASP.NET Core API
```

Separate origins may be appropriate for independent deployment, but then CORS must allow only intended origins, methods, and headers. CORS is a browser access policy, not authentication or authorization.

The static host also needs a history fallback for React-owned routes while preserving `/api/*` status codes.

## Keep Runtime Configuration Honest

Vite build-time variables are embedded during the build. They are appropriate for public settings such as an API base URL, not secrets.

If one artifact must move across environments, load public runtime configuration before bootstrapping React or serve it from a known endpoint. Validate required values and fail clearly instead of silently calling the wrong environment.

The server should use its normal secure configuration providers for connection strings, signing material, and external credentials.

## Model Failures Across the Boundary

React should distinguish at least:

- Offline or network failure
- Request cancellation
- Unauthenticated response
- Forbidden response
- Validation problem
- Concurrency conflict
- Unexpected server failure

Do not display raw exception details to users. Preserve a correlation or trace identifier so support can connect a safe client message to server telemetry.

Use retries only for operations that are safe to repeat or protected with idempotency. Retrying a non-idempotent order command blindly can create duplicate work.

## Observe the Whole Request

Production diagnosis needs both sides of the boundary:

- Browser error and performance telemetry with privacy controls
- API structured logs, metrics, and traces
- Correlation across the browser request and server operation
- Release version identifiers for both artifacts
- Health checks that reflect server dependencies appropriately

Avoid placing personal or sensitive data in URLs, logs, analytics events, and exception messages.

## Test the Contract at Multiple Levels

React component tests can fake the API boundary and verify loading, success, conflict, and forbidden UI. ASP.NET Core integration tests should verify authentication, authorization, validation, persistence behavior, and Problem Details.

Add a small number of end-to-end tests that exercise the deployed browser and API together. Contract drift often appears only when separately passing client and server builds meet.

## Review the Production Boundary

- Can every protected API operation defend itself without the React UI?
- Are server secrets absent from browser assets?
- Are request and response contracts explicit and version tolerant?
- Are authentication expiry and authorization denial handled distinctly?
- Does hosting preserve both SPA routes and real API errors?
- Can telemetry correlate a browser failure with a server request safely?
- Are retries limited to safe operations?

The React and ASP.NET Core boundary is not just a deployment seam. It is the system's primary trust boundary. Clear ownership makes the interface responsive while keeping security and business correctness on infrastructure you control.

For the server model, see Microsoft's official introduction to [authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction).
