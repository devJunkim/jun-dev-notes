---
title: "Signals vs RxJS Observables in Angular: When to Use Which"
excerpt: "Choose Signals for current UI state and RxJS for event coordination, with practical Angular examples covering computed state, HTTP, effects, and interop."
category: "Angular"

seo:
  focusKeyword: "Angular Signals vs RxJS Observables"
  description: "Compare Angular Signals and RxJS Observables for component state, computed values, HTTP, event streams, effects, and practical interoperability."
  socialTitle: "Signals vs RxJS Observables in Angular: When to Use Which"
  socialDescription: "Choose Signals for current UI state and RxJS for event coordination, with practical Angular examples covering computed state, HTTP, effects, and interop."
---

# Signals vs RxJS Observables in Angular: When to Use Which

A selected tab has one current value. A search box produces a sequence of user inputs, requests, cancellations, and responses. Both are reactive, but they ask different questions: what should the screen show now, and what should happen as events arrive?

Signals make current state easier to model. RxJS remains useful for describing time, composition, and asynchronous policies. A maintainable Angular application can use both without turning every value into both.

> **Quick answer:** Prefer Signals for local state and synchronous derivations. Prefer RxJS when ordering, cancellation, timing, or multiple asynchronous sources define the behavior. Convert at a clear boundary when an Observable should become state that the template reads.

## Signals and Observables at a Glance

| Concern | Signal | Observable |
| --- | --- | --- |
| Primary model | A current value | A stream of notifications |
| Reading | Synchronous getter | Subscription |
| Derived values | `computed` | Operators such as `map` |
| Time and concurrency | Need an appropriate async abstraction | Rich operator composition |
| Error/completion | Usually represented in state | Explicit terminal notifications |
| Template consumption | Call the signal | `async` pipe or convert with `toSignal` |
| Typical use | Selection, toggles, derived display state | HTTP composition, events, retries, sockets |

An Observable can emit synchronously. A signal can contain data that originally arrived asynchronously. The distinction is the model and its lifecycle, not a rule that one is always synchronous and the other always asynchronous.

Angular also provides resource APIs for asynchronous state. Those can fit data-loading features; they do not make event ordering and stream composition disappear. The examples here use the established Signals and RxJS interop APIs so the boundary remains explicit.

## Local State Is Usually Simpler with Signals

A cart summary needs quantities and a total. It does not inherently need subscription management:

```typescript
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';

interface CartLine {
  id: string;
  unitPrice: number;
  quantity: number;
}

@Component({
  selector: 'app-cart-summary',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>Items: {{ itemCount() }}</p>
    <p>Total: {{ total() }}</p>
    <button (click)="addOne('book')">Add a book</button>
  `,
})
export class CartSummaryComponent {
  readonly lines = signal<readonly CartLine[]>([
    { id: 'book', unitPrice: 30, quantity: 1 },
  ]);

  readonly itemCount = computed(() =>
    this.lines().reduce((sum, line) => sum + line.quantity, 0));

  readonly total = computed(() =>
    this.lines().reduce(
      (sum, line) => sum + line.unitPrice * line.quantity, 0));

  addOne(id: string): void {
    this.lines.update(lines =>
      lines.map(line =>
        line.id === id
          ? { ...line, quantity: line.quantity + 1 }
          : line));
  }
}
```

This example uses ordinary numbers for a display calculation. Authoritative pricing, currency rounding, and payment totals belong in the backend's business rules.

The state update replaces the changed objects and the array. Mutating `this.lines()[0].quantity` directly would not express a signal update. A readonly TypeScript annotation also does not freeze the runtime objects.

The [Angular Signals guide](https://angular.dev/guide/signals) explains that computed values are lazy and memoized, and that dependencies are tracked from signal reads. Here, those mechanics let both totals follow the lines without maintaining duplicate writable state.

Avoid storing `total` in a second writable signal and updating it with an effect. That introduces another state value that can drift from the source. If the value can be derived, describe the derivation.

## Effects Are for Side Effects

An effect is useful when reactive state must update something outside the normal reactive view: telemetry, an imperative integration, or an external API. It should not be the default way to connect one signal to another.

For example, this fragment synchronizes a user preference with an injected adapter:

```typescript
import { effect, inject, Injectable, signal } from '@angular/core';

@Injectable()
export abstract class PreferenceWriter {
  abstract setDensity(value: 'compact' | 'comfortable'): void;
}

@Injectable()
export class DisplayPreferences {
  private readonly writer = inject(PreferenceWriter);
  readonly density = signal<'compact' | 'comfortable'>('comfortable');

  constructor() {
    effect(() => this.writer.setDensity(this.density()));
  }
}
```

The application must provide a concrete `PreferenceWriter`. A browser implementation might use storage; an SSR implementation must handle the absence of browser APIs. The adapter should tolerate the initial write and implement an appropriate error policy.

Create effects in an injection context, or supply the necessary injector explicitly. If an effect starts work that survives a rerun, arrange cleanup. Do not turn an async effect into an improvised request manager without defining stale-response handling.

Effects track synchronous signal reads. Reading a signal only after an `await` does not establish the same tracked dependency. More generally, a reactive side effect is not a durable queue: do not rely on it to process every transient intermediate state.

## HTTP Requests Still Have a Lifecycle

`HttpClient` returns Observables. A request typically emits a response and completes, or errors. Subscribing starts the request; subscribing again can start another request.

For Observable fundamentals, subscription cleanup, and a typeahead built entirely with RxJS, see [Angular Observables and RxJS: A Practical Guide](https://dev.jun-kim.net/2026/09/11/angular-observables-and-rxjs-a-practical-guide/). The example below focuses on the boundary between signal state and an asynchronous pipeline.

A component can keep that Observable and use the `async` pipe. It can also convert the result to a signal if the view benefits from synchronous state reads and computed derivations. Neither approach requires manually copying each response into a writable signal.

The key is to preserve loading, success, and failure as distinct states. An empty array may mean "no matches," but it should not also silently mean "the request failed."

## A Search Example Combining Both Models

This standalone component uses a signal for the query, RxJS for request coordination, and a signal for the current search state. Register `provideHttpClient()` in the application's providers.

```typescript
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import {
  catchError, distinctUntilChanged, map,
  of, startWith, switchMap, timer,
} from 'rxjs';

