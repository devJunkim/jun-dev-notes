---
title: "Angular Components: Inputs, Outputs, and Component Communication"
excerpt: "Learn how Angular components communicate with inputs and outputs, how decorator and signal-based APIs differ, and when to use a shared service instead."
category: "Angular"

seo:
  focusKeyword: "Angular component communication"
  description: "A practical guide to Angular component communication with @Input, @Output, input and output APIs, event payloads, and shared services."
  socialTitle: "Angular Inputs, Outputs, and Component Communication"
  socialDescription: "Learn parent-child communication in Angular with practical input, output, signal-based, and shared-service examples."
---

# Angular Components: Inputs, Outputs, and Component Communication

Angular applications are trees of components. A product page contains a gallery, an order form, and stock indicators; a dashboard contains filters, cards, and charts. Those components need to exchange data without becoming tightly coupled.

For a direct parent-child relationship, Angular's core pattern is straightforward: the parent passes state down through an **input**, and the child reports an event up through an **output**. This one-way flow keeps ownership visible. When components are distant or state must outlive one child, a shared service or state-management approach is usually a better fit.

Angular supports both decorator-based `@Input()` and `@Output()` APIs and newer signal-based `input()` and `output()` APIs. The concepts are the same, but the TypeScript shapes differ. This article starts with the widely used decorator pattern, then shows the signal-based equivalent without assuming that every existing project should be rewritten.

> **Quick answer:** Inputs are data supplied to a component; outputs are events emitted by a component. Keep state in the closest sensible owner, pass values down, and emit meaningful events up.

## Angular Component Communication at a Glance

| Situation | Typical mechanism | Direction |
| --- | --- | --- |
| Parent supplies child data | `@Input()` or `input()` | Parent → child |
| Child reports an action | `@Output()` or `output()` | Child → parent |
| Parent calls a child API | Template reference or `viewChild` | Parent → child |
| Siblings share feature state | Common parent or scoped service | Both through owner/service |
| Distant features share application state | Service, signals, or state library | Shared |
| Components communicate through URL state | Angular Router | Application-wide navigation state |

Inputs and outputs are usually best when the relationship is direct and the data flow remains easy to trace in a template.

## Parent-to-Child Communication with `@Input()`

A component declares an input to let its consumer bind a value. The child should treat that input as state owned by the parent.

```typescript
import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

export interface ProductSummary {
  id: number;
  name: string;
  price: number;
  inStock: boolean;
}

@Component({
  selector: 'app-product-card',
  standalone: true,
  imports: [CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-card.component.html',
})
export class ProductCardComponent {
  @Input({ required: true }) product!: ProductSummary;
}
```

The parent binds a TypeScript expression with square brackets:

```html
<app-product-card [product]="selectedProduct"></app-product-card>
```

The child reads the value in its template:

```html
<article class="product-card">
  <h2>{{ product.name }}</h2>
  <p>{{ product.price | currency }}</p>
  <p>{{ product.inStock ? 'In stock' : 'Unavailable' }}</p>
</article>
```

Without brackets, Angular passes a string literal:

```html
<!-- Passes the string "selectedProduct", not the property value. -->
<app-product-card product="selectedProduct"></app-product-card>
```

Use property binding whenever the value comes from component state, is not a string, or should be evaluated as an expression.

### Required and optional decorator inputs

`@Input({ required: true })` tells Angular's template compiler that the consumer must bind the input. The definite-assignment assertion (`!`) tells TypeScript that Angular initializes the property, but it does not supply a runtime default.

Optional inputs should have an honest type and usually a useful default:

```typescript
@Input() showInventory = false;
@Input() heading: string | undefined;
```

Avoid adding `!` merely to silence TypeScript when the input is genuinely optional. Model `undefined` and handle it.

### Responding when an input changes

For simple rendering, Angular template binding is enough. When a component must react imperatively to multiple input changes, implement `OnChanges`:

```typescript
import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';

@Component({
  selector: 'app-price-history',
  standalone: true,
  template: `<p>Points loaded: {{ pointCount }}</p>`,
})
export class PriceHistoryComponent implements OnChanges {
  @Input() prices: readonly number[] = [];

  pointCount = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['prices']) {
      this.pointCount = this.prices.length;
    }
  }
}
```

