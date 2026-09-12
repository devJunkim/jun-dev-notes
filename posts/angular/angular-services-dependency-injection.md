---
title: "Angular Services and Dependency Injection: A Practical Guide"
excerpt: "Learn how Angular services and dependency injection organize shared logic, HTTP access, state, provider scope, and testable component dependencies."
category: "Angular"

seo:
  focusKeyword: "Angular services and dependency injection"
  description: "A practical guide to Angular services, @Injectable, providedIn root, constructor injection, inject(), provider scopes, HTTP, state, and testing."
  socialTitle: "Angular Services and Dependency Injection"
  socialDescription: "Understand Angular service design, injector hierarchy, root and component providers, shared state, HTTP access, and testing."
---

# Angular Services and Dependency Injection: A Practical Guide

Components are responsible for presenting a view and handling user interaction. When a component also fetches data, caches state, calculates business rules, writes logs, and coordinates unrelated features, it becomes difficult to understand and test.

Angular services give that non-visual behavior a focused home. Angular's dependency-injection (DI) system creates and supplies those services where they are needed. The important design decisions are not just how to write `@Injectable()`, but what belongs in a service and where that service should be provided.

> **Quick answer:** Put reusable, non-visual capabilities and shared feature state in focused services. Provide them at the narrowest lifetime that matches their ownership, then inject them through a constructor or the `inject()` function.

## Angular Services and Dependency Injection at a Glance

| Concept | Purpose |
| --- | --- |
| Service | Class or value that provides a focused capability or state |
| `@Injectable()` | Supplies DI metadata and can configure automatic provision |
| `providedIn: 'root'` | Makes one root-provided instance available application-wide |
| Constructor injection | Declares dependencies as constructor parameters |
| `inject()` | Resolves a dependency within an Angular injection context |
| Component `providers` | Creates a provider scoped to that component subtree |
| Injector hierarchy | Controls which provider instance a consumer receives |

Services are ordinary TypeScript classes participating in Angular DI. They are not automatically APIs, stores, or singletons; their behavior depends on their code and provider registration.

## What Belongs in an Angular Service?

A service is appropriate for behavior that is not primarily view rendering and benefits from reuse, shared lifetime, substitution, or centralized configuration.

Common examples include:

- HTTP and data-access gateways
- Authentication and authorization state
- Feature-level state and coordination
- Logging and analytics adapters
- Browser-storage access
- Formatting or calculation with injected dependencies

```typescript
import { Injectable } from '@angular/core';

export interface PriceLine {
  quantity: number;
  unitPrice: number;
}

@Injectable({ providedIn: 'root' })
export class PricingService {
  calculateSubtotal(lines: readonly PriceLine[]): number {
    return lines.reduce(
      (total, line) => total + line.quantity * line.unitPrice,
      0,
    );
  }
}
```

This calculation could also be a pure function. DI is useful only if the service needs dependencies, substitution, or shared identity. Do not turn every helper function into an injectable class.

## Understanding `@Injectable()`

`@Injectable()` marks a class for Angular's injection system and can declare how it is provided.

```typescript
import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AuditService {
  record(eventName: string): void {
    console.info(`[audit] ${eventName}`);
  }
}
```

The decorator is especially important when the service has injected constructor parameters. The `providedIn` option creates a tree-shakable provider without requiring a separate providers-array entry.

### What `providedIn: 'root'` means

A root-provided service is available through the application's root environment injector. Consumers resolving that token from the same root generally share one instance.

```typescript
@Injectable({ providedIn: 'root' })
export class SessionService {
  private currentUserId: string | null = null;

  signIn(userId: string): void {
    this.currentUserId = userId;
  }

  signOut(): void {
    this.currentUserId = null;
  }
}
```

It is common to call this a singleton service, but “singleton-like within that injector” is more precise. Providing `SessionService` again in a child injector creates another instance for that subtree. Multiple bootstrapped Angular applications also have separate root environments.

Root scope is a good fit for truly application-wide state or stateless capabilities. It is not the default answer for state that should reset when a route or component is destroyed.

## Constructor Injection

Constructor injection is an established, explicit way to declare dependencies.

```typescript
import { Component } from '@angular/core';
import { PricingService, PriceLine } from './pricing.service';

@Component({
  selector: 'app-order-summary',
  standalone: true,
  template: `<p>Subtotal: {{ subtotal }}</p>`,
})
export class OrderSummaryComponent {
  readonly subtotal: number;

  constructor(pricing: PricingService) {
    const lines: readonly PriceLine[] = [
      { quantity: 2, unitPrice: 25 },
      { quantity: 1, unitPrice: 10 },
    ];

    this.subtotal = pricing.calculateSubtotal(lines);
  }
}
```