interface Product {
  id: string;
  name: string;
}

type SearchState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ready'; products: Product[] }
  | { kind: 'error'; message: string };

@Component({
  selector: 'app-product-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label>
      Search products
      <input #searchBox [value]="query()"
             (input)="query.set(searchBox.value)" />
    </label>

    @let result = state();
    @switch (result.kind) {
      @case ('idle') { <p>Enter at least two characters.</p> }
      @case ('loading') { <p>Searching...</p> }
      @case ('error') { <p role="alert">{{ result.message }}</p> }
      @case ('ready') {
        <ul>
          @for (product of result.products; track product.id) {
            <li>{{ product.name }}</li>
          } @empty {
            <li>No matching products.</li>
          }
        </ul>
      }
    }
  `,
})
export class ProductSearchComponent {
  private readonly http = inject(HttpClient);
  readonly query = signal('');

  private readonly state$ = toObservable(this.query).pipe(
    map(value => value.trim()),
    distinctUntilChanged(),
    switchMap(query => {
      if (query.length < 2) {
        return of<SearchState>({ kind: 'idle' });
      }

      return timer(300).pipe(
        switchMap(() => this.http.get<Product[]>('/api/products', {
          params: { q: query },
        })),
        map((products): SearchState => ({ kind: 'ready', products })),
        startWith<SearchState>({ kind: 'loading' }),
        catchError(() => of<SearchState>({
          kind: 'error',
          message: 'Products could not be loaded. Try another search.',
        })),
      );
    }),
  );

  readonly state = toSignal(this.state$, {
    initialValue: { kind: 'idle' } as SearchState,
  });
}
```

The query is state. RxJS normalizes input, ignores repeated normalized values, waits before each request, and switches to the latest search. Converting the final state once keeps the template straightforward.

The outer `switchMap` runs when the normalized query changes. It unsubscribes from the prior timer or request immediately, and `startWith` replaces any displayed result with a loading state before the 300 ms wait. A query shorter than two characters switches straight to idle. Because `toObservable` emits after signal stabilization, several synchronous signal updates can be coalesced; individual keystrokes in separate input events still trigger separate updates.

Placing `catchError` inside the request pipeline lets a failed request become one error state while future searches remain active. A catch at the outermost level that returns a finite fallback can complete the entire search stream.

Unsubscribing from an Angular HTTP request aborts the client request where supported. It does not undo work the server has already committed. This distinction makes search cancellation sensible but makes blindly cancelling writes dangerous.

## Where Stream Composition Still Matters

The search uses `switchMap` because an older result becomes irrelevant when the query changes. Ordered writes, independent concurrent requests, and submissions that must ignore repeat clicks need different policies. RxJS also combines live sources or waits for several finite requests. Choose those operators from the feature's ordering and completion requirements; the linked Observable guide covers their mechanics in detail. Client-side cancellation and ordering do not replace backend idempotency or concurrency checks.

## Interop Requires an Ownership Decision

`toSignal` subscribes immediately. Create it once and reuse the returned signal instead of calling it in a getter, a template expression, or repeatedly inside other reactive work.

By default it uses the creating injection context for cleanup. Provide an initial value when the source may emit later. `requireSync: true` is appropriate only when the source really guarantees a synchronous emission, such as a suitable `BehaviorSubject`; it is not appropriate for an HTTP request.

An unhandled Observable error can surface when reading the resulting signal. Convert errors into an intentional state before crossing the boundary if the template must display them. Completion leaves the latest signal value available.

In the other direction, `toObservable` uses an effect to expose signal state. Synchronous signal changes can be coalesced after stabilization. It is therefore not a replacement for a source that must retain every click or event. These lifecycle details are documented in Angular's [Signals interop guide](https://angular.dev/ecosystem/rxjs-interop).

For imperative subscriptions that are necessary, use an appropriate destruction strategy such as `takeUntilDestroyed`. The `async` pipe remains a good choice when an existing Observable already describes exactly what the template needs.

## Common Mistakes

Avoid rewriting a working RxJS feature solely to remove dollar signs. Migration should simplify ownership or behavior, not merely change the API vocabulary.

Other recurring mistakes include nested subscriptions, duplicate HTTP subscriptions through multiple conversions, writable copies of derived state, and effects that silently issue repeated network requests. Shared services also require attention: a root-scoped signal lives beyond one component, which may be wrong for temporary form state.

A good test suite verifies behavior across time. For search, test request failure followed by another query, rapid input changes, an empty result, and component destruction. For local computed state, direct updates and reads are often sufficient.

## Practical Decision Guide

| Situation | Recommended starting point |
| --- | --- |
| Toggle, selected ID, expanded panel | Signal |
| Filter or total derived from local state | `computed` |
| Existing HTTP Observable displayed directly | `async` pipe |
| Async pipeline whose latest state drives several computed values | RxJS plus one `toSignal` boundary |
| Ordered writes, concurrent requests, retries, socket events | RxJS |
| Sync with an imperative external API | Effect with deliberate lifetime |
| Need every individual user event | Event stream, not signal-state observation |

Signals simplify the representation of what the UI currently knows. RxJS makes the rules for arriving at that state explicit. Keep each where it clarifies the feature, and make conversions a boundary rather than a habit.
