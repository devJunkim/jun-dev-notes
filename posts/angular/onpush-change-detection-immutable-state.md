---
title: "Angular OnPush Change Detection: Immutable State and Debugging Stale Views"
excerpt: "Use Angular OnPush change detection with immutable inputs, signals, observable bindings, and explicit boundaries that prevent stale UI surprises."
category: "Angular"

seo:
  focusKeyword: "Angular OnPush change detection"
  description: "Understand Angular OnPush change detection, immutable input updates, signals, AsyncPipe, markForCheck, and practical stale-view debugging."
  socialTitle: "Angular OnPush Change Detection Without Stale Views"
  socialDescription: "Build predictable Angular component boundaries with immutable inputs, signals, and focused debugging instead of manual detection calls everywhere."
---

# Angular OnPush Change Detection: Immutable State and Debugging Stale Views

A parent updates `customer.address.city`, the debugger shows the new value, and an OnPush child still displays the old city. The child is not randomly stale. The object reference crossing its input boundary did not change.

OnPush works best as a design constraint: inputs behave like immutable snapshots, state changes have explicit owners, and templates subscribe through Angular-aware mechanisms.

> **Quick answer:** Pass new input references when state changes, read signals in templates, and bind observables with `AsyncPipe`. Use `markForCheck()` only at integration boundaries Angular cannot observe. Do not treat `detectChanges()` as a general fix for mutable state.

## What Makes an OnPush View Eligible for Checking

Angular can skip an OnPush component subtree when it has no relevant change signal. Important triggers include a new value arriving through a template-bound input, an event handled in that subtree, a signal read by the template changing, and an explicit change-detection notification.

Angular's [subtree-skipping guide](https://angular.dev/best-practices/skipping-subtrees) documents the traversal rules and the mutable-input edge case. OnPush does not mean a component renders once, nor does it prevent ancestor traversal after events.

Configure the strategy explicitly when supporting Angular versions or codebases with different defaults:

```typescript
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface CustomerSummary {
  readonly id: string;
  readonly name: string;
  readonly city: string;
}

@Component({
  selector: 'app-customer-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article>
      <h2>{{ customer().name }}</h2>
      <p>{{ customer().city }}</p>
    </article>
  `,
})
export class CustomerCardComponent {
  readonly customer = input.required<CustomerSummary>();
}
```

The `readonly` declarations communicate intent to TypeScript callers. They do not deep-freeze runtime objects, so the update path must still create a new value.

## Replace the Value Instead of Mutating It

This update creates a new object reference:

```typescript
import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-customer-page',
  template: `
    <app-customer-card [customer]="customer()" />
    <button type="button" (click)="moveToToronto()">Move</button>
  `,
})
export class CustomerPageComponent {
  readonly customer = signal<CustomerSummary>({
    id: 'customer-42',
    name: 'Avery Chen',
    city: 'Ottawa',
  });

  moveToToronto(): void {
    this.customer.update(current => ({
      ...current,
      city: 'Toronto',
    }));
  }
}
```

Mutating `current.city` and returning `current` preserves the same identity. Even when another trigger happens to refresh the screen, the design remains brittle because correctness depends on unrelated activity.

For nested state, copy each changed level or use a state abstraction that guarantees immutable updates. Do not deep-clone the whole application tree for every keystroke. Model smaller ownership boundaries so updates remain focused.

## Signals and Observables Need Angular-Aware Consumption

When an OnPush template reads a signal, Angular tracks that dependency and marks the component when the signal changes. For observables, `AsyncPipe` subscribes, exposes the latest value, marks the view appropriately, and cleans up with the view.

```typescript
import { AsyncPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CustomerStore } from './customer.store';

@Component({
  selector: 'app-customer-status',
  imports: [AsyncPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.status$ | async; as status) {
      <p>{{ status }}</p>
    }
  `,
})
export class CustomerStatusComponent {
  readonly store = inject(CustomerStore);
}
```

A manual subscription that assigns a plain field may require explicit notification and cleanup. Prefer `AsyncPipe`, signals, or framework interop helpers because they make the scheduling relationship visible.

Do not subscribe in a getter called by the template. That creates work during rendering and can leak subscriptions or produce inconsistent values.

## Reserve `markForCheck` for Real Integration Boundaries

Some callback sources live outside Angular's normal awareness: a legacy library, a custom browser API wrapper, or an imperative child update through `ViewChild`. Adapt that source once and notify Angular deliberately:

```typescript
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  inject,
} from '@angular/core';

@Component({
  selector: 'app-legacy-clock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<time>{{ value }}</time>`,
})
export class LegacyClockComponent {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  value = '';

  constructor(clock: LegacyClock) {
    const unsubscribe = clock.subscribe(value => {
      this.value = value;
      this.cdr.markForCheck();
    });

    this.destroyRef.onDestroy(unsubscribe);
  }
}
```

`markForCheck()` makes the view eligible for a later check. `detectChanges()` immediately checks a view and its children; used broadly, it can hide broken ownership, add duplicate work, or cause timing-dependent behavior. Keep either call close to the integration that requires it.

## Debug the First Broken Boundary

When a view is stale, inspect the state transition in order:

1. Did the producer emit or set a new value?
2. Did the changed object or array receive a new reference?
3. Is the template reading the signal or observable through an Angular-aware binding?
4. Did an imperative update bypass a template-bound input?
5. Is list identity correct, or is tracking reusing the wrong row?
6. Is a callback executing outside the expected Angular scheduling boundary?

Adding `detectChanges()` at the leaf may make the symptom disappear while the first broken boundary remains. Tests should update inputs as a real parent would, trigger events through the DOM, and assert the rendered result after Angular stabilizes.

## Optimize After Measuring

OnPush can reduce unnecessary subtree checks in large component trees, but it is not automatically faster for every screen. Rendering a huge list, recalculating expensive template functions, or repeatedly creating large object graphs can dominate the cost.

Profile representative interactions. Keep template expressions cheap, use stable tracking keys for collections, and split components around state ownership rather than arbitrary markup size. Do not mutate shared inputs merely to avoid allocation unless measurements justify a more specialized design with an explicit notification contract.

The main benefit of OnPush is often predictability: a component updates because its input snapshot changed, its own event ran, or a reactive dependency notified it—not because some distant mutable object happened to be revisited.