Do not copy every input into duplicate local state. Derived values can often be calculated directly or represented with `computed` when using signal inputs.

## Child-to-Parent Communication with `@Output()`

An output exposes a custom event. With the decorator API, the child declares an `EventEmitter<T>` and emits a meaningful payload when something happens.

```typescript
import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface ProductSummary {
  id: number;
  name: string;
  price: number;
  inStock: boolean;
}

@Component({
  selector: 'app-product-card',
  standalone: true,
  template: `
    <h2>{{ product.name }}</h2>
    <button
      type="button"
      [disabled]="!product.inStock"
      (click)="requestAddToCart()">
      Add to cart
    </button>
  `,
})
export class ProductCardComponent {
  @Input({ required: true }) product!: ProductSummary;
  @Output() addRequested = new EventEmitter<number>();

  requestAddToCart(): void {
    this.addRequested.emit(this.product.id);
  }
}
```

The parent listens with parentheses and receives the payload through `$event`:

```html
<app-product-card
  [product]="selectedProduct"
  (addRequested)="addToCart($event)">
</app-product-card>
```

```typescript
addToCart(productId: number): void {
  this.cartService.add(productId);
}
```

Outputs do not bubble through the DOM like native browser events. Only a consumer listening to that component output receives it. If a grandparent needs the event, the parent must deliberately handle and re-emit it—or the design should use a more suitable state owner.

### Emit events, not commands to the parent

An output name should describe what happened from the child's perspective:

- Prefer `addRequested`, `quantityChanged`, or `dismissed`.
- Avoid `updateParent`, `callApi`, or `setDashboardState`.

The child should not need to know how its parent reacts. A reusable product card can emit `addRequested`; one parent might update a cart, while another might require sign-in first.

Avoid output names that collide with native DOM events such as `click` or `change`, and use camelCase. Angular output names are case-sensitive.

## A Complete Parent-Child Example

The parent owns products and cart state. Each child receives one product and reports user intent.

```typescript
import { Component } from '@angular/core';
import {
  ProductCardComponent,
  ProductSummary,
} from './product-card.component';

@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [ProductCardComponent],
  templateUrl: './catalog.component.html',
})
export class CatalogComponent {
  readonly products: readonly ProductSummary[] = [
    { id: 1, name: 'Mechanical Keyboard', price: 129, inStock: true },
    { id: 2, name: 'USB-C Dock', price: 89, inStock: false },
  ];

  readonly cartProductIds: number[] = [];

  addToCart(productId: number): void {
    this.cartProductIds.push(productId);
  }
}
```

```html
<section>
  @for (product of products; track product.id) {
    <app-product-card
      [product]="product"
      (addRequested)="addToCart($event)">
    </app-product-card>
  }
</section>

<p>Items in cart: {{ cartProductIds.length }}</p>
```

The flow stays explicit: `CatalogComponent` owns the state, bindings pass products down, and events request changes up.

## Signal-Based `input()` and `output()` APIs

Modern Angular also provides initializer functions for declaring inputs and outputs. These are alternatives to the decorator APIs, not different communication directions.

```typescript
import { Component, computed, input, output } from '@angular/core';

export interface ProductSummary {
  id: number;
  name: string;
  price: number;
  inStock: boolean;
}

@Component({
  selector: 'app-product-card',
  standalone: true,
  template: `
    <h2>{{ label() }}</h2>
    <button type="button" (click)="requestAddToCart()">
      Add to cart
    </button>
  `,
})
export class ProductCardComponent {
  readonly product = input.required<ProductSummary>();
  readonly addRequested = output<number>();

  readonly label = computed(() =>
    `${this.product().name} — $${this.product().price}`
  );

  requestAddToCart(): void {
    this.addRequested.emit(this.product().id);
  }
}
```

The parent template syntax remains familiar:

```html
<app-product-card
  [product]="selectedProduct"
  (addRequested)="addToCart($event)">
</app-product-card>
```

