---
title: "Expand and Contract Database Migrations: Deploying Compatible Schema Changes"
excerpt: "Plan database changes across overlapping application versions with additive schema updates, compatible writers, bounded backfills, and an explicit rollback window."
category: "Architecture"

seo:
  focusKeyword: "expand and contract database migrations"
  description: "Use expand and contract database migrations to coordinate old and new applications, backfill data safely, and define realistic rollback limits."
  socialTitle: "Expand and Contract Database Migrations"
  socialDescription: "Make schema changes a sequence of compatible releases instead of betting that every application instance upgrades at once."
---

# Expand and Contract Database Migrations: Deploying Compatible Schema Changes

Renaming a column can be a one-line migration and still break a rolling deployment. The new application expects the new name while old instances, scheduled jobs, and reports continue to query the old one.

The hard part is the period when several versions share one database. A migration plan must describe what each version can read and write throughout that period, including after a rollback.

> **Quick answer:** Add the new representation first, deploy a compatibility version, migrate existing data in bounded steps, switch reads only after reconciliation, and remove the old representation after its rollback and consumer obligations expire. Treat expansion and contraction as separate releases with observable exit criteria.

## Start with a Compatibility Matrix

Suppose an order table has a nullable `delivery_instructions` column. The team wants the clearer name `delivery_note` without changing the meaning of its contents. A direct rename would require every consumer to switch together.

Use a bridge release that can work with both columns:

| Release | Reads | Writes | Required schema |
| --- | --- | --- | --- |
| Original | Old column | Old column | Old column |
| Bridge | Old column | Both columns in one transaction | Both columns |
| Switched | New column | Both columns in one transaction | Both columns |
| Contract-ready | New column | New column | New column; tolerates old column still existing |

The old column remains authoritative while original writers exist. The bridge does not read the new column just because it is populated: an original writer could have changed only the old value since that population.

List all consumers before scheduling the work. Background processes, bulk imports, support scripts, reporting queries, and delayed jobs belong in the matrix even when they are absent from the main application's deployment dashboard.

AWS's guidance on [blue/green application deployments](https://aws.amazon.com/blogs/startups/upgrades-without-tears-part-2-bluegreen-deployment-step-by-step-on-aws/) highlights the same compatibility constraint when application environments overlap. Routing traffic differently does not make a shared database private to one release.

## Expand Without Changing the Existing Contract

The first change adds the new nullable column and leaves the original intact. For this example, a PostgreSQL migration contains:

```sql
ALTER TABLE orders ADD COLUMN delivery_note text NULL;
```

This is an illustrative statement for an existing table, not a complete deployment script. Review the engine version, schema qualification, lock behavior, and available disk space before applying it.

Additive does not mean free of operational risk. DDL still requires locks, and defaults, constraints, indexes, or type conversions can change how much work the engine performs. Set a bounded lock-wait policy and rehearse against realistic data and concurrent transactions. Do not call a rollout zero-downtime solely because no column was dropped.

Only deploy the bridge after expansion succeeds. It writes the same value to both columns in one database transaction and continues reading the old one. The new nullable column avoids demanding values that original applications do not know how to supply.

For a semantic transformation rather than a rename, write down the conversion and its reversibility first. Splitting a free-text address into structured fields is not a mechanical copy, and may require unresolved states, human review, or a deliberate loss of rollback capability.

## Retire Original Writers Before Final Reconciliation

Wait until original instances and jobs have drained before treating the new representation as current. A dashboard showing the bridge deployment complete is useful evidence, but check worker revisions, scheduled tasks, and alternate write paths too.

If original and bridge writers overlap, both can produce legitimate updates. An early backfill is possible, but its results are provisional until old-only writes stop and reconciliation runs again.

