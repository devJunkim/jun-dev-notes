---
title: "Amazon ECS Deployment Circuit Breakers: Failure Detection and Safe Rollback"
excerpt: "Configure ECS deployment circuit breakers and rollback while understanding health checks, last-known-good requirements, events, and database limits."
category: "Cloud"

seo:
  focusKeyword: "ECS deployment circuit breaker"
  description: "Use Amazon ECS deployment circuit breakers with rollback, health checks, EventBridge events, deployment observability, and database-safe recovery."
  socialTitle: "ECS Deployment Circuit Breakers and Safe Rollback"
  socialDescription: "Stop failed ECS rollouts automatically without confusing task replacement with complete application recovery."
---

# Amazon ECS Deployment Circuit Breakers: Failure Detection and Safe Rollback

An ECS service deploys a task definition that cannot start. The scheduler repeatedly replaces tasks while the release remains below its minimum healthy capacity. Automatic rollback can shorten the incident, but only if failure detection, health signals, and the previous deployment are trustworthy.

> **Quick answer:** Enable the ECS deployment circuit breaker with rollback for supported service deployments, make container and load-balancer health checks represent real startup, and alert on deployment state changes. Rollback returns the service to a previous completed deployment; it does not undo database migrations, external side effects, or bad data.

## What the Circuit Breaker Watches

During a deployment, ECS evaluates whether new tasks can reach a steady healthy state. Failure can come from tasks that do not start or from replacement tasks that fail applicable health checks.

For a rolling ECS deployment, enable the feature in the service deployment configuration:

```json
{
  "deploymentCircuitBreaker": {
    "enable": true,
    "rollback": true
  },
  "maximumPercent": 200,
  "minimumHealthyPercent": 100
}
```

The exact percentages are workload and capacity decisions, not universal defaults. They control scheduler concurrency and minimum availability separately from the circuit breaker.

AWS’s [ECS deployment failure detection documentation](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/deployment-failure-detection.html) describes deployment circuit breakers and CloudWatch alarms as mechanisms that can mark supported deployments failed and roll them back.

## Rollback Needs a Completed Deployment

Automatic rollback requires a preceding deployment in the `COMPLETED` state. That deployment becomes the last known good target. If no such deployment exists, ECS cannot manufacture one.

Keep task definition revisions and image references traceable to reviewed source. Prefer immutable image digests or uniquely versioned tags. Reusing a mutable tag such as `latest` can make a nominal rollback run different bytes than the earlier deployment.

Record at least:

- service and cluster;
- deployment and task definition revision;
- image digest;
- source commit and build ID;
- configuration version;
- migration version;
- deployment start, failure, and rollback timestamps.

The service deployment history exposes states such as `ROLLBACK_IN_PROGRESS`; AWS documents those states in its [ECS service deployment history](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/service-deployment.html).

## Make Health Checks Describe Startup Correctly

The circuit breaker is only as useful as the signals it receives. A task that opens a port before loading required configuration may look healthy too early. A health check that depends on every optional downstream service may reject an otherwise usable task.

Separate concerns where the platform permits:

- **Container health** should establish that the process can perform its essential local work.
- **Load-balancer target health** should establish that the task can receive application traffic.
- **Application readiness** may include critical dependencies needed for the served operation.
- **External monitoring** should observe user outcomes after the deployment completes.

Give startup enough time for expected initialization but not enough to hide a deadlock. Align container `startPeriod`, interval, timeout, retries, load-balancer grace period, and application startup behavior. Conflicting timers can make diagnosis difficult.

Do not run database migrations inside a frequent health endpoint. Health checks should be bounded and safe to repeat.

## Understand the Two Failure Stages

ECS deployment circuit breaker behavior conceptually covers two stages:

1. Can replacement tasks reach a running state?
2. Once running, do applicable health checks remain healthy?

Stage-one failures often point to image pulls, invalid task definitions, missing secrets, capacity, network setup, or immediate process exits. Stage-two failures often point to health endpoints, dependency access, load-balancer configuration, or application startup errors.

Classify the stage before changing retry counts. Increasing thresholds may only delay a deterministic failure.

## Add CloudWatch Alarms for Outcomes the Scheduler Cannot See

A process can be healthy while returning too many 500 responses or creating unacceptable latency. ECS also supports deployment failure detection through CloudWatch alarms for supported deployment types.

Useful alarm candidates can include:

- elevated target 5xx rate;
- sustained latency beyond the service objective;
- failed business operations with a low-noise metric;
- queue age or processing failures for a worker service.

Choose evaluation windows that can detect a bad rollout without reacting to one transient datapoint. Ensure the metric has traffic during deployment; a no-data alarm cannot prove success.

When both the circuit breaker and alarms are configured, AWS notes that either mechanism can trigger deployment failure. Document which signals control rollback and how operators distinguish them.

## Subscribe to Deployment State Changes

ECS emits deployment state change events to EventBridge. Route failed and rollback states to an operational destination:

```json
{
  "source": ["aws.ecs"],
  "detail-type": ["ECS Deployment State Change"],
  "detail": {
    "eventName": ["SERVICE_DEPLOYMENT_FAILED"]
  }
}
```

Treat the pattern as a starting point and verify the event schema in the target account and Region. Include cluster, service, deployment, task definition, and safe reason fields in the notification. Link to logs and deployment history rather than copying secrets or full environment data into alerts.

An automatic rollback without notification is still an incident. The previous release may be serving again, but the team needs to understand why the new release failed and whether capacity or data were affected.

## Rollback Does Not Reverse Data Changes

ECS changes running tasks. It does not undo:

- a destructive database migration;
- messages already published;
- emails or payments already sent;
- writes made under a new schema;
- changes to external configuration or secrets;
- an infrastructure change deployed outside the service.

Use expand-and-contract database changes so the previous application remains compatible during the rollback window. Separate irreversible cleanup from the initial release. Design mutating operations with idempotency or compensation appropriate to their business semantics.

If a deployment requires a coordinated one-way data migration, automatic task rollback may be unsafe. Define a release-specific recovery procedure instead of assuming the circuit breaker provides full transactionality.

## Do Not Retry Permanent Startup Errors Forever

Typical deterministic failures include a missing secret permission, invalid command, incompatible CPU architecture, unavailable image tag, or configuration validation error. Scheduler retries create noise but do not repair them.

Use startup validation that fails with a clear safe reason, inspect stopped-task details, and retain logs long enough for diagnosis. Avoid printing credentials or the full task environment.

Capacity shortages and transient registry or network failures require a different response. Confirm available capacity, placement constraints, quotas, subnets, and dependency reachability before relaxing deployment safety.

## Test the Rollback Path

In a non-production service, deliberately deploy controlled failures:

- a process that exits during startup;
- a task that runs but fails its health endpoint;
- a release that triggers a selected alarm;
- a deployment with no valid prior completed target;
- a rollback across an additive database migration.

Verify the failed state, rollback target, event delivery, notification content, service capacity, and operator runbook. Then verify a successful deployment clears or supersedes the incident state.

Automatic rollback is a bounded recovery tool. It reduces exposure to failed tasks when deployment signals are honest and artifacts are traceable. It does not replace compatible data evolution, post-deployment monitoring, or ownership of the failed release.
