---
title: "Idempotency in Distributed Systems: Designing Safe Retryable Operations"
excerpt: "Design idempotent APIs, message consumers, and workflows with durable keys, database constraints, explicit transaction boundaries, and honest failure recovery."
category: "Architecture"

seo:
  focusKeyword: "idempotency in distributed systems"
  description: "Design safe retryable APIs and message consumers using idempotency keys, unique constraints, deduplication, transaction boundaries, and failure analysis."
  socialTitle: "Idempotency in Distributed Systems"
  socialDescription: "Make retries safe across APIs, queues, and workflows without confusing duplicate delivery with exactly-once execution."
---

# Idempotency in Distributed Systems: Designing Safe Retryable Operations

A caller can time out after a server commits an order but before the response arrives. A message consumer can commit database work and crash before acknowledging its message. In both cases, retrying is reasonable—and may repeat an effect that already succeeded.

Idempotency gives repeated attempts one business result. It is not merely checking whether the same HTTP request or message appeared before; it is designing the state transition and its failure boundaries so duplication is safe.

> **Quick answer:** Identify one logical operation with a stable idempotency key, enforce that identity atomically with the business change, and store enough outcome information to answer a retry. Expect concurrent attempts and at-least-once delivery. For effects outside the local transaction, propagate idempotency or use durable workflow state and reconciliation.

## Define the Effect That Must Happen Once

HTTP `GET`, `PUT`, and `DELETE` have idempotent method semantics when implemented according to their intended meaning, but business behavior still matters. Two identical `POST /orders` requests normally create two orders unless the API defines an operation identity.

Start by naming the invariant:

- one order for checkout attempt `checkout-842`;
- one payment capture for merchant operation `capture-193`;
- one inventory reservation for order `order-51` and line `3`;
- one projection update for event ID `event-77`.

“Process this request once” is too vague. Retries may contain the same payload under different transport IDs, while two legitimate operations may have identical payloads. Use a client- or producer-generated operation key whose scope and lifetime match the business action.

## An API Key Needs a Contract

A client sends a stable key on every attempt of the same logical command. The server associates the key with the authenticated tenant and operation type; a globally unique random value is useful, but scoping remains defense in depth.

```text
POST /orders
Idempotency-Key: 01J...

{ "customerId": 42, "lines": [...] }
```

Store a request fingerprint with the key. If the same key arrives with a materially different command, reject it rather than returning an unrelated earlier result. Canonicalize only fields that define the operation; hashing raw JSON bytes can treat harmless property ordering as a different request.

Define how long records remain, whether failures are replayable, and which response is returned. Usually a successfully committed result is replayed. Validation failures need not reserve a key forever, while an uncertain downstream outcome may need a durable `Processing` state and later reconciliation.

Do not place credentials or sensitive request bodies in the idempotency record. Store a safe fingerprint and the minimum result needed to reconstruct the response.

## Let the Database Decide the Race

A read-then-insert check is not safe under concurrency:

```text
Request A: key does not exist
Request B: key does not exist
Request A: creates order
Request B: creates another order
```

Put a unique constraint on the operation identity, such as `(TenantId, OperationType, IdempotencyKey)`. Then coordinate the idempotency row and business mutation in one database transaction.

```csharp
public sealed class IdempotencyRecord
{
    public required string TenantId { get; init; }
    public required string Operation { get; init; }
    public required string Key { get; init; }
    public required string RequestHash { get; init; }
    public required string Status { get; set; }
    public string? ResourceId { get; set; }
}
```

One viable transaction is:

```text
Begin transaction
  Insert idempotency key with Processing status
  If unique-key conflict:
    load existing record and return or resolve its state
  Apply the business transition
  Store Completed status and result identity
Commit
```

The exact conflict and locking behavior depends on the database. Catching a uniqueness violation may invalidate the current transaction, so the existing result may need to be read in a new transaction. Another design inserts and locks the key through database-specific upsert semantics. Test with genuinely concurrent calls against the production database engine; an in-memory substitute cannot prove the race is closed.

