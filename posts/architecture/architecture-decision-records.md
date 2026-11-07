---
title: "Architecture Decision Records: Capture Context, Trade-Offs, and Consequences"
excerpt: "Use architecture decision records to preserve why significant choices were made without turning design documentation into an approval bottleneck."
category: "Architecture"

seo:
  focusKeyword: "architecture decision records"
  description: "Create useful architecture decision records with clear context, options, consequences, lifecycle states, ownership, and review practices."
  socialTitle: "Architecture Decision Records That Teams Actually Use"
  socialDescription: "Preserve significant design reasoning with small, versioned records instead of repeatedly reopening decisions without context."
---

# Architecture Decision Records: Capture Context, Trade-Offs, and Consequences

A team replaces a queue with synchronous HTTP. Six months later, an incident reveals that the queue originally isolated a fragile dependency. The code shows what changed, but the constraints and trade-offs that justified the earlier design disappeared.

> **Quick answer:** Write an architecture decision record when a choice materially affects system structure, quality attributes, dependencies, interfaces, or delivery constraints. Record the context, considered options, decision, consequences, status, and owners. Keep accepted records immutable; supersede them with a new decision when the context changes.

## Record Decisions, Not General Documentation

An ADR is a short record of one architecturally significant choice. It is not a system overview, runbook, requirements document, or transcript of every discussion.

Good ADR subjects include:

- selecting synchronous calls or messaging between services;
- choosing one data ownership boundary;
- adopting an availability or consistency trade-off;
- standardizing an authentication mechanism;
- selecting a framework that changes deployment or support obligations;
- defining an API compatibility policy.

The AWS [ADR process guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/architectural-decision-records/adr-process.html) identifies structure, quality attributes, dependencies, interfaces, and construction techniques as common architecturally significant areas.

“Use PostgreSQL” is not enough. The useful record explains which requirements made that choice preferable, which alternatives were considered, and which costs the team accepted.

## Use a Small, Repeatable Template

A practical Markdown record can stay compact:

```markdown
# ADR-0017: Use an outbox for order events

- Status: Accepted
- Date: 2026-11-07
- Owners: Orders team
- Decision scope: Order write path

## Context

The service must commit an order and publish its event without losing
the event when the broker is unavailable. The database supports local
transactions; the broker does not participate in them.

## Decision

Write the order and an outbox row in one database transaction. A separate
dispatcher publishes pending rows with idempotent consumer contracts.

## Considered options

1. Publish after committing the order.
2. Use a distributed transaction.
3. Use a transactional outbox.

## Consequences

- Event delivery is at least once, so consumers must handle duplicates.
- Publication is eventually consistent.
- The team owns dispatcher lag, retry, and cleanup operations.

## Follow-up

- Add outbox lag and failure alerts.
- Define retention and replay procedures.
```

The template is deliberately plain. A team may add compliance references, decision drivers, or review dates, but fields that nobody maintains create noise rather than governance.

## Start with Context and Decision Drivers

Context should describe forces that make the decision necessary:

- traffic and latency expectations;
- availability and recovery objectives;
- data classification and residency;
- existing platform constraints;
- team skills and operational ownership;
- migration and compatibility requirements;
- cost or delivery deadlines.

Distinguish facts from assumptions. “The dependency supports 500 requests per second” should cite evidence. “Traffic may triple next year” is a forecast and may justify a review trigger rather than permanent complexity.

Avoid retrofitting context to make the chosen option look inevitable. A credible ADR preserves uncertainty and competing priorities.

## Compare Viable Options Fairly

List alternatives that could actually satisfy the decision drivers. A table can expose trade-offs without producing a false numerical score:

| Option | Strength | Cost or risk |
| --- | --- | --- |
| Synchronous HTTP | Simple request semantics | Caller availability depends on the downstream service |
| Durable queue | Buffers outages and load spikes | Eventual consistency and message operations |
| Scheduled batch | Low online coupling | Delayed results and reconciliation complexity |

Do not include a deliberately absurd option merely to make the preferred choice win. If a short experiment resolved uncertainty, link the benchmark or proof of concept and record its environment and limitations.

The ADR should state why the selected option best fits the current drivers, not claim that it is universally superior.

## Make Consequences Operational

Every meaningful decision creates obligations. “Use events” implies schema ownership, duplicate handling, ordering assumptions, retry policy, dead-letter handling, observability, and replay procedures.

Capture positive and negative consequences:

- new failure modes;
- operational signals and alerts;
- required skills or ownership;
- security and privacy impact;
- migration and rollback limitations;
- future choices that become easier or harder.

Consequences should lead to backlog work when implementation is required. An ADR is not a substitute for the tasks, tests, dashboards, or runbooks that make the decision real.

## Give Records a Lifecycle

Useful states commonly include:

- **Proposed** — open for review;
- **Accepted** — approved and active;
- **Rejected** — considered but not selected;
- **Superseded** — replaced by a later ADR;
- **Deprecated** — no longer preferred but still present during migration.

After acceptance, preserve the record as historical evidence. If the context changes, create a new ADR that links to and supersedes the old one. Rewriting the old decision destroys the explanation for code and incidents that occurred under it.

AWS’s [architectural decision record guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/architectural-decision-records/) recommends a decision log and treats accepted records as immutable, with later records superseding earlier choices.

Immutability does not prohibit correcting a broken link or typo, but material reasoning should remain reviewable in version control.

## Store ADRs Near the Work

For a software system, a repository directory such as `docs/decisions/` usually provides useful properties:

- changes use the normal pull-request workflow;
- links can point to code, tests, and operational documents;
- decision history travels with the system;
- reviewers can see design and implementation together.

Use stable identifiers and filenames:

```text
docs/decisions/
├── 0015-api-compatibility-policy.md
├── 0016-service-database-ownership.md
└── 0017-transactional-outbox.md
```

If an organization uses a central catalog, keep the authoritative location clear and link between it and the repository. Two independently edited copies create a new documentation problem.

## Keep Review Proportional

Every developer should be able to propose an ADR. Reviewers should include people who own affected systems and quality attributes. A security-sensitive identity decision needs security review; a local library choice may need only the owning team.

Set a bounded review window and name the decision owner. Requiring a large architecture committee for every record turns ADRs into a delivery gate. Allowing a consequential decision to merge without affected owners creates documentation without alignment.

Time-box experiments for unknowns, then update the proposed record with evidence. A “decision” that remains proposed indefinitely is an unresolved risk, not a historical record.

## Connect Decisions to Enforcement

An ADR has value when teams can find and apply it:

- link it from relevant code or service documentation;
- cite it in pull requests that implement the decision;
- encode enforceable parts in architecture tests, policies, or CI checks;
- create monitoring for operational consequences;
- include supersession review in migration completion criteria.

Do not ask a document to enforce what automation can check. If the decision forbids direct database access across service boundaries, dependency checks and infrastructure policy provide stronger protection than prose alone.

## Avoid Common ADR Failure Modes

- **Recording everything:** trivial implementation choices bury significant decisions.
- **Writing after the fact:** the record becomes a justification instead of a decision aid.
- **Capturing only the winner:** future readers cannot understand rejected alternatives.
- **Ignoring consequences:** the design appears free of operational cost.
- **Editing history:** the record no longer explains earlier implementations.
- **No ownership:** proposed decisions never conclude and accepted obligations are forgotten.
- **No discovery path:** correct records exist but teams repeatedly make the decision again.

The goal is not to maximize ADR count. It is to preserve the reasoning that code cannot express so future changes can distinguish an outdated constraint from a trade-off that still matters.
