---
title: "ChurchServe React Tutorial Part 19-1: Build a Bounded Request Cache"
excerpt: "Build a framework-neutral TypeScript request cache with shared promises, explicit freshness, LRU eviction, cancellation, and identity-safe clearing."
category: "React Tutorial"

seo:
  focusKeyword: "React TypeScript request cache"
  description: "Build a bounded TypeScript request cache for React with shared promises, TTL expiry, LRU eviction, cancellation, and safe invalidation."
  socialTitle: "ChurchServe React Part 19-1: A Bounded Request Cache"
  socialDescription: "Create a small, tested request cache that shares active work without retaining stale data forever."
---

# ChurchServe React Tutorial Part 19-1: Build a Bounded Request Cache

The stores from Part 18 prevent duplicate state ownership, but they do not yet define how long server responses remain reusable. Keeping data forever risks stale UI and unbounded memory. Refetching every time wastes work and makes back-and-forth navigation feel slower than necessary.

> **Quick answer:** Keep active requests separate from completed values. Share one promise per key, retain successful results for an explicit time, evict least-recently-used values when capacity is reached, and abort obsolete work during a forced refresh or full clear. Inject the clock so expiry tests remain deterministic.

## Define the cache contract

Create `src/core/data/requestCache.ts`. The cache is generic because it should know nothing about schedules, assignments, or React components:

```typescript
export type CacheLookup<T> =
  | { readonly hit: true; readonly value: T }
  | { readonly hit: false }

export type RequestCacheOptions = {
  readonly maxAgeMs: number
  readonly maxEntries: number
  readonly now?: () => number
}
```

The discriminated lookup matters. An event-detail request can legitimately return `undefined`, and `{ hit: true, value: undefined }` is different from a cache miss.

The constructor rejects a negative lifetime and a capacity that is not a positive integer. Invalid policy should fail when the store is assembled, not during an unrelated navigation.

## Separate retained values from active work

Use one map for completed entries and another for in-flight requests:

```typescript
type CacheEntry<T> = {
  readonly value: T
  readonly storedAt: number
}

type ActiveRequest<T> = {
  readonly controller: AbortController
  readonly promise: Promise<T>
}

export class RequestCache<TKey, TValue> {
  private readonly values = new Map<TKey, CacheEntry<TValue>>()
  private readonly requests = new Map<TKey, ActiveRequest<TValue>>()
  private generation = 0
}
```

An active promise coordinates concurrent callers. A completed entry is subject to freshness and capacity rules. Combining those responsibilities in one map makes failure, cancellation, and eviction behavior much harder to reason about.

## Read with explicit freshness and LRU behavior

`read` checks age and promotes a hit to the newest map position:

```typescript
read(key: TKey): CacheLookup<TValue> {
  const entry = this.values.get(key)
  if (!entry) return { hit: false }

  if (this.now() - entry.storedAt > this.options.maxAgeMs) {
    this.values.delete(key)
    return { hit: false }
  }

  this.values.delete(key)
  this.values.set(key, entry)
  return { hit: true, value: entry.value }
}
```

JavaScript maps preserve insertion order. Deleting and reinserting a hit moves it to the newest position, so the first key remains the least recently used.

The comparison uses `>` rather than `>=`. An entry is still fresh at exactly `maxAgeMs`; it expires when it becomes older than the configured boundary.

## Share one promise per key

`getOrLoad` first checks retained data, then active work:

```typescript
getOrLoad(
  key: TKey,
  load: (signal: AbortSignal) => Promise<TValue>,
  forceRefresh = false,
): Promise<TValue> {
  if (!forceRefresh) {
    const cached = this.read(key)
    if (cached.hit) return Promise.resolve(cached.value)

    const pending = this.requests.get(key)
    if (pending) return pending.promise
  } else {
    this.values.delete(key)
    this.cancelRequest(key)
  }

  // Start and register one new request.
}
```

Two callers asking for the same missing key receive the same promise. A forced refresh is different: it discards the retained value, aborts the active request for that key, and creates new work.

Starting the loader in a microtask gives the cache time to register the active request before user code executes:

```typescript
const controller = new AbortController()
const requestGeneration = this.generation
let request!: ActiveRequest<TValue>

const promise = Promise.resolve()
  .then(() => {
    controller.signal.throwIfAborted()
    return load(controller.signal)
  })
  .then((value) => {
    if (
      !controller.signal.aborted &&
      this.generation === requestGeneration &&
      this.requests.get(key) === request
    ) {
      this.set(key, value)
    }
    return value
  })
  .finally(() => {
    if (this.requests.get(key) === request) this.requests.delete(key)
  })

request = { controller, promise }
this.requests.set(key, request)
return promise
```

A rejected loader never reaches `set`, and `finally` removes the failed request. The next call can retry normally.

The identity comparison also protects a newer forced refresh. Even if an older loader ignores its abort signal and resolves late, it is no longer the registered request and cannot overwrite the newer value.

## Bound completed values

`set` refreshes the entry timestamp and evicts from the oldest end:

```typescript
set(key: TKey, value: TValue) {
  this.values.delete(key)
  this.values.set(key, { value, storedAt: this.now() })

  while (this.values.size > this.options.maxEntries) {
    const oldest = this.values.keys().next()
    if (oldest.done) break
    this.values.delete(oldest.value)
  }
}
```

This is intentionally a small in-memory LRU policy, not a general storage layer. It has no persistence, cross-tab synchronization, background refresh, or HTTP validator support.

## Make clearing safe across identities

Single-key invalidation removes only a completed value. Full clearing also cancels active work and advances a generation:

```typescript
invalidate(key: TKey) {
  this.values.delete(key)
}

clear() {
  this.generation++
  this.values.clear()
  for (const request of this.requests.values()) {
    request.controller.abort()
  }
  this.requests.clear()
}
```

The generation handles loaders that do not cooperate with cancellation. A response started before `clear()` can still settle for its original caller, but it cannot repopulate data belonging to a later authenticated feature scope.

## Test behavior rather than implementation details

Inject a controllable clock and test the exact contract:

```typescript
it('refreshes a value after its exact freshness boundary', async () => {
  let now = 0
  let loads = 0
  const cache = new RequestCache<string, string>({
    maxAgeMs: 100,
    maxEntries: 2,
    now: () => now,
  })
  const load = async () => `value-${++loads}`

  await cache.getOrLoad('events', load)
  now = 100
  await expect(cache.getOrLoad('events', load)).resolves.toBe('value-1')
  now = 101
  await expect(cache.getOrLoad('events', load)).resolves.toBe('value-2')
})
```

The focused suite also proves:

- concurrent calls share one loader;
- failures are not retained;
- recently used values survive LRU eviction;
- cached `undefined` remains distinguishable from a miss;
- forced refresh aborts and replaces active work;
- clearing prevents a late response from repopulating the cache;
- invalid policy values fail immediately.

## Result

ChurchServe now has a small TypeScript cache with explicit freshness, bounded memory, shared promises, cancellation, and safe clearing. It is framework-neutral: React will observe the feature stores that use it, not the cache itself.

Part 19-2 applies different policies to schedule lists, event details, and assignment boards, then coordinates those retained values with optimistic mutations.