Angular resolves `PricingService` when it creates the component. Constructor injection works well when dependencies should be visible in the class signature and when conventional class construction matters to the team.

TypeScript's `private` parameter-property shorthand is also common:

```typescript
constructor(private readonly audit: AuditService) {}
```

The parameter becomes a private field. Avoid doing substantial work in the constructor; use it to establish dependencies and initial state.

## Using the `inject()` Function

The `inject()` function resolves a token from the active injection context. Field initializers in Angular-created components and services are a common valid location.

```typescript
import { Component, inject } from '@angular/core';
import { AuditService } from './audit.service';

@Component({
  selector: 'app-save-button',
  standalone: true,
  template: `<button type="button" (click)="save()">Save</button>`,
})
export class SaveButtonComponent {
  private readonly audit = inject(AuditService);

  save(): void {
    this.audit.record('save-requested');
  }
}
```

`inject()` is also useful in functional route guards, provider factories, and inheritance scenarios. It cannot be called from an arbitrary callback after construction unless that callback runs in an injection context.

```typescript
// Incorrect: an ordinary click handler is not a new injection context.
save(): void {
  // const audit = inject(AuditService); // Runtime injection-context error
}
```

Constructor injection and `inject()` are both supported patterns. Choose a consistent style based on readability and the specific API context rather than treating one as universally superior.

Angular's official [dependency-injection guide](https://angular.dev/guide/di) documents injection contexts and current provider APIs.

## Provider Scope and the Injector Hierarchy

Angular resolves dependencies through hierarchical injectors. It starts near the requesting component and searches upward until it finds a provider. A closer provider for the same token shadows one higher in the hierarchy.

The provider location determines availability and instance lifetime:

| Provider location | Typical visibility and lifetime |
| --- | --- |
| `providedIn: 'root'` | Shared through the application root |
| Application configuration | Shared from that environment injector |
| Route providers | Shared within the route environment |
| Component providers | New instance for each component instance and its descendants |

### Component-level providers

Provide a service at a component when each component subtree needs isolated state.

```typescript
import { Component, Injectable, signal } from '@angular/core';

@Injectable()
export class DraftEditorState {
  readonly title = signal('');
  readonly isDirty = signal(false);

  updateTitle(title: string): void {
    this.title.set(title);
    this.isDirty.set(true);
  }
}

@Component({
  selector: 'app-draft-editor',
  standalone: true,
  providers: [DraftEditorState],
  template: `...`,
})
export class DraftEditorComponent {}
```

Every `DraftEditorComponent` gets its own `DraftEditorState`. Descendants resolve that same local instance unless they provide another. When the component subtree is destroyed, Angular destroys the scoped service and invokes applicable cleanup hooks.

Providing the same root service again at component level is a common source of “why do I have two stores?” bugs. The hierarchy is working as configured; the local provider intentionally creates a different instance.

### Route-level providers

Standalone route configuration can scope services to a route subtree:

```typescript
import { Routes } from '@angular/router';
import { CheckoutState } from './checkout-state.service';

export const routes: Routes = [
  {
    path: 'checkout',
    providers: [CheckoutState],
    loadComponent: () =>
      import('./checkout.component').then(module => module.CheckoutComponent),
  },
];
```

This is useful when state should be shared across checkout children but should not become global application state.

## HTTP and Data-Access Services

A focused data-access service can hide URLs and transport details from components while returning typed observables.

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface Product {
  id: number;
  name: string;
  price: number;
}

export interface CreateProductRequest {
  name: string;
  price: number;
}

@Injectable({ providedIn: 'root' })
export class ProductsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/products';

  getAll(): Observable<readonly Product[]> {
    return this.http.get<readonly Product[]>(this.baseUrl);
  }

  create(request: CreateProductRequest): Observable<Product> {
    return this.http.post<Product>(this.baseUrl, request);
  }
}
```

Configure `HttpClient` during application bootstrap:

```typescript
import { ApplicationConfig } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [provideHttpClient()],
};
```

The service does not subscribe internally just to return data. Returning the observable lets the caller compose cancellation, loading, error, and presentation behavior. A service may coordinate more when it genuinely owns caching or feature state.

## Sharing State Through a Service

Signals provide a compact way for a service to own synchronous feature state.

```typescript
import { Injectable, computed, signal } from '@angular/core';

export interface CartItem {
  productId: number;
  name: string;
  price: number;
}

