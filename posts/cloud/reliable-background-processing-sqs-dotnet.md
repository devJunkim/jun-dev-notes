---
title: "Reliable Background Processing on AWS with SQS and .NET"
excerpt: "Design reliable SQS consumers in .NET with idempotent processing, deliberate visibility timeouts, safe acknowledgement, and recoverable failures."
category: "Cloud"

seo:
  focusKeyword: "SQS background processing in .NET"
  description: "Build reliable AWS SQS background processing in .NET with idempotency, visibility timeouts, dead-letter queues, Lambda batches, and ECS workers."
  socialTitle: "Reliable Background Processing with SQS and .NET"
  socialDescription: "Understand queue delivery, deletion, duplicate processing, and failure recovery for Lambda and .NET workers on ECS."
---

# Reliable Background Processing on AWS with SQS and .NET

A queue lets an API accept work without waiting for a document import or report to finish. It also gives the consumer somewhere to recover from a temporary outage.

The hard part is the boundary between “the work succeeded” and “the message was acknowledged.” A process can fail between those two steps, so reliability depends on more than catching exceptions around a handler.

> **Quick answer:** Treat SQS delivery as repeatable, make business effects idempotent, and delete a message only after the required durable work succeeds. Design visibility, retries, dead-letter handling, and consumer concurrency as one processing contract.

## Start with the Work Contract

A typical flow is an API or scheduled producer sending a small job message to SQS. A consumer receives it, loads any referenced data, performs the operation, and acknowledges completion by deleting the message.

Use a stable business operation ID, such as an import job ID, in the message. An SQS message ID identifies a queue message; it does not necessarily identify a business operation that a producer may submit more than once.

If accepting a job also changes database state, consider the producer's reliability gap. Saving a job row and then sending to SQS can fail between those operations. A transactional outbox or another durable handoff can preserve the intent to enqueue work. A queue cannot recover a message that was never successfully sent.

Keep payloads narrow: job ID, schema version, correlation ID, and an object reference where appropriate. Put large files in object storage, with an immutable key or version identifier and a lifecycle that outlasts retries and redrive. Validate both the message and the referenced resource; a reference is not permission to fetch an arbitrary URL.

## Standard and FIFO Do Not Remove Consumer Failure Windows

Standard queues provide at-least-once delivery and best-effort ordering. Design for duplicate messages and out-of-order arrival.

FIFO queues provide ordering within a message group and producer deduplication within a bounded deduplication interval. Choose meaningful group IDs, such as an account ID, when operations for one account must remain ordered. A single group also limits concurrency for that sequence.

FIFO does not make a database update and SQS deletion one atomic operation. A consumer can commit its work, crash before deletion, and receive the message again. Do not turn queue-level deduplication into a claim of exactly-once business processing.

Use Standard when independent jobs can run in any order. Use FIFO when per-group ordering is a real requirement worth the associated scheduling constraints. In both cases, define what repeated processing means.

## Make the Business Effect Idempotent

For a database-only operation, an idempotency record and the business changes can share a transaction:

```text
Begin database transaction
  Claim JobId using a unique constraint
  If this job already committed, return the recorded outcome
  Apply the business changes
  Record the completed outcome
Commit database transaction
Delete the SQS message
```

The unique constraint must handle concurrent consumers, not just sequential duplicates. On a claim conflict, the implementation needs to observe the committed outcome or retry appropriately; a preliminary “does it exist?” query is not sufficient synchronization.

Do not commit a “processed” marker before doing the work. A crash afterward would cause the retry to skip an unfinished operation. Conversely, do not assume an in-memory `HashSet` survives a restart or coordinates multiple ECS tasks.

An external side effect needs its own strategy. If a job charges a payment provider, use that provider's supported idempotency key and persist enough state to reconcile uncertain outcomes. Holding a database transaction open around an HTTP call does not make the remote effect transactional.

## Visibility Is Time to Work, Not a Lock

Receiving an SQS message temporarily hides it from other consumers. If processing and deletion do not finish before the visibility timeout expires, it becomes eligible for another delivery. A crash or failed attempt normally leaves the message undeleted so it can be retried.

