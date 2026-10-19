---
title: "ChurchServe Angular Tutorial Part 19-1: Build a Bounded Request Cache"
excerpt: "Create a reusable Angular request cache with in-flight sharing, explicit expiry, LRU eviction, deterministic clocks, and focused tests."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular request cache shareReplay"
  description: "Build and test a bounded Angular request cache with shareReplay, TTL expiry, LRU eviction, forced refresh, and safe clearing."
  socialTitle: "ChurchServe Part 19-1: Bounded Request Cache"
  socialDescription: "Coordinate duplicate Angular HTTP requests without creating an unlimited or permanently stale cache."
---

# ChurchServe Angular Tutorial Part 19-1: Build a Bounded Request Cache

[Part 18-3](https://dev.jun-kim.net/2026/10/19/churchserve-angular-tutorial-part-18-3-optimistic-angular-assignments/) completed ChurchServe’s optimistic assignment workflow. That workflow now creates several related reads: schedule pages, event details, and assignment boards.

Before caching those resources, ChurchServe needs one small abstraction with explicit behavior. Part 19-1 builds that abstraction. Part 19-2 will apply it to the schedule feature and define resource-specific invalidation rules.

> **Quick answer:** Keep in-flight request sharing separate from retained values. Key each request explicitly, expire completed values after a configured duration, cap the number of retained entries, evict the least recently used entry, and make clearing prevent older requests from repopulating the cache.

## Files Added

```text
src/app/core/data/
├── cache-clock.ts
├── request-cache.spec.ts
└── request-cache.ts
```

The cache lives under `core/data` because it has no schedule-domain dependency. Feature stores choose the keys, lifetimes, capacities, and invalidation events.

## Coordination Is Not the Same as Retention

Two requests arriving together need **in-flight coordination**: both subscribers should observe one HTTP operation. A later request needs a **retention policy**: it may reuse a completed value only while that value is still fresh.

`shareReplay` coordinates subscribers, but it does not decide:

- which parameters belong in the cache key;
- how long a result is safe to reuse;
- how many entries may remain in memory;
- which mutation invalidates the value;
- whether a new identity may see a previous identity’s response.

Those are domain decisions. ChurchServe therefore avoids a blanket caching interceptor and gives feature stores explicit control.

## Inject a Testable Clock

Expiry tests should advance a number rather than wait in real time. Create `cache-clock.ts`:

```typescript
import { InjectionToken } from '@angular/core';

export type CacheClock = () => number;

export const CACHE_CLOCK = new InjectionToken<CacheClock>('CACHE_CLOCK', {
  providedIn: 'root',
  factory: () => Date.now,
});
```

Application code receives `Date.now` by default. Tests can provide `() => now` and move `now` across the expiry boundary instantly.

## Define the Cache Contract

Create `request-cache.ts` and start with its configuration and internal types:

```typescript
import { defer, finalize, Observable, of, shareReplay, tap } from 'rxjs';

export interface RequestCacheOptions {
  readonly maxAgeMs: number;
  readonly maxEntries: number;
  readonly now?: () => number;
}

type CacheLookup<T> = { readonly hit: true; readonly value: T } | { readonly hit: false };

interface CacheEntry<T> {
  readonly value: T;
  readonly storedAt: number;
}
```

`CacheLookup` deliberately distinguishes a miss from a cached `undefined`. That matters when an event-detail `404` is a valid result that may be retained briefly.

## Implement Fresh Reads and LRU Ordering

The class keeps completed values separate from active requests:

```typescript
export class RequestCache<TKey, TValue> {
  private readonly values = new Map<TKey, CacheEntry<TValue>>();
  private readonly requests = new Map<TKey, Observable<TValue>>();
  private readonly now: () => number;
  private generation = 0;

  constructor(private readonly options: RequestCacheOptions) {
    if (options.maxAgeMs < 0) {
      throw new Error('Request cache maxAgeMs cannot be negative.');
    }

    if (!Number.isInteger(options.maxEntries) || options.maxEntries < 1) {
      throw new Error('Request cache maxEntries must be a positive integer.');
    }

    this.now = options.now ?? Date.now;
  }

  read(key: TKey): CacheLookup<TValue> {
    const entry = this.values.get(key);
    if (!entry) {
      return { hit: false };
    }

    if (this.now() - entry.storedAt > this.options.maxAgeMs) {
      this.values.delete(key);
      return { hit: false };
    }

    // Reinsert fresh hits so Map insertion order acts as a small LRU list.
    this.values.delete(key);
    this.values.set(key, entry);
    return { hit: true, value: entry.value };
  }
```

JavaScript maps preserve insertion order. Removing and reinserting a hit moves it to the newest position, leaving the first key as the least recently used entry.

The comparison uses `>` rather than `>=`. A value remains fresh at exactly `maxAgeMs` and expires once it becomes older than that limit.

## Share In-Flight Requests

Add `getOrLoad`:

```typescript
  getOrLoad(
    key: TKey,
    load: () => Observable<TValue>,
    forceRefresh = false,
  ): Observable<TValue> {
    if (!forceRefresh) {
      const cached = this.read(key);
      if (cached.hit) {
        return of(cached.value);
      }
    } else {
      this.values.delete(key);
    }

    const pending = this.requests.get(key);
    if (pending) {
      return pending;
    }

    const requestGeneration = this.generation;
    let request!: Observable<TValue>;
    request = defer(load).pipe(
      tap((value) => {
        if (this.generation === requestGeneration) {
          this.set(key, value);
        }
      }),
      finalize(() => {
        if (this.requests.get(key) === request) {
          this.requests.delete(key);
        }
      }),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
    this.requests.set(key, request);
    return request;
  }
```

`defer(load)` constructs the underlying observable on subscription. Concurrent callers receive the same `request` observable. `shareReplay` sends the one response to every subscriber, while `finalize` removes the active request after success, failure, or cancellation.

`refCount: true` allows Angular `HttpClient` to cancel the network request when all consumers unsubscribe. A failed request never enters `values`, so the next call can retry normally.

## Bound Memory and Support Invalidation

Complete the class with `set`, `invalidate`, and `clear`:

```typescript
  set(key: TKey, value: TValue): void {
    this.values.delete(key);
    this.values.set(key, { value, storedAt: this.now() });

    while (this.values.size > this.options.maxEntries) {
      const oldest = this.values.keys().next();
      if (oldest.done) {
        break;
      }
      this.values.delete(oldest.value);
    }
  }

  invalidate(key: TKey): void {
    this.values.delete(key);
  }

  clear(): void {
    this.generation++;
    this.values.clear();
    this.requests.clear();
  }
}
```

The generation is more than a bookkeeping counter. A request started before `clear()` captured an older generation. If its response arrives later, `tap` refuses to retain that value. This is essential when clearing is caused by logout or a permission change.

## Test Request Sharing

Create `request-cache.spec.ts`. Use a controlled `Subject` to prove two subscribers share one load:

```typescript
it('shares one in-flight request and retains its result', () => {
  const response = new Subject<string>();
  let loads = 0;
  const load = (): Observable<string> => {
    loads++;
    return response;
  };
  const values: string[] = [];

  cache.getOrLoad('events', load).subscribe((value) => values.push(value));
  cache.getOrLoad('events', load).subscribe((value) => values.push(value));
  expect(loads).toBe(1);

  response.next('page-1');
  response.complete();
  cache.getOrLoad('events', load).subscribe((value) => values.push(value));

  expect(values).toEqual(['page-1', 'page-1', 'page-1']);
  expect(loads).toBe(1);
});
```

The third call happens after completion and reads the retained value rather than the in-flight observable.

## Test Expiry and Failures

Advance the injected clock across the exact boundary:

```typescript
it('refreshes expired entries using an injected clock', () => {
  let loads = 0;
  const load = () => of(`value-${++loads}`);

  cache.getOrLoad('events', load).subscribe();
  now += 100;
  cache.getOrLoad('events', load).subscribe();
  now += 1;
  cache.getOrLoad('events', load).subscribe();

  expect(loads).toBe(2);
});
```

Failed requests must be retryable:

```typescript
it('does not retain failed requests', () => {
  let loads = 0;
  const load = () => {
    loads++;
    return loads === 1 ? throwError(() => new Error('offline')) : of('recovered');
  };

  cache.getOrLoad('events', load).subscribe({ error: () => undefined });
  cache.getOrLoad('events', load).subscribe();

  expect(loads).toBe(2);
  expect(cache.read('events')).toEqual({ hit: true, value: 'recovered' });
});
```

## Test Eviction and Identity-Safe Clearing

Touching `one` makes `two` the least recently used value:

```typescript
it('evicts the least recently used value when full', () => {
  cache.set('one', '1');
  cache.set('two', '2');
  cache.read('one');
  cache.set('three', '3');

  expect(cache.read('one')).toEqual({ hit: true, value: '1' });
  expect(cache.read('two')).toEqual({ hit: false });
  expect(cache.read('three')).toEqual({ hit: true, value: '3' });
});
```

Finally, prove that a late response cannot repopulate a cleared cache:

```typescript
it('does not retain a response that arrives after the cache was cleared', () => {
  const response = new Subject<string>();
  cache.getOrLoad('one', () => response).subscribe();

  cache.clear();
  response.next('old identity');
  response.complete();

  expect(cache.read('one')).toEqual({ hit: false });
});
```

The remaining focused test covers forced refresh, single-key invalidation, and full clearing.

## Result

ChurchServe now has a small cache primitive with explicit expiry, capacity, LRU behavior, request sharing, cancellation, invalidation, and identity-safe clearing. It contains no schedule assumptions.

Part 19-2 will choose concrete policies for schedule pages, event details, and assignment boards, then connect cache invalidation to application mutations and session changes.
