---
title: "Amazon EventBridge vs SQS: Event Routing or Durable Work Queue?"
excerpt: "Choose Amazon EventBridge or SQS by separating event routing from durable work ownership, then design retries, ordering, backpressure, and dead-letter recovery."
category: "Cloud"

seo:
  focusKeyword: "Amazon EventBridge vs SQS"
  description: "Compare Amazon EventBridge and SQS for routing, buffering, retries, ordering, backpressure, fan-out, and operational recovery."
  socialTitle: "Amazon EventBridge vs SQS: Choose by Delivery Semantics"
  socialDescription: "Separate event distribution from durable work queues and design failure recovery before connecting AWS services."
---

# Amazon EventBridge vs SQS: Event Routing or Durable Work Queue?

Amazon EventBridge and Amazon SQS both decouple producers from consumers, but they solve different coordination problems. Choosing by a feature checklist can hide the most important question: who owns unfinished work when a consumer is slow or unavailable?

> **Quick answer:** Use EventBridge to match and route events to one or more targets. Use SQS when a consumer needs a durable backlog it can poll and drain at its own rate. Combine them when routing and buffering are both required, and make every consumer idempotent because duplicate delivery is possible.

## Start with the Communication Model

The [AWS messaging decision guide](https://docs.aws.amazon.com/decision-guides/latest/decision-guides/sns-or-sqs-or-eventbridge.html) distinguishes EventBridge's rule-based event routing from SQS's pull-based queue.

| Concern | EventBridge event bus | SQS queue |
| --- | --- | --- |
| Primary role | Match events and route to targets | Hold work until consumers process it |
| Consumption | Service invokes configured targets | Consumers poll messages |
| Fan-out | Multiple rules and targets | One queue distributes messages among its consumers |
| Backpressure | Target retry and failure policy | Visible backlog and consumer-controlled rate |
| Ordering | Do not assume global order | FIFO queues support ordered message groups |

An event says something happened: `OrderPlaced`, `CustomerAddressChanged`, or `BuildCompleted`. A queue message often represents work to perform: generate an invoice, resize an image, or synchronize a record.

Names do not enforce semantics. A command published broadly on an event bus can acquire several owners, while an event placed in one competing-consumer queue may reach only one interested system.

## Use EventBridge for Routing and Fan-Out

EventBridge rules match fields in an event envelope and route matching events to configured targets. Producers do not need to know every consumer, and new consumers can subscribe through additional rules.

Define event source, detail type, schema ownership, and compatibility policy. Additive fields are generally easier to evolve, but consumers may still reject unknown enum values or changed meanings. Avoid placing secrets or unnecessary personal data in broadly routed events.

Rules are infrastructure code. Test patterns against representative events so a small JSON-path mistake does not silently route everything—or nothing. Monitor matched events, failed invocations, throttling, and dead-letter delivery.

## Use SQS for Backpressure and Work Ownership

An SQS queue persists messages while workers are unavailable and lets consumers scale according to backlog. The visibility timeout temporarily hides a received message; deleting it acknowledges completion.

Set visibility longer than normal processing and extend it deliberately for exceptional long-running work. If visibility expires before deletion, another worker can receive the same message. If a worker deletes before durable completion, a crash can lose work.

Standard queues provide at-least-once delivery and best-effort ordering. Design idempotency around a stable operation or event ID, not the SQS message ID alone. FIFO queues add ordering and deduplication features, but ordering is scoped to message groups and can reduce parallelism when too much work shares one group.

## Combine Them at the Boundary

A common design routes a domain event through EventBridge into a dedicated SQS queue for each asynchronous consumer:

```text
Order service -> EventBridge -> Billing queue -> Billing workers
                              -> Email queue   -> Email workers
                              -> Analytics target
```

EventBridge owns subscription and filtering; each queue owns that consumer's backlog, retry cadence, concurrency, and redrive process. An outage in email processing does not require the producer or billing consumer to slow down.

This combination adds cost and operational surface. For a single work consumer with no routing need, publish directly to SQS. For a lightweight event target that already provides sufficient retry behavior, an intermediate queue may be unnecessary.

## Design Retries and Dead Letters Before Failure

EventBridge and SQS retry at different boundaries. EventBridge retries delivery to a target according to target policy. SQS makes a message visible again after processing failure or timeout, and a redrive policy can move repeatedly received messages to a dead-letter queue.

When EventBridge targets SQS, distinguish “could not enqueue” from “worker could not process.” Use a dead-letter queue for failed target delivery where supported, and a separate queue redrive policy for poison work items. Alert on both paths.

A dead-letter queue is evidence, not recovery. Record failure reason and source identifiers safely, define who diagnoses it, fix the cause, and redrive in controlled batches. Replaying without idempotency can repeat charges, emails, or state transitions.

## Include Security and Operations

Grant producers permission only to put events on the intended bus or send messages to the intended queue. Queue policies and target roles must prevent confused-deputy access. Encrypt where required, restrict who can inspect payloads, and avoid sensitive data in message attributes used for routing or metrics.

Track queue age, backlog, receive count, processing latency, EventBridge rule matches, failed target invocations, and DLQ depth. Trace identifiers should cross the event and queue boundary without exposing credentials or personal data.

EventBridge and SQS are complementary when the architecture needs both discovery and durable workload control. Choosing deliberately keeps a consumer outage from becoming a producer outage—or an invisible data-loss path.