An `input()` returns a read-only `InputSignal`, so TypeScript code reads it by calling `this.product()`. An `output()` returns an output emitter with an `emit` method. Angular's official documentation covers both [input properties](https://angular.dev/guide/components/inputs) and [custom output events](https://angular.dev/guide/components/outputs).

The original `@Input()` and `@Output()` APIs remain supported. Follow the conventions of the project you are maintaining, and adopt signal APIs intentionally rather than mixing styles randomly within one feature.

## When Inputs and Outputs Stop Scaling

Inputs and outputs become awkward when state crosses many component levels. Passing a filter through components that do not use it, then re-emitting changes through the same chain, is often called prop drilling or event forwarding.

Start by asking who should own the state:

- If two siblings need it, their closest common parent may own it.
- If one feature subtree needs it, provide a service at that feature boundary.
- If URL navigation should reproduce it, use router parameters or query parameters.
- If many distant features coordinate complex state, consider a dedicated state pattern or library.

A small signal-based service can hold feature state:

```typescript
import { Injectable, computed, signal } from '@angular/core';

@Injectable()
export class CartState {
  private readonly productIds = signal<readonly number[]>([]);

  readonly items = this.productIds.asReadonly();
  readonly count = computed(() => this.productIds().length);

  add(productId: number): void {
    this.productIds.update(ids => [...ids, productId]);
  }
}
```

Provide it at the route or feature component when each feature instance needs isolated state:

```typescript
@Component({
  selector: 'app-checkout',
  standalone: true,
  providers: [CartState],
  templateUrl: './checkout.component.html',
})
export class CheckoutComponent {}
```

Using `providedIn: 'root'` would create application-wide state instead. Choose the provider scope to match the state lifetime.

Services are not automatically better than bindings. A global event bus can hide data flow more severely than a few explicit inputs and outputs. Prefer the smallest ownership boundary that keeps behavior understandable.

## Common Mistakes and Misconceptions

### Mutating input objects in the child

If a child changes an input object directly, ownership becomes unclear and change detection can become surprising. Prefer emitting an event and letting the owner replace or update its state.

### Using `EventEmitter` in services

`EventEmitter` is designed for component and directive outputs. Services should normally expose signals or RxJS observables and methods appropriate to their state model.

### Treating an output as a DOM event

Angular custom outputs do not bubble. A distant ancestor will not receive one unless intermediate components forward it.

### Forgetting the difference between attributes and property bindings

`product="selectedProduct"` is a string attribute value. `[product]="selectedProduct"` evaluates the parent expression.

### Copying an input into local state without synchronization

The copy can become stale when the parent sends a new value. Derive presentation from the input, react with `ngOnChanges`, or use computed state where appropriate.

### Chaining inputs and outputs across an entire application

Direct bindings are excellent for local composition but noisy across unrelated layers. Move ownership to a common parent, scoped service, router, or deliberate state solution.

## Interview-Oriented Questions

### What is the difference between an Angular input and output?

An input lets a consumer supply data to a component. An output lets the component emit a custom event that a consumer can handle.

### Are `@Input()` and `@Output()` obsolete?

No. Angular supports them. The `input()` and `output()` functions provide newer signal-oriented APIs, and teams can choose based on project conventions and migration goals.

### Do Angular outputs bubble?

No. They are custom component events consumed through Angular template binding, not bubbling DOM events.

### How should sibling components communicate?

Usually through their closest common parent when the state is local. A feature-scoped service is appropriate when the relationship or lifetime makes parent mediation cumbersome.

### Why avoid mutating an input?

It violates clear ownership, can produce unexpected coupling, and makes state transitions harder to trace. Emit an event and let the owner decide how state changes.

## Summary

Angular component communication is easiest to reason about when ownership and direction stay explicit:

- Inputs carry data from parent to child.
- Outputs carry events from child to parent.
- `@Input()` and `@Output()` remain supported decorator APIs.
- `input()` and `output()` offer modern signal-based alternatives.
- Shared services, router state, or dedicated state management fit broader communication needs.

Keep state in the nearest sensible owner, avoid mutating inputs, and emit events that describe what happened rather than commanding a particular parent implementation. That produces components that are easier to reuse, test, and change.