Set visibility based on processing duration and operational margin. Extend it with `ChangeMessageVisibility` when appropriate for longer work, and monitor extension failures. An extension is not a guarantee that another consumer cannot ever observe a duplicate.

For jobs that cannot fit comfortably within SQS's visibility constraints, split the work, checkpoint progress, or hand it to an execution system designed for that duration. Repeatedly extending an unbounded job is not a complete recovery design.

Deletion uses the receipt handle from the current receive, not the message ID. A later receive produces a new handle. If deletion fails after the business commit, the retry must recognize that the business work already finished.

## A Focused .NET Worker

An ECS-hosted worker generally owns polling, processing, and deletion itself. This `BackgroundService` example uses `AWSSDK.SQS` and the .NET hosting abstractions. It processes one message at a time so waiting inside a local batch does not consume another message's visibility budget.

```csharp
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed record QueueSettings(string QueueUrl);

public interface IJobProcessor
{
    // Validate the payload. Complete durable, idempotent work before returning.
    Task ProcessAsync(string body, CancellationToken cancellationToken);
}

public sealed class SqsWorker(
    IAmazonSQS sqs,
    QueueSettings settings,
    IJobProcessor processor,
    ILogger<SqsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(
                    new ReceiveMessageRequest
                    {
                        QueueUrl = settings.QueueUrl,
                        MaxNumberOfMessages = 1,
                        WaitTimeSeconds = 20,
                        VisibilityTimeout = 120,
                    }, stoppingToken);

                foreach (var message in response.Messages ?? [])
                {
                    using var budget = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken);
                    budget.CancelAfter(TimeSpan.FromSeconds(90));

                    try
                    {
                        await processor.ProcessAsync(message.Body, budget.Token);
                        await sqs.DeleteMessageAsync(new DeleteMessageRequest
                        {
                            QueueUrl = settings.QueueUrl,
                            ReceiptHandle = message.ReceiptHandle,
                        }, budget.Token);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            "SQS attempt failed for {MessageId}; failure type {FailureType}. " +
                            "The message was not acknowledged.",
                            message.MessageId, exception.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "SQS polling failed; failure type {FailureType}",
                    exception.GetType().Name);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
```

The 90-second processing budget and 120-second visibility are illustrative values for short jobs, with room for acknowledgement and scheduling overhead. Cancellation is cooperative: a processor that ignores the token can run past both. Measure actual durations and account for network retries before adopting those numbers.

Configure the SDK's HTTP timeout to exceed the long-poll wait. Reuse an appropriately configured `IAmazonSQS` client and use an ECS task role for credentials. The queue URL comes from validated configuration, not user input.

Because hosted services are long-lived, the injected processor must be safe for that lifetime. If processing needs an EF Core `DbContext` or another scoped service, resolve the processing operation inside a fresh DI scope per message instead of capturing it in this constructor.

Shutdown cancels polling and ongoing work. This sample leaves interrupted messages for retry rather than attempting to drain indefinitely. Configure the host's shutdown budget and ECS stop timeout to match the chosen policy. If the process is killed before cleanup, correctness must still come from durable state and idempotency.

The outer delay prevents a tight loop during polling failures. A production fleet should add jitter and distinguish a persistent configuration or IAM failure from a temporary outage. Per-message failures rely on queue redelivery, so a poison payload does not become an immediate local retry loop.

## Lambda Owns a Different Part of the Loop

With a Lambda SQS event-source mapping, the integration polls the queue and invokes the function with a batch. After successful processing, the integration deletes the successfully handled messages. Application code normally returns the result rather than implementing its own receive/delete loop.

By default, a failed batch can cause every message in that batch to be retried after visibility expires, including records whose work already succeeded. Enable `ReportBatchItemFailures` on the event-source mapping and return failed message identifiers to avoid retrying successful records unnecessarily.

This focused handler method uses `Amazon.Lambda.SQSEvents`, `Amazon.Lambda.Core`, and the same `IJobProcessor` contract. It is for a Standard queue; the Lambda bootstrap and dependency construction are outside the example.

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;

