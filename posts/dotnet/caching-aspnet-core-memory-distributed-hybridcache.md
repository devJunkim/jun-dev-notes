---
title: "Caching in ASP.NET Core: In-Memory, Distributed, and HybridCache"
excerpt: "Choose between IMemoryCache, distributed caching, and HybridCache in ASP.NET Core while accounting for invalidation, stampedes, serialization, and scale-out."
category: ".NET"

seo:
  focusKeyword: "caching in ASP.NET Core"
  description: "Compare IMemoryCache, IDistributedCache, and HybridCache in ASP.NET Core with practical guidance for expiry, invalidation, stampedes, and multi-instance apps."
  socialTitle: "Caching in ASP.NET Core: Memory, Distributed, and Hybrid"
  socialDescription: "Choose a cache from workload evidence, define freshness and failure behavior, and avoid turning stale data into a correctness problem."
---

# Caching in ASP.NET Core: In-Memory, Distributed, and HybridCache

A cache can reduce latency and dependency load, but it also creates another copy of data with its own lifetime and failure modes. Adding one before measuring the bottleneck can make an application more complicated without making it faster.

ASP.NET Core applications can use local memory, an external distributed store, or `HybridCache`, which presents one API over local and optional distributed layers. The correct choice begins with consistency and topology, not with the fastest benchmark.

> **Quick answer:** Use `IMemoryCache` for small, process-local data that can be reloaded. Use `IDistributedCache` when instances must share cached values and you can own serialization and miss coordination. Use `HybridCache` for a simpler two-level model and in-process stampede protection. In every case, define keys, freshness, invalidation, size, and failure behavior before caching.

## Prove That a Cache Belongs There

Cache data when repeated reads are expensive enough to matter, the value is safe to reuse, and a bounded period of staleness is acceptable. Measure request latency, source load, hit ratio, miss cost, and memory before and after the change.

Do not cache merely because a method calls a database. A better query, index, response shape, or batching strategy may remove the actual bottleneck without creating stale copies. Also avoid caching secrets, authorization decisions without every relevant identity and policy dimension in the key, unbounded user input, or mutable objects whose callers can change shared state.

Caching error responses is a product decision. A short negative cache for a stable “not found” result can protect a source from repeated misses; caching a temporary outage can prolong it after recovery.

## `IMemoryCache` Is Local to One Process

`IMemoryCache` stores objects directly in the application process. It avoids network and serialization overhead, but every instance has different contents and loses them on restart.

```csharp
using Microsoft.Extensions.Caching.Memory;

public sealed record ProductSummary(int Id, string Name, decimal Price);

public sealed class ProductReader(
    IMemoryCache cache,
    ProductRepository repository)
{
    public Task<ProductSummary?> FindAsync(
        int productId,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"product:v1:{productId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SlidingExpiration = TimeSpan.FromMinutes(1);
                entry.Size = 1;

                return await repository.FindAsync(productId, cancellationToken);
            });
}
```

An absolute expiration caps total staleness. Sliding expiration keeps frequently accessed entries alive but should not be the only bound for data that must eventually refresh. `Size` has meaning only when the application configures a size limit and uses one consistent unit across entries.

The factory is not a universal single-flight guarantee. Concurrent callers can observe a miss and repopulate the same key. If misses are expensive, coordinate them explicitly or use `HybridCache`. Never hold a general lock across unrelated keys.

Local caching is appropriate for reference data, computed configuration views, or other values where instance-to-instance differences during the expiry window are acceptable. Sticky sessions do not make local caches coherent; they only influence which instance receives a request.

## `IDistributedCache` Shares Serialized Values

`IDistributedCache` stores byte or string values in an external implementation such as Redis, SQL Server, or Postgres. It supports scale-out because instances address the same store, but the application owns serialization, version compatibility, and miss handling.

```csharp
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

public sealed class DistributedProductReader(
    IDistributedCache cache,
    ProductRepository repository)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ProductSummary?> FindAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var key = $"product:v1:{productId}";
        var json = await cache.GetStringAsync(key, cancellationToken);

        if (json is not null)
        {
            return JsonSerializer.Deserialize<ProductSummary>(json, JsonOptions);
        }

        var product = await repository.FindAsync(productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        await cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(product, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            },
            cancellationToken);

        return product;
    }
}
```