Do not commit a permanent “completed” marker before the business change. A crash between them would cause retries to skip work that never happened.

## A Duplicate May Still Be In Progress

Two requests can overlap before the first commits. The second needs a defined response: wait briefly, return a conflict or accepted status with a status URL, or retry reading after the winning transaction completes.

A `Processing` record that survives independently of the business transaction creates another problem: the owner can crash and leave it forever. Add a lease or recovery rule only when that separate reservation is necessary. A timeout alone must not authorize two workers to perform a non-idempotent external effect concurrently.

The original HTTP response might never reach the caller. Persist a stable resource ID or response representation so a retry can learn that the operation succeeded. Re-running only the response formatting is different from re-running the business action.

## At-Least-Once Messages Require Durable Deduplication

Queues commonly deliver at least once. A consumer can see the same message again even when the broker offers a deduplication feature, because broker acceptance, application state, and acknowledgement do not share one atomic boundary.

For a database-local effect, store the consumed message ID in the same transaction as the change:

```text
Begin transaction
  Insert (ConsumerName, MessageId) into Inbox with a unique constraint
  If it already exists, skip the business effect
  Update the local business state
Commit
Acknowledge message
```

Use the producer's stable event or operation ID, not a delivery receipt that changes on redelivery. Include `ConsumerName` when independent consumers must each process the event once.

If the process crashes after commit but before acknowledgement, redelivery finds the inbox row and safely acknowledges without repeating the update. [Reliable Background Processing on AWS with SQS and .NET](https://dev.jun-kim.net/2026/09/15/reliable-background-processing-on-aws-with-sqs-and-net/) covers visibility, deletion, Lambda batches, and dead-letter handling around this boundary.

Retention must cover realistic retry and redrive windows. Deleting deduplication records too early turns an old replay into new work. Keeping every ID forever creates unbounded storage, so make the retention contract explicit.

## External Effects Move the Boundary

A local database transaction cannot atomically commit a payment provider call, an email, and a broker acknowledgement. Holding a database lock while making an HTTP call does not change that fact.

Use the downstream provider's idempotency mechanism when available, deriving or persisting a stable downstream key for the logical operation. If the response is lost, query by that key or reconcile before deciding to send again.

When a committed database change must reliably publish a message, use a transactional outbox: save the change and outgoing event together, then let a relay retry publication. [Transactional Outbox Pattern in .NET](https://dev.jun-kim.net/2026/09/17/transactional-outbox-pattern-in-net-reliable-event-delivery/) explains why the relay remains at-least-once and consumers still need idempotency.

For a multi-step workflow, store each transition and its operation identity durably. Compensation is not idempotency: refunding a duplicated charge may restore the balance, but it still creates extra events, fees, and customer confusion. Make both forward and compensating commands idempotent.

## Natural Idempotency Is Often Simpler

Some operations can express the desired final state rather than an action count:

- “set subscription status to cancelled” is easier to repeat than “toggle status”;
- “put document version 7” is safer than “append the same document again”;
- an upsert on a unique business key can make repeated creation converge;
- applying event version 12 only when the stored version is 11 prevents a stale repeat.

Natural idempotency still needs concurrency control. Two inventory decrements cannot be made safe by giving them the same final SQL statement unless the database predicate and business key enforce the intended transition.

## Test the Failure Windows

Happy-path duplicate tests are insufficient. Exercise:

- two simultaneous requests with the same key;
- the same key with a different payload;
- a crash before and after the local commit;
- a timeout after a downstream service accepts the request;
- duplicate and out-of-order messages;
- redrive after deduplication retention expires;
- a poisoned `Processing` record whose owner disappeared.

Observe duplicate-key conflicts, replayed responses, in-progress operations, deduplication age, and reconciliation failures without logging sensitive payloads.

Idempotency does not create exactly-once transport. It makes repeated delivery converge on one defined business outcome, even when the network cannot tell the caller which attempt succeeded.
