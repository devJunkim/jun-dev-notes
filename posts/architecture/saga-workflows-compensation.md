---
title: "Saga Workflows: Designing Compensation for Distributed Business Operations"
excerpt: "Coordinate multi-service business operations with explicit saga state, idempotent steps, domain-aware compensation, and recoverable failure handling."
category: "Architecture"

seo:
  focusKeyword: "saga pattern compensation"
  description: "Design saga workflows with orchestration or choreography, idempotent commands, compensating actions, pivot points, and operational recovery."
  socialTitle: "Saga Workflows and Reliable Compensation"
  socialDescription: "Model partial success explicitly when one business operation spans services and a database rollback can no longer undo the work."
---

# Saga Workflows: Designing Compensation for Distributed Business Operations

An order service reserves inventory, a payment service authorizes a card, and shipping rejects the address. No database transaction spans all three services. “Roll everything back” is not an instruction any one database can execute.

A saga makes partial progress and recovery explicit. It coordinates local commits, records durable workflow state, and applies domain-specific compensating actions when forward progress is no longer appropriate.

> **Quick answer:** Model each saga step as an idempotent command with a durable outcome and a deliberate compensation. Record enough state to resume after crashes, distinguish transient retries from business rejection, and identify the point after which the workflow must move forward instead of pretending every effect can be undone.

## A Saga Is Not a Distributed Rollback

Each participant commits its own local transaction. Other services may observe that change before the overall workflow finishes. Compensation is a new business action, not time travel.

Canceling a reservation may release inventory, but it does not erase an audit record or guarantee the item is still available to the same customer later. Refunding a payment may incur fees and appear as a separate statement entry. Sending a follow-up email cannot make the first email unread.

The Azure Architecture Center's [saga pattern guidance](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga) distinguishes compensable steps, a pivot or point of no return, and retryable steps that must eventually complete. That model prevents vague “undo” requirements from hiding irreversible effects.

## Write the State Machine Before the Handlers

For an order workflow, define states and transitions explicitly:

```text
Pending
  -> InventoryReserved
  -> PaymentAuthorized
  -> Confirmed

InventoryRejected -> Rejected
PaymentRejected   -> ReleasingInventory -> Rejected
ShippingRejected  -> VoidingPayment -> ReleasingInventory -> Rejected
```

Store the saga ID, business ID, current state, step attempts, participant operation IDs, deadlines, and the data required for compensation. A single status string without transition history is difficult to audit or resume.

Apply transitions conditionally. If two duplicate completion messages arrive, only the transition from the expected prior state should succeed. Optimistic concurrency or a compare-and-set version prevents both handlers from advancing the same saga independently.

## Make Forward and Compensating Commands Idempotent

At-least-once delivery means a command may run after its response was lost. Every participant needs a durable idempotency key scoped to the operation:

```json
{
  "sagaId": "saga-8842",
  "commandId": "reserve-inventory-1",
  "orderId": "order-314",
  "sku": "AB-12",
  "quantity": 2
}
```

The inventory service records `commandId` and the result atomically with the reservation. A retry returns the existing outcome rather than reserving twice.

Compensation needs the same property. `release-inventory-1` should release only the reservation created by this saga and should report success when that release already occurred. It must not subtract a quantity from whichever reservation currently happens to match the SKU.

Use a transactional outbox when a participant must commit state and publish its result reliably. [Transactional Outbox Pattern in .NET](https://dev.jun-kim.net/2026/09/17/transactional-outbox-pattern-in-net-reliable-event-delivery/) covers that local dual-write boundary; the saga does not remove it.

## Retry Technical Failures Before Compensating Business Work

A timeout does not prove a step failed. The remote service may have committed and lost the response. Query by idempotency key or retry the same command before choosing compensation.

Classify outcomes:

| Outcome | Typical workflow response |
| --- | --- |
| Explicit business rejection | Take an alternate path or compensate |
| Timeout or unavailable dependency | Retry with the same operation ID and bounded backoff |
| Unknown result after retry budget | Reconcile or pause for review |
| Invalid workflow data | Stop, alert, and preserve evidence |

Do not compensate merely because one HTTP call timed out. Issuing a refund while the original authorization outcome remains unknown can create a second incorrect side effect.

## Choose Orchestration or Choreography Deliberately

With orchestration, one durable coordinator owns workflow state and sends commands. This makes complex sequences, deadlines, and operator visibility easier to centralize, but the orchestrator becomes critical infrastructure and can accumulate business logic that belongs in participants.

With choreography, participants react to events without one controller. It can fit a short, naturally event-driven flow, but the overall state becomes difficult to see as steps and compensations grow. Cycles and accidental coupling often appear in event subscriptions.

The decision is not “centralized versus scalable.” Both approaches require durable messages, idempotency, observability, and recovery. Prefer orchestration when the business needs an explicit process owner, branching, timeouts, or manual intervention. Keep choreography small and document who owns completion.

## Put Irreversible Work Behind a Pivot

Sequence validation and reversible reservations before irreversible or externally visible actions when the business permits it. After the pivot, use retryable steps designed to reach completion rather than compensation that cannot restore the earlier world.

For example, validate the shipping address and reserve inventory before capturing payment. An authorization may be reversible; capture and settlement may be progressively harder to undo. The exact pivot is a business and provider decision, not a universal technical rule.

Concurrent workflows can still create anomalies because sagas do not provide isolation. Use version checks, semantic locks, commutative updates, or resource-specific reservations where conflicting operations matter. Compensation must respect changes made by other workflows instead of overwriting them with an old snapshot.

## Operate Compensation as a First-Class Path

Compensation can fail. Persist each compensation attempt, retry safely, and expose a terminal state such as `CompensationRequired` for operator intervention. Do not mark the saga “failed” while hiding which effects remain active.

Useful telemetry includes saga ID, business ID, current state, time in state, attempt count, next deadline, and safe participant outcome codes. Trace propagation helps, but durable workflow history is the source of truth after traces expire.

Test crashes after every local commit and before every outgoing message. Also test duplicate outcomes, out-of-order events, late success after compensation starts, expired reservations, participant schema changes, and an unavailable compensation dependency.

Use a saga only when a business operation genuinely spans independent transactional boundaries. If one database transaction can satisfy the consistency requirement, it is usually simpler. A saga earns its complexity when partial success is unavoidable and the organization is prepared to operate the recovery path, not merely draw it in a sequence diagram.
