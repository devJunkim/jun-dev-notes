---
title: "Angular Route Resolvers: Data Loading, Errors, and UX Trade-Offs"
excerpt: "Use Angular route resolvers deliberately with typed route data, centralized navigation errors, cancellation-aware HTTP, and honest loading UX."
category: "Angular"

seo:
  focusKeyword: "Angular route resolvers"
  description: "Use Angular route resolvers for typed data loading with redirects, centralized error handling, cancellation, and practical UX trade-offs."
  socialTitle: "Angular Route Resolvers: Data and Error Handling"
  socialDescription: "Decide when navigation should wait for data and handle missing, failed, or superseded loads predictably."
---

# Angular Route Resolvers: Data Loading, Errors, and UX Trade-Offs

An Angular route resolver loads data before activating a route. That can prevent a detail page from rendering without its required model, but it also turns network latency and failure into navigation behavior.

Resolvers are therefore a user-experience decision, not just a convenient place to call `HttpClient`.

Angular's [route resolver guide](https://angular.dev/guide/routing/data-resolvers) documents resolver return types, route-data access, redirects, and navigation error handling.

> **Quick answer:** Resolve only data the route cannot meaningfully render without. Return a `RedirectCommand` for expected alternate destinations, centralize unexpected navigation failures, and keep optional or progressively rendered data in the component or a feature service.

## Decide Whether Navigation Must Wait

A route such as `/orders/42` may require the order's identity and permissions before it can render anything useful. Resolving that data avoids a component that briefly exists in an invalid state.

A dashboard is different. Blocking the entire route until five independent widgets load creates a blank wait and couples unrelated failures. Let the shell activate, then load panels progressively.

Use a resolver when:

- the route has no useful state without the result;
- a missing resource should redirect before activation;
- server rendering needs the data at the navigation boundary;
- the same route contract is shared by multiple components.

Prefer component-owned loading for optional panels, refreshable data, infinite lists, or interactions that should preserve the current screen while a request runs.

This ownership question parallels [Modern Angular Component Design with Signals, Inputs, and Outputs](https://dev.jun-kim.net/2026/09/15/modern-angular-component-design-with-signals-inputs-and-outputs/): state should live at the boundary that decides its lifecycle.

## Return Data or a Redirect

Functional resolvers can inject dependencies and return a value, Promise, or Observable. This example treats a missing order as an expected navigation outcome:

```typescript
import { inject } from '@angular/core';
import {
  RedirectCommand,
  ResolveFn,
  Router,
} from '@angular/router';
import { catchError, of } from 'rxjs';

export interface OrderDetails {
  readonly id: string;
  readonly status: 'open' | 'shipped' | 'cancelled';
  readonly total: number;
}

export const orderResolver: ResolveFn<OrderDetails> = route => {
  const orders = inject(OrdersApi);
  const router = inject(Router);
  const orderId = route.paramMap.get('orderId');

  if (!orderId) {
    return new RedirectCommand(router.parseUrl('/orders'));
  }

  return orders.find(orderId).pipe(
    catchError(error => {
      if (error.status === 404) {
        return of(new RedirectCommand(router.parseUrl('/orders/not-found')));
      }

      throw error;
    }),
  );
};
```

Returning a `RedirectCommand` keeps the redirect inside the router's navigation flow. Calling `router.navigate()` as a side effect and then returning placeholder data starts a second navigation while the first one is still being resolved.

The sample assumes `OrdersApi` translates HTTP responses consistently and that `error` has a known application error shape. Do not spread raw `HttpErrorResponse` checks throughout a large application if a data-access boundary can provide typed outcomes.

## Keep Route Configuration Typed

```typescript
import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'orders/:orderId',
    loadComponent: () =>
      import('./order-page.component').then(m => m.OrderPageComponent),
    resolve: { order: orderResolver },
  },
];
```

The component can read resolved data through the router's component-input binding feature or from `ActivatedRoute`. With input binding enabled during router configuration, the route-data key becomes the input name:

```typescript
import { CurrencyPipe } from '@angular/common';
import { Component, input } from '@angular/core';

@Component({
  standalone: true,
  selector: 'app-order-page',
  imports: [CurrencyPipe],
  template: `
    <h1>Order {{ order().id }}</h1>
    <p>Status: {{ order().status }}</p>
    <p>Total: {{ order().total | currency }}</p>
  `,
})
export class OrderPageComponent {
  readonly order = input.required<OrderDetails>();
}
```

Register the router with `withComponentInputBinding()` for this form. The route key and input type still need review together; TypeScript does not prove that every route activating the component supplies the correct runtime data.

Alternatively, convert `ActivatedRoute.data` to a signal and derive the value. That approach is useful when the component needs several route fields, but avoid copying them into writable state. [Signals vs RxJS Observables in Angular](https://dev.jun-kim.net/2026/09/13/signals-vs-rxjs-observables-in-angular-when-to-use-which/) covers that boundary.

## Centralize Unexpected Navigation Errors

Expected absence can redirect in the resolver. Authentication renewal, an unavailable API, or a programming error often needs consistent application-wide handling.

```typescript
import { inject } from '@angular/core';
import {
  provideRouter,
  Router,
  withComponentInputBinding,
  withNavigationErrorHandler,
} from '@angular/router';

bootstrapApplication(AppComponent, {
  providers: [
    provideRouter(
      routes,
      withComponentInputBinding(),
      withNavigationErrorHandler(error => {
        const router = inject(Router);
        console.error('Navigation failed', error);
        return router.parseUrl('/unavailable');
      }),
    ),
  ],
});
```

The handler is a policy boundary, not permission to expose sensitive error bodies or redirect every bug into the same screen. Log enough context to diagnose the failed navigation, then show a safe user-facing outcome.

If different failures require different recovery, translate them into application-specific error types before the global handler. Keep authentication redirects coordinated with guards so the same condition does not produce competing navigations.

## Understand Cancellation and Caching

When a navigation is superseded, Angular unsubscribes from resolver Observables. `HttpClient` requests normally respond to unsubscription by aborting the underlying request. That saves client work, but it does not prove the server stopped processing a request it already received.

Avoid `toPromise`-style conversions or custom Promise wrappers that discard cancellation. Return the HTTP Observable when its single-result lifecycle matches the resolver.

Resolvers run according to route reuse and `runGuardsAndResolvers` policy. They are not a general cache. If revisiting a route should reuse data, define cache keys, expiry, invalidation after mutations, and user isolation in a service. `shareReplay(1)` by itself does not answer those questions and can retain stale or sensitive data longer than intended.

## Show Navigation Progress Deliberately

Because the destination component is not active yet, its local spinner cannot describe resolver work. Use router navigation events for a global or shell-level progress indicator, and avoid flashing it for very short navigations.

Do not resolve large secondary datasets merely to eliminate every loading state. A fast route shell with honest progressive loading is often better than a blank screen waiting for perfect completeness.

Test at least four paths: resolved data, expected redirect, unexpected failure, and a navigation superseded by another navigation. Also confirm how parameter-only navigation behaves under the selected rerun policy.

Resolvers work best when they protect a real route invariant. Used as a universal data-fetching layer, they turn the router into a hidden application service and make latency harder for users to understand.