public sealed class SqsBatchHandler(IJobProcessor processor)
{
    public async Task<SQSBatchResponse> HandleAsync(
        SQSEvent input, ILambdaContext context)
    {
        var processingTime = context.RemainingTime - TimeSpan.FromSeconds(2);
        if (processingTime <= TimeSpan.Zero)
        {
            throw new TimeoutException("Insufficient time to process this batch.");
        }

        using var budget = new CancellationTokenSource(processingTime);
        var failures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var message in input.Records)
        {
            try
            {
                budget.Token.ThrowIfCancellationRequested();
                await processor.ProcessAsync(message.Body, budget.Token);
            }
            catch (Exception exception)
            {
                context.Logger.LogLine(
                    $"SQS record {message.MessageId} failed ({exception.GetType().Name}).");
                failures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = message.MessageId,
                });
            }
        }

        return new SQSBatchResponse(failures);
    }
}
```

The deadline leaves an illustrative two-second margin to return the response. After the budget expires, remaining records are reported as failures without starting them. The processor must honor cancellation; a blocking dependency can still exhaust the function timeout. If the invocation times out or throws instead of returning a valid response, partial successes are not reliably reported. Tune the margin and use the application's structured logging convention in deployment.

For FIFO, stop after the first failure and report failed and unprocessed records as failures to preserve ordering. Do not copy the Standard-queue loop unchanged. Partial batch response also does not remove duplicate-delivery risk: idempotency remains necessary.

Choose the Lambda queue visibility setting using AWS's function-timeout and batching guidance; do not copy the standalone worker's numbers into the event-source mapping configuration.

## Dead-Letter Queues Need an Operating Procedure

A source queue's redrive policy can move messages to a dead-letter queue after the configured receive threshold. Set `maxReceiveCount` high enough for plausible transient failures, while keeping permanently invalid work from consuming capacity indefinitely.

An SQS source DLQ is distinct from Lambda's asynchronous invocation destinations. Queue-triggered recovery should be designed around the SQS event-source integration and source queue redrive behavior.

Alarm on DLQ arrivals and preserve enough safe context to diagnose them. Fix the cause before redriving: replaying an invalid schema or a forbidden object reference simply repeats the failure. Rate-limit recovery so old work does not overwhelm current traffic, and keep idempotency records long enough to cover your replay window.

Moving a failed FIFO message aside can allow later work to proceed without that earlier operation. Decide whether that is acceptable for the business ordering requirement before attaching a DLQ by habit.

## Batch Size and Scaling Are Capacity Decisions

Batches reduce request overhead, but received messages are already consuming visibility time while they wait for local processing. Bound parallelism and size the batch against the slowest expected work. With direct SQS batch delete APIs, inspect individual failures even when the overall HTTP request succeeded.

Scale according to queue age and processing capacity, not just message count. Useful signals include `ApproximateAgeOfOldestMessage`, visible and in-flight message counts, DLQ arrivals, handler duration, retry counts, and completed business jobs. SQS metrics are approximate; message deletion counts alone do not prove unique business completion.

Limit Lambda event-source concurrency or ECS worker concurrency to what the database and downstream APIs can sustain. More consumers can turn a queue backlog into a database outage. Long polling reduces empty receives; it does not provide downstream backpressure by itself.

Log the business job ID, SQS message ID, correlation ID, attempt outcome, and elapsed time. Avoid raw payloads and secrets. Grant producers only the send permissions they need and consumers the relevant receive, delete, and visibility permissions, plus narrowly scoped access to referenced objects and encryption keys.

## Choose the Consumer Around Recovery Needs

| Workload need | Likely starting point |
| --- | --- |
| Bounded independent jobs with managed polling and scaling | Lambda event-source mapping |
| Long-lived processing with custom concurrency or process resources | ECS worker |
| Per-group ordered work | FIFO plus a consumer that preserves group semantics |
| Work too large for one safe attempt | Checkpoints, smaller jobs, or a workflow service |

The broader .NET packaging and compute trade-offs are covered in [AWS Lambda vs ECS](https://dev.jun-kim.net/2026/09/13/aws-lambda-vs-ecs-choosing-the-right-compute-option-for-net-applications/).

Before calling a queue consumer reliable, test a crash after the business commit but before deletion. Then test a duplicate, an expired visibility timeout, a poison message, and a throttled dependency. Those cases reveal whether the system can recover, rather than merely whether it can process one message successfully.
