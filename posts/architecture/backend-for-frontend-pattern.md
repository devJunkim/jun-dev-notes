---
title: "Backend for Frontend Pattern: Client-Specific APIs Without a Shared Bottleneck"
excerpt: "Use the Backend for Frontend pattern to tailor APIs for web and mobile clients while controlling duplication, authorization, latency, and operational overhead."
category: "Architecture"

seo:
  focusKeyword: "Backend for Frontend pattern"
  description: "Apply the Backend for Frontend pattern with clear ownership, aggregation boundaries, authorization, resilience, and criteria for avoiding unnecessary services."
  socialTitle: "Backend for Frontend: Useful Boundary or Extra Hop?"
  socialDescription: "Tailor APIs to client needs without moving domain logic into another distributed monolith."
---

# Backend for Frontend Pattern: Client-Specific APIs Without a Shared Bottleneck

A mobile app needs compact payloads and coarse operations, while a desktop application needs richer data and different release timing. Forcing both through one general-purpose API can turn every client change into a cross-team negotiation.

> **Quick answer:** Introduce a Backend for Frontend (BFF) when clients have meaningfully different workflows, payloads, or ownership. Keep client orchestration and response shaping in the BFF, leave business invariants in domain services, enforce authorization at every trust boundary, and include the extra hop in reliability and latency budgets.

## A BFF Is a Client Boundary

The [Azure Architecture Center](https://learn.microsoft.com/azure/architecture/patterns/backends-for-frontends) describes a separate backend tailored to each frontend interface. A typical flow is:

```text
Web client    -> Web BFF    -> Domain APIs
Mobile client -> Mobile BFF -> Domain APIs
```

The BFF can aggregate several downstream calls, translate transport models into a client-focused response, apply client-specific caching, and hide backend topology. The frontend team can evolve that contract without adding every presentation concern to shared services.

This is not automatically one service per screen or device model. Split only where requirements and ownership diverge enough to justify another deployable component.

## Keep Business Rules Behind the Boundary

A BFF may decide that a mobile dashboard needs five recent orders and a compact customer summary. It should not independently decide whether an order can be cancelled or how a refund is calculated.

Client-specific responsibilities include:

- response aggregation and shaping;
- protocol adaptation and pagination defaults;
- client-focused caching and bandwidth reduction;
- orchestration of already-authorized downstream operations.

Shared domain responsibilities include invariants, financial calculations, durable workflow state, and authorization rules that must hold for every caller. Duplicating those rules across BFFs creates inconsistent behavior and makes non-UI consumers unsafe.

## Design Authorization End to End

The edge gateway or BFF can validate an access token, but downstream services must still protect their operations. Propagate identity and authorization context using a defined service-to-service mechanism; do not trust user-controlled headers that merely claim a role or tenant.

Give each BFF only the downstream permissions its client requires. A narrower service identity can reduce blast radius, but only if the underlying APIs enforce it. Apply rate limits and payload validation at the public boundary, and avoid logging tokens or sensitive aggregated responses.

Browser-facing BFFs are sometimes used to keep tokens out of JavaScript by maintaining a server-side session. That design requires CSRF defenses, secure cookie settings, session expiration, logout behavior, and protection against session fixation. The BFF pattern alone supplies none of those controls.

## Budget the Extra Failure Surface

Aggregation can reduce client round trips while increasing server-side coupling. If one response calls four services serially, its latency and availability inherit all four dependencies.

Set an end-to-end deadline, run independent calls concurrently where safe, and cancel remaining work when the client disconnects or the deadline expires. Retries must fit inside that deadline and should target only transient, idempotent operations. Define whether partial data is acceptable before a dependency fails.

Observe the complete request with correlated traces, dependency timings, cache outcomes, and stable error identifiers. A generic BFF `500` without downstream context simply moves diagnosis one layer farther away.

## Control Duplication Deliberately

Two BFFs may repeat authentication plumbing, telemetry, or SDK configuration. Shared libraries can help with stable cross-cutting mechanics, but sharing client orchestration aggressively recreates the coupling the pattern was intended to remove.

Prefer a common platform for logging, security defaults, and deployment templates while allowing response composition to remain client owned. Track duplicated domain decisions as defects, not as an expected consequence of separate BFFs.

Each BFF needs its own deployment, scaling, patching, on-call ownership, SLOs, and security review. Include that operational cost in the architecture decision rather than treating the service as a free adapter.

## Test Contracts and Failure Modes

Test the BFF contract from the client perspective and use stubbed or containerized dependencies to exercise timeouts, partial failures, stale caches, and downstream schema changes. Contract tests should protect fields and behavior the client relies on without freezing incidental implementation details.

Load tests need realistic fan-out. One incoming request that creates five downstream calls multiplies connection pressure and rate-limit consumption. Capacity planning must follow downstream work, not only public request count.

## Know When Not to Add One

Avoid a BFF when there is one client, clients make essentially identical requests, or the team cannot operate another service reliably. A gateway aggregation policy, a well-designed shared API, or client-selectable fields may solve a smaller problem.

GraphQL can let clients select data shapes, but it does not automatically solve authorization, backend fan-out, ownership, or workflow orchestration. Choose it for its query model, not merely to avoid naming a BFF.

The pattern succeeds when it gives a frontend autonomy without stealing responsibility from the domain. If it becomes a general integration layer for every consumer, it is no longer a focused backend for one frontend—it is another shared bottleneck.