Do not solve this with two independent application calls: one update for the old column and another for the new column. A failure between them creates contradictory rows. In this example both columns are in one database, so the update belongs in one transaction. Cross-store replication requires its own durable design; [Transactional Outbox Pattern in .NET](https://dev.jun-kim.net/2026/09/17/transactional-outbox-pattern-in-net-reliable-event-delivery/) explains the different boundary involved there.

## Backfill as Resumable Work

Avoid one unbounded transaction across a large table. Use small batches, record progress, and tune the batch size from lock duration, replication lag, and foreground latency.

After original writers are retired, this PostgreSQL batch copies mismatched values while locking the selected rows:

```sql
WITH batch AS (
    SELECT id
    FROM orders
    WHERE delivery_note IS DISTINCT FROM delivery_instructions
    ORDER BY id
    LIMIT 500
    FOR UPDATE SKIP LOCKED
)
UPDATE orders AS target
SET delivery_note = target.delivery_instructions
FROM batch
WHERE target.id = batch.id;
```

The sample assumes a primary key named `id`, nullable text columns, and a short transaction per execution. `IS DISTINCT FROM` compares nulls as well as text, so a legitimate null can replace a stale non-null value. Copying only rows where the new column is null would miss stale values written during the mixed-version interval.

PostgreSQL documents [`UPDATE ... FROM`](https://www.postgresql.org/docs/current/sql-update.html) and [row-locking clauses](https://www.postgresql.org/docs/current/sql-select.html). Other database engines need their own batching and concurrency strategy; this syntax is not portable SQL.

The row lock prevents a writer from changing a selected row while the batch updates it. A bridge update that runs afterward writes both representations. This reasoning depends on every remaining application writer following the bridge contract.

An empty batch is not proof of completion: `SKIP LOCKED` can skip all remaining candidates. Perform an independent reconciliation query and account for active transactions before switching reads:

```sql
SELECT count(*) AS mismatched_rows
FROM orders
WHERE delivery_note IS DISTINCT FROM delivery_instructions;
```

The repeated update is safe because it converges on the current authoritative value instead of appending another effect. [Idempotency in Distributed Systems](https://dev.jun-kim.net/2026/09/22/idempotency-in-distributed-systems-designing-safe-retryable-operations/) discusses that property more broadly. A migration can be safely repeatable and still be expensive, so rate limits and cancellation remain useful.

The row limit bounds updates, not necessarily rows examined. As matching rows disappear, this simple query may repeatedly scan a growing prefix of already-synchronized rows. Inspect its plan and consider a suitable temporary index or checkpointed key ranges followed by final reconciliation for a large table. Remove migration-only indexes deliberately when their job is complete.

## Switch Reads While Preserving Rollback

Deploy the switched release only after reconciliation succeeds and the write path keeps both columns synchronized. During its rollout, bridge readers use the old column and switched readers use the new one. Both should observe the same committed value.

Keep dual writes for the promised rollback window. Rolling back to the bridge remains plausible because its read column is maintained. Rolling back to the original release reintroduces old-only writes and therefore invalidates the new-column freshness guarantee. That rollback requires another reconciliation before switching forward again.

Observe fallback usage if the new reader has a compatibility fallback. A fallback can make a rollout look successful while every request still depends on the old representation. A permanent fallback without an owner makes contraction unlikely to happen safely.

For this nullable rename, null is valid data rather than a migration marker. Do not interpret every null new value as permission to fall back indefinitely. Use rollout state and measured reconciliation to determine readiness.

## Contract Only After the Rollback Floor Moves

Before deploying the contract-ready release, retire every old-column reader and end support for rollback to the bridge or original release. The first new-only write can leave the old column stale even while that column still exists. The rollback floor must therefore move to the switched release before those writes begin.

Then deploy contract-ready code and drain every remaining dual writer, including external jobs. Before dropping the column, move the rollback floor again: the switched release still writes the old column and cannot run against the contracted schema. Only contract-ready versions remain compatible.

Remove the obsolete column in a separate database release after both gates pass. Restoring an older application binary cannot recreate a dropped column or recover values discarded by a lossy conversion. Retain backups with a tested recovery procedure, while recognizing that a database restore also affects business writes made after the recovery point.

An EF Core migration's `Down` method is not evidence that live business data can be restored safely. Review generated migrations and SQL; Microsoft's [production migration guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) describes reviewed scripts and migration bundles as deployment artifacts. Generate and approve the database change independently of ordinary request handling.

## Rehearse the Mixed-Version Failure Paths

Test original and bridge writers together, a crash halfway through a backfill, a locked row skipped by a batch, an update racing with reconciliation, and a rollback during the read switch. Include rows with null, empty, and ordinary text values.

Measure more than schema version: incompatible consumer count, remaining mismatches, backfill throughput, lock waits, replication lag, and foreground errors show whether the transition is safe to advance.

Expand-and-contract buys a controlled compatibility interval. Its cost is temporary duplicate representation and several release gates. Pay that cost deliberately where rolling upgrades and rollback matter; for a small system with an acceptable maintenance window, a coordinated migration may be simpler.