The `v1` namespace allows an incompatible representation to move to a new key space during rollout. Treat cached JSON as a versioned contract when application versions overlap. Set payload limits and do not place large object graphs in a cache simply because serialization succeeds.

This cache-aside example has a race: several instances can miss and query the source together. A distributed lock can reduce that stampede, but adds lease, failure, and ownership problems. Often a bounded local single-flight mechanism, randomized expiry, source capacity protection, or `HybridCache` is simpler.

The cache is another dependency. Decide whether a cache outage should fall back to the source, fail fast, or shed load. Falling back from every instance at once can overload the source that the cache was protecting.

## `HybridCache` Unifies Local and Distributed Layers

`HybridCache`, introduced in .NET 9 through `Microsoft.Extensions.Caching.Hybrid`, uses an in-process primary cache and, when an `IDistributedCache` is registered, a secondary cache. It also combines concurrent population calls for the same key within one `HybridCache` instance.

```csharp
using Microsoft.Extensions.Caching.Hybrid;

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Cache");
});

builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 256 * 1024;
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(2),
    };
});
```

The following reader supplies state separately so the factory does not need a closure:

```csharp
public sealed class HybridProductReader(
    HybridCache cache,
    ProductRepository repository)
{
    public ValueTask<ProductSummary?> FindAsync(
        int productId,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"product:v1:{productId}",
            (Repository: repository, ProductId: productId),
            static async (state, token) =>
                await state.Repository.FindAsync(state.ProductId, token),
            cancellationToken: cancellationToken);
}
```

The stampede protection is local to one `HybridCache` instance. Several application instances can still populate the same distributed miss concurrently. The secondary cache reduces source reads after one instance fills it, but it does not create a cross-instance lock.

By default, strings and byte arrays receive special handling and other types use `System.Text.Json`; serializers can be configured. Review compatibility exactly as you would for `IDistributedCache`.

## Invalidation Is Part of the Write Path

Time-based expiry limits how long stale data survives, but it does not know that a product changed. Cache-aside systems commonly remove the affected key after the durable write commits:

```csharp
await repository.UpdateAsync(product, cancellationToken);
await cache.RemoveAsync($"product:v1:{product.Id}", cancellationToken);
```

That sequence still has a failure window: the database can commit and removal can fail. A short expiry bounds the inconsistency; a durable invalidation message can improve it when the requirement justifies the machinery. Never remove the cache before a database transaction that might roll back unless the extra miss is deliberately acceptable.

Tag invalidation in `HybridCache` is useful for related keys, but it does not physically search and delete every entry. More importantly, invalidating a key or tag on one server does not evict the local primary copies held by other servers. Their local expiration remains the coherence bound unless the application supplies another invalidation mechanism.

Versioned keys can make broad invalidation cheap: move from `catalog:v12:*` to `catalog:v13:*`, then let old entries expire. The trade-off is temporary duplicate storage.

## Prevent Stampedes Without Hiding Failure

A stampede occurs when many callers miss together and all perform the expensive load. It is especially likely after expiration, eviction, deployment, or cache recovery.

Useful controls include:

- per-key request coalescing;
- slightly randomized expiration to avoid synchronized misses;
- stale-while-refresh behavior where stale data is safe;
- concurrency limits around the source;
- prewarming only a small, measured hot set;
- backpressure or load shedding when neither cache nor source can cope.

Retries can make a stampede worse. If the source is failing, repeated cache factories multiply pressure. [HttpClient in .NET](https://dev.jun-kim.net/2026/09/15/httpclient-in-net-best-practices-for-calling-external-apis/) explains why retries need a bounded, idempotent contract.

## Choose from the Consistency Boundary

| Need | Starting point |
| --- | --- |
| One process; values are cheap to recreate | `IMemoryCache` |
| Multiple instances need a shared serialized value | `IDistributedCache` |
| Local speed plus shared secondary storage and a simpler API | `HybridCache` |
| Public HTTP response reuse | Evaluate output or response caching separately |
| Strongly current authorization or financial state | Usually read the authoritative source |

Track hit and miss rates, load duration, eviction, payload size, cache errors, and source load. A high hit rate is not success if the cache serves incorrect data or hides a broken invalidation path.

Caching is useful when its stale-data contract is explicit. Choose the smallest mechanism that satisfies that contract, and preserve a safe path for cold starts, eviction, and cache failure.
