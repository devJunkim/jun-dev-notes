---
title: "AWS Secrets Manager Rotation: Caching, Version Stages, and Safe Application Reloads"
excerpt: "Use AWS Secrets Manager rotation with bounded client caches, version-stage semantics, least-privilege access, and deliberate connection reload behavior."
category: "Cloud"

seo:
  focusKeyword: "AWS Secrets Manager rotation"
  description: "Design AWS Secrets Manager rotation with AWSCURRENT, AWSPENDING, client caching, least privilege, connection refresh, and rollback testing."
  socialTitle: "AWS Secrets Manager Rotation Without Application Surprises"
  socialDescription: "Rotate credentials safely by aligning secret version stages, application caches, connection pools, and recovery behavior."
---

# AWS Secrets Manager Rotation: Caching, Version Stages, and Safe Application Reloads

A database password rotates successfully in AWS Secrets Manager, but application instances keep using pooled connections and an hour-old cached value. New connections fail while old ones continue working, producing a partial outage that looks random across the fleet.

Rotation is a protocol between the secret store, the target system, and every consumer. Updating the stored value is only one step.

> **Quick answer:** Read the version labeled `AWSCURRENT`, cache it for a bounded period, and design clients to rebuild connections when authentication fails or the cache refreshes. Grant only `GetSecretValue` and required decrypt permissions, test rotation under load, and keep recovery possible without logging secret material.

## Understand Version Stages

Secrets Manager stores versions and assigns staging labels. `AWSCURRENT` identifies the version returned by default, `AWSPENDING` is used during rotation, and `AWSPREVIOUS` commonly identifies the prior current version after a successful transition.

AWS documents these labels in [Secrets Manager secret concepts](https://docs.aws.amazon.com/secretsmanager/latest/userguide/whats-in-a-secret.html). Applications should normally request no explicit version ID so they follow `AWSCURRENT` as rotation advances.

Do not teach ordinary consumers to fall back to `AWSPREVIOUS` automatically. That can hide a broken rotation, extend compromised credentials, or create inconsistent fleet behavior. Recovery to a previous version should be an explicit operational decision with audit and validation.

## Cache Without Pretending the Value Is Static

Fetching a secret for every request adds latency, cost, and a new dependency to the hot path. Cache it in process with a refresh interval shorter than the acceptable rotation convergence window.

The AWS [.NET caching component guidance](https://docs.aws.amazon.com/secretsmanager/latest/userguide/retrieving-secrets_cache-net.html) notes that the cache uses an LRU policy and refreshes secrets hourly by default. That default is not automatically appropriate for every rotation or revocation requirement.

```csharp
using Amazon.SecretsManager.Extensions.Caching;

public sealed class DatabaseSecretReader : IDisposable
{
    private readonly SecretsManagerCache _cache;

    public DatabaseSecretReader()
    {
        _cache = new SecretsManagerCache(new SecretCacheConfiguration
        {
            CacheItemTTL = 300_000,
            MaxCacheSize = 16,
        });
    }

    public Task<string> GetCurrentAsync(string secretId) =>
        _cache.GetSecretString(secretId);

    public void Dispose() => _cache.Dispose();
}
```

The five-minute TTL and cache size are illustrative. Confirm the exact package API and version used by the application. Cache only approved secret identifiers; do not let user input choose arbitrary secret names.

The caching library is a convenience, not an encrypted vault inside the process. Restrict memory dumps, diagnostic access, and logging. Never place the returned JSON or connection string in telemetry.

## Coordinate Connection Pools With Refresh

Refreshing a string does not update connections already in a pool. Depending on the database and rotation strategy, existing sessions may remain valid while new authentication uses the rotated credential.

Wrap connection creation behind a component that reads the current cached credential and can rebuild its data source or clear affected pools deliberately. On an authentication failure:

1. distinguish invalid credentials from network and capacity failures;
2. refresh or replace the cached value through a bounded path;
3. rebuild new connections safely;
4. retry the idempotent operation at most within its deadline;
5. alert if the new current version still fails.

Do not clear every pool on every transient SQL exception. That can amplify an unrelated outage. Avoid unbounded fallback attempts across secret versions.

Some rotation strategies alternate between two database users so one set remains valid during transition. Others update one user in place. Choose the strategy based on the target system, replication behavior, permission maintenance, and tolerated overlap.

## Give Rotation and Consumers Different Permissions

Application roles generally need `secretsmanager:GetSecretValue` for specific secret ARNs plus `kms:Decrypt` when a customer-managed key requires it. They do not need permission to update stages, change rotation configuration, or read every secret in the account.

The rotation function needs narrowly scoped access to the secret, its encryption key, and the target system actions required to create, set, test, and finish the pending credential. Network reachability to the database is part of that design.

Keep environment variables or configuration limited to a secret identifier, not the secret value. Use an ECS task role, Lambda execution role, or workload identity rather than static AWS access keys. [Amazon ECS Task Roles](https://dev.jun-kim.net/2026/09/22/amazon-ecs-task-roles-least-privilege-access-without-static-aws-keys/) explains the ECS credential boundary without repeating it here.

Resource policies and cross-account access require special care: constrain principals, prevent unintended broad delegation, and test both retrieval and rotation from the actual runtime identity.

## Validate Before Moving Current

A managed or custom rotation workflow should create a pending version, apply it to the target, test it, and only then move `AWSCURRENT`. The test must prove the permissions the application needs, not merely that a TCP connection opens.

Rotation code must be idempotent because AWS may retry individual steps. The client request token identifies the candidate version; repeating a step should observe or complete the same version rather than create another credential.

If rotation fails, preserve evidence about the step, version ID, and safe error classification. Do not log the password or full secret JSON. Alarm on rotation failures and on application authentication errors after rotation.

## Test Convergence and Recovery

Exercise rotation in a production-like environment with multiple instances, caches at different ages, pooled connections, active requests, and a rolling deployment. Verify:

- `AWSPENDING` is tested before becoming current;
- every instance converges within the documented window;
- old credentials stop working when intended;
- authentication failures trigger bounded refresh rather than a retry storm;
- the service remains within connection and Secrets Manager API quotas;
- rollback or emergency replacement follows an audited runbook.

Track rotation age, failures by step, cache refresh failures, authentication rejection rate, and fleet convergence without recording secret values. A synthetic connection using the application role can detect permission drift, but it should not become an aggressive health probe against the database.

Rotation succeeds only when consumers adopt the new credential safely and the previous credential can be retired. Design that lifecycle before enabling a schedule, not during the first failed midnight rotation.
