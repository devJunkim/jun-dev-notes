---
title: "Strangler Fig Modernization: Replacing a Legacy System Incrementally"
excerpt: "Modernize a legacy application through routed capability slices, explicit data ownership, reversible cutovers, and evidence-based decommissioning."
category: "Architecture"

seo:
  focusKeyword: "strangler fig pattern"
  description: "Apply the strangler fig pattern with capability boundaries, routing, data migration, rollback, observability, and safe legacy decommissioning."
  socialTitle: "Strangler Fig Modernization Without a Big-Bang Rewrite"
  socialDescription: "Replace a legacy system one observable capability at a time while preserving client contracts and rollback options."
---

# Strangler Fig Modernization: Replacing a Legacy System Incrementally

A rewrite reaches year two with no production traffic. The legacy system keeps changing, the replacement keeps chasing it, and the final cutover becomes riskier with every month.

The strangler fig pattern changes the unit of progress. A façade keeps the client contract stable while individual capabilities move to a new implementation and prove themselves under real traffic.

> **Quick answer:** Put a controlled routing boundary in front of the legacy system, choose one cohesive capability with measurable behavior, and migrate its code and data through a reversible cutover. Retire the old path only after dependencies, traffic, reconciliation, and rollback evidence show that it is no longer needed.

## Start With a Capability, Not a Code Layer

“Move the data-access layer” is not an independently valuable migration slice. It often creates a new shared library while the same deployment and database coupling remain.

A better slice has a recognizable business contract: customer notification preferences, invoice rendering, product search, or document download. It has inputs, outputs, ownership, traffic, and success measures that can move together.

The [AWS strangler fig guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/strangler-fig.html) describes a proxy that routes requests between the monolith and new services while clients continue using the same interface. The target does not have to be microservices; a modular replacement can be the safer architecture.

Choose an early slice with limited dependencies and meaningful value. The easiest code may be a poor first cut if it shares every table and workflow with the monolith.

## Make the Routing Boundary Observable

The façade can be an API gateway, reverse proxy, adapter, or module-level dispatch boundary. Its routing rule must be explicit and reversible:

```yaml
routes:
  - capability: invoice-rendering
    match:
      path: /invoices/{id}/document
    destination: new-invoice-service
    fallback: legacy-application
    rolloutPercent: 10
```

This is illustrative configuration, not a recommendation to fall back automatically. Retrying a failed write against the legacy system can duplicate effects or apply different rules. Fallback may be safe for a read-only idempotent operation and unsafe for a command.

Log the selected route, capability, implementation version, outcome, and latency with a shared correlation ID. Do not put customer identifiers into high-cardinality metrics.

Keep routing independent from the client. Requiring every consumer to learn which implementation owns each feature moves migration complexity outward and makes rollback slower.

## Preserve the Contract Before Improving It

The first replacement should match observable behavior that clients rely on: status codes, validation, ordering, rounding, time zones, authorization, error identifiers, and retry semantics. This includes inconvenient behavior that must be changed through a separately planned contract migration. [Backward-Compatible API Evolution](https://dev.jun-kim.net/2026/09/22/backward-compatible-api-evolution-changing-contracts-without-breaking-clients/) covers that client-facing migration boundary in detail.

Use characterization tests and sampled production examples with sensitive data removed. Shadow reads can compare results without serving the new response, but they must not trigger writes or external side effects.

A façade is also an anti-corruption boundary. Translate legacy concepts into the new domain model rather than spreading old database names and status codes throughout the replacement. Keep the translation explicit so it can be tested and eventually removed.

## Move Data Ownership Deliberately

Code routing is usually easier than data migration. Decide the system of record for every phase:

| Phase | Writes | Reads | Recovery |
| --- | --- | --- | --- |
| Legacy owned | Legacy only | Legacy | Existing recovery |
| Copy and validate | One authoritative writer plus replication | Compare without serving | Rebuild copy |
| New owned | New only | New, with monitored rollback window | Route back only if data can reconcile |
| Retired | New only | New | Restore from new-system backups |

Avoid unconstrained dual writes from application code. One write can succeed while the other fails, leaving no durable repair instruction. Prefer change data capture, an outbox, or another replayable synchronization mechanism with lag and error monitoring.

Reconcile counts, identifiers, aggregates, and business invariants—not just row totals. Define what happens to changes made during backfill and how deletions are represented.

## Cut Over With Explicit Gates

A useful rollout sequence is:

1. Deploy the replacement with no production routing.
2. Validate contracts and data synchronization.
3. Shadow safe reads and compare results.
4. Route internal or test tenants.
5. Increase traffic in measured steps.
6. Stop legacy writes for the capability.
7. Observe through a defined stability window.
8. Remove legacy code and data only after dependency evidence is clean.

Define rollback before each gate. Once the new system writes data the legacy model cannot represent, routing back is not a switch; it is a data recovery project.

Canary metrics should cover business outcomes as well as HTTP health: documents produced, payments reconciled, notifications delivered, and support contacts. A fast `200` with the wrong invoice is not a successful migration.

## Control the Temporary Architecture

The façade, synchronization jobs, duplicate models, and compatibility code are migration scaffolding. Without ownership and removal criteria, they become the permanent architecture.

Track each capability with an owner, current system of record, routed percentage, dependency list, rollback boundary, target retirement date, and blocking evidence. Budget operational capacity for both systems while they coexist.

Do not extract a capability merely to adopt new technology. Independent deployment adds network failure, authorization, observability, and data-consistency costs. Keep a modular boundary inside one process when it meets the actual goals.

## Prove the Legacy Path Is Unused

Before decommissioning, verify zero intended traffic over a meaningful window, no batch jobs or direct database clients remain, backups and audits have moved, operational runbooks reference the new system, and compliance retention is satisfied.

Disable the path reversibly before deleting it. Watch for seasonal jobs and rarely used administrative operations. Then remove credentials, routes, infrastructure, data copies, alerts, and ownership—not only application code.

The pattern succeeds when each slice reduces legacy responsibility and temporary complexity. Shipping a new service while all authoritative work still happens in the monolith is coexistence, not strangling.
