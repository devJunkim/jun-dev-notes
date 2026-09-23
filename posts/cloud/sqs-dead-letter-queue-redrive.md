---
title: "Amazon SQS Dead-Letter Queues: Diagnosis, Redrive, and Safe Recovery"
excerpt: "Operate Amazon SQS dead-letter queues as a recovery workflow with deliberate retry thresholds, alarms, diagnosis, controlled redrive, and idempotency."
category: "Cloud"

seo:
  focusKeyword: "Amazon SQS dead-letter queues"
  description: "Design Amazon SQS dead-letter queue operations with maxReceiveCount, retention, alarms, diagnosis, safe redrive, and replay controls."
  socialTitle: "Amazon SQS Dead-Letter Queues and Safe Redrive"
  socialDescription: "Turn poison-message isolation into an operational recovery process instead of repeatedly replaying the same production failure."
---

# Amazon SQS Dead-Letter Queues: Diagnosis, Redrive, and Safe Recovery

A dead-letter queue receives 8,000 messages after a deployment. Redriving all of them immediately feels like recovery, but if the defect remains—or the downstream database is already saturated—the replay becomes a second incident.

A DLQ is an isolation mechanism, not an automatic repair system. Recovery requires evidence about why messages failed and a controlled way to reintroduce them.

> **Quick answer:** Choose `maxReceiveCount` from the real retry and visibility policy, alarm on DLQ arrivals, retain messages long enough to investigate, and classify the failure before redrive. Fix the cause, replay at a bounded rate, and rely on idempotent processing because redrive does not create exactly-once delivery.

## Configure the Source and DLQ as One System

The source queue's redrive policy identifies the DLQ and the receive count after which SQS moves a repeatedly received message. A redrive allow policy on the DLQ controls which source queues may use it.

```json
{
  "deadLetterTargetArn": "arn:aws:sqs:ca-central-1:123456789012:orders-dlq",
  "maxReceiveCount": 5
}
```

The value `5` is illustrative. A low threshold can quarantine transient failures before normal retry has a chance to work. A very high threshold can repeatedly consume capacity on a permanent schema or authorization failure.

AWS's [SQS dead-letter queue guidance](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-dead-letter-queues.html) recommends allowing sufficient retries and documents important retention and ordering behavior. Use the same queue type for source and DLQ, and avoid a DLQ when moving a FIFO message aside would violate the business ordering requirement.

## Derive the Threshold from Failure Timing

`maxReceiveCount` cannot be chosen independently from visibility timeout, consumer concurrency, SDK retries, and downstream recovery time. A message can be received again because processing failed, the worker crashed after committing, or visibility expired before completion.

Before increasing the threshold, determine whether successful messages are timing out. Extending visibility or checkpointing long work may be the real fix. Before decreasing it, determine whether a normal dependency outage would quarantine an entire backlog.

Use bounded backoff in the consumer or an explicit delayed-retry design when immediate redelivery creates a hot loop. SQS redelivery timing alone is not a full retry schedule.

## Retention Has Non-Obvious Semantics

Set the DLQ retention period longer than the source queue's retention period. For Standard queues, moving a message to a DLQ does not reset its original enqueue timestamp for expiry; the DLQ age metric reflects time since it arrived in the DLQ. A message that spent most of its lifetime in the source queue may therefore disappear from the DLQ sooner than an operator expects.

For FIFO queues, the enqueue timestamp resets when the message moves to the DLQ. Treat that difference explicitly in runbooks rather than inferring retention from the visible age metric alone.

Retention is not archival. If failed payloads are evidence that must be kept longer, copy a security-reviewed diagnostic record to an approved store with its own encryption, access, and deletion policy. Do not log entire message bodies by default; they may contain personal data or secrets.

## Alarm on Arrival, Then Classify

Create a CloudWatch alarm for DLQ message arrivals or visible messages, and route it to an owned response path. A DLQ nobody monitors converts immediate failures into silent data loss after retention expires.

Operators need safe diagnostic fields:

- source queue and consumer version;
- message ID and business correlation ID;
- approximate receive count;
- schema or event type version;
- a stable failure code and dependency name;
- first and most recent failure timestamps.

Classify failures before replay:

| Failure class | Typical response |
| --- | --- |
| Temporary dependency outage | Confirm recovery, then bounded redrive |
| Consumer defect | Deploy and verify the fix before redrive |
| Invalid or obsolete payload | Quarantine, transform through an approved path, or discard with audit |
| Missing authorization or key access | Repair policy or data ownership; do not expose credentials in the message |
| Duplicate business operation | Confirm idempotency behavior before replay |

Do not edit message bodies manually in the console as the normal repair path. A repeatable transformer with validation, review, and auditability is safer for migrations.

## Redrive at a Rate the System Can Absorb

SQS dead-letter queue redrive can move messages back to their source queue or to another queue. Choose a velocity below the verified spare processing capacity. Live traffic and recovery traffic compete for the same consumers and dependencies.

Use a canary sequence:

1. Stop or contain the source of new failures.
2. Deploy the fix and prove it on representative messages in a safe environment.
3. Redrive a small sample.
4. Verify business outcomes, deletion, latency, and downstream saturation.
5. Increase velocity gradually while watching both queues and dependencies.
6. Stop if the DLQ begins receiving the same failure again.

If replay order matters, FIFO semantics and message-group behavior require specific analysis. A DLQ may already have broken the original end-to-end sequence.

## Idempotency Is Still Required

Redrive creates a new processing attempt, not an exactly-once guarantee. The original attempt may have committed the business change and crashed before deleting the message. The replay must not charge, email, or reserve inventory twice.

Use a domain idempotency key and an atomic record of the accepted operation. Keep deduplication state long enough to cover source retention, DLQ investigation, and the latest plausible redrive. SQS FIFO deduplication windows do not replace application-level idempotency for long recovery periods.

Validate the recovery with the business system of record. A falling queue count proves transport progress, not that each order reached the correct final state.

## Secure and Rehearse the Runbook

Grant consumers only the queue actions they need. Restrict redrive permissions to an operational role and constrain which source queues may target the DLQ. Include encryption-key permissions where required, without placing credentials in configuration files or payloads.

Rehearse poison messages, expired visibility, dependency outages, and redrive throttling. The runbook should name the decision owner, dashboards, safe sample procedure, stop conditions, discard approval, and reconciliation query.

A useful DLQ shortens diagnosis and preserves recovery choices. Its success metric is not that it remains empty at all costs; it is that failures become visible, explainable, and safely recoverable before retention turns them into loss.