@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly itemsState = signal<readonly CartItem[]>([]);

  readonly items = this.itemsState.asReadonly();
  readonly count = computed(() => this.itemsState().length);
  readonly total = computed(() =>
    this.itemsState().reduce((sum, item) => sum + item.price, 0)
  );

  add(item: CartItem): void {
    this.itemsState.update(items => [...items, item]);
  }

  remove(productId: number): void {
    this.itemsState.update(items =>
      items.filter(item => item.productId !== productId)
    );
  }
}
```

Private writable state plus public read-only signals prevents consumers from changing the array without using the store's operations.

Root scope makes this cart global to the application. If separate route instances need separate carts, provide the service at the route or feature component instead. State ownership should determine provider scope.

RxJS remains appropriate for asynchronous event streams, cancellation, multicasting, and existing observable-heavy code. Signals and observables solve overlapping but not identical problems.

## Replacing Dependencies and Testing Services

Angular providers map a token to a value, class, alias, or factory. Tests can replace a dependency without changing production code.

```typescript
import { TestBed } from '@angular/core/testing';

class FakeAuditService {
  readonly events: string[] = [];

  record(eventName: string): void {
    this.events.push(eventName);
  }
}

TestBed.configureTestingModule({
  providers: [
    { provide: AuditService, useClass: FakeAuditService },
  ],
});

const audit = TestBed.inject(AuditService) as FakeAuditService;
audit.record('test-event');

expect(audit.events).toEqual(['test-event']);
```

For a service with no Angular-specific dependencies, constructing it directly can be simpler than configuring `TestBed`. Use Angular's testing utilities when injector behavior or Angular-provided dependencies are part of the test.

HTTP services can use Angular's HTTP testing provider and controller so tests inspect requests without reaching a live server.

## Avoiding God Services

A service can become as overloaded as a component. `ApplicationService` classes that handle authentication, carts, notifications, settings, analytics, and HTTP requests create global coupling.

Prefer capabilities with clear reasons to change:

- `ProductsApi` owns product transport.
- `CartStore` owns cart state transitions.
- `SessionService` owns session state.
- `AuditService` reports audit events.

Do not split services so finely that every class forwards one method to another. Cohesion, not file count, is the goal.

Watch for root services that retain component references or subscriptions forever. Use Angular cleanup facilities, finite streams, or a narrower provider scope to align resource lifetime with ownership.

## Common Mistakes and Misconceptions

### Assuming every service is automatically a singleton

Instances belong to providers and injectors. `providedIn: 'root'` normally yields one shared root instance, while component and route providers can create other instances.

### Providing a state service in every consuming component

Each provider creates a local instance, so components expecting shared state stop seeing one another's updates. Provide it at their closest shared owner.

### Calling `inject()` anywhere

`inject()` requires an injection context. Field initialization and provider factories are valid; arbitrary later method execution is not automatically valid.

### Subscribing inside every HTTP service method

Internal subscriptions can hide errors, cancellation, and lifecycle. Return an observable unless the service truly owns the side effect and subscription lifetime.

### Moving all logic out of components

View-specific formatting and interaction belong near the view. Services are for reusable capabilities, state ownership, and non-visual boundaries—not a place to hide every line of TypeScript.

### Using a global service for temporary feature state

Root services can retain state across navigation. A route or component provider may better match the desired reset behavior.

## When a Service Is Appropriate

Use a service when:

- Multiple consumers need one capability or state owner.
- Logic coordinates HTTP, storage, logging, or another external boundary.
- A dependency needs centralized configuration or replacement.
- State should outlive one component or be shared within a defined subtree.

Prefer a component method or pure function when:

- The behavior exists only for one view.
- No shared lifetime or injected dependency is needed.
- The transformation is deterministic and easy to call directly.
- Introducing a service would add indirection without a meaningful boundary.

## Interview-Oriented Questions

### What does `providedIn: 'root'` do?

It registers a tree-shakable provider in the root environment injector, making the service broadly available and normally shared within that application root.

### What is the difference between constructor injection and `inject()`?

Constructor injection receives dependencies as parameters. `inject()` resolves them from the current Angular injection context. Both use the same DI system.

### What happens when a component provides a root-provided service again?

The component injector creates a local instance for that component and its descendants, shadowing the root instance in that subtree.

### Why put HTTP calls in a service?

It centralizes transport details and typed contracts, keeps components focused on presentation, and provides a clear place for replacement and HTTP-focused tests.

### Is all shared state supposed to be root-scoped?

No. Application-wide state can use root scope, while route or component providers often better model feature-local state and cleanup.

## Summary

Angular services give focused non-visual behavior and state a clear owner. Dependency injection connects those capabilities to components and other services without requiring consumers to construct them directly.

Use `@Injectable({ providedIn: 'root' })` for appropriate application-wide services, component or route providers for isolated state, and either constructor injection or `inject()` where its context is valid. Keep services cohesive, choose scope deliberately, and do not add a service when a component method or pure function is clearer.
