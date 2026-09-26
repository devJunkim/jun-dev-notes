---
title: "Angular Deferrable Views: Loading Triggers, UX States, and Performance Trade-Offs"
excerpt: "Use Angular @defer blocks with intentional triggers, stable placeholders, prefetching, error states, and measured bundle improvements."
category: "Angular"

seo:
  focusKeyword: "Angular deferrable views"
  description: "Design Angular deferrable views with @defer triggers, placeholders, loading and error states, prefetching, SSR behavior, and performance measurement."
  socialTitle: "Angular Deferrable Views Without Loading Surprises"
  socialDescription: "Reduce initial JavaScript while preserving layout stability, accessible feedback, predictable triggers, and recoverable failures."
---

# Angular Deferrable Views: Loading Triggers, UX States, and Performance Trade-Offs

A dashboard defers every card to improve its initial bundle. The first screen becomes a wall of shifting placeholders, nested chunks request each other, and users wait longer for the content they came to see.

Deferrable views are a loading boundary, not a blanket performance annotation. The trigger and fallback states become part of the page's user experience.

> **Quick answer:** Defer code that is not required for the initial task, choose a trigger that matches user intent, and reserve stable space with an accessible placeholder. Prefetch only when it is likely to help, provide loading and error states, and verify the actual chunk and Core Web Vitals changes in a production build.

## Defer a Cohesive Optional Feature

Angular can split eligible dependencies referenced only inside an `@defer` block into separate chunks. A useful boundary is a heavy chart, editor, map, or secondary recommendation panel—not the heading and primary action above the fold.

```typescript
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { SalesChartComponent } from './sales-chart.component';

@Component({
  selector: 'app-dashboard',
  imports: [SalesChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Sales dashboard</h1>

    @defer (on viewport; prefetch on idle) {
      <app-sales-chart />
    } @placeholder (minimum 300ms) {
      <div class="chart-placeholder" aria-hidden="true"></div>
    } @loading (after 150ms; minimum 300ms) {
      <p role="status">Loading sales chart…</p>
    } @error {
      <p role="alert">The chart could not be loaded.</p>
    }
  `,
})
export class DashboardComponent {}
```

Angular's [`@defer` guide](https://angular.dev/guide/templates/defer) documents triggers, prefetching, and the placeholder, loading, and error sub-blocks. Only dependencies that meet Angular's deferral requirements are split; inspect the production build instead of assuming an import moved.

The timing values are illustrative. Minimum display times can prevent flicker, but they can also hold a loading state after content is ready.

## Match the Trigger to User Intent

`on viewport` fits content lower on a page. `on interaction` fits a feature the user explicitly opens. `on hover` can prefetch a likely interaction but performs poorly for touch-only use if it is the sole plan. `on idle` is suitable for low-priority work only when competing downloads and device constraints are acceptable.

A `when` expression gives application control but can load repeatedly changing conditions earlier than expected. Once a deferred block is loaded, it does not become unloaded when the condition becomes false.

Do not use a timer to hide an architectural race. If data or permission must be ready first, model that state explicitly. Deferring a component does not automatically defer an API request started by a parent service.

## Separate Downloading Code From Loading Data

The JavaScript chunk and the feature's data have different lifecycles. Decide whether data retrieval starts before, with, or after the code load.

Prefetching code on idle can make an interaction feel immediate while avoiding a premature API call. For expensive or sensitive data, load only after authorization and a real user trigger. Cancel data requests when the component is destroyed and avoid duplicate loads across nested views.

An error block handles a failed deferred dependency load. Application data errors still belong to the feature's own state model. Provide retry behavior where it is safe, but avoid automatic reload loops when a chunk URL is invalid after a deployment.

## Preserve Layout and Accessibility

Give the placeholder approximately the same dimensions as the eventual content to reduce cumulative layout shift. Avoid a spinner with no label when users need to know what is happening.

Use `role="status"` for non-urgent progress and `role="alert"` sparingly for failures needing immediate announcement. Keep focus stable. If interaction triggers loading, ensure the initiating control remains meaningful and the loaded component receives focus only when that follows expected keyboard behavior.

Placeholders are eagerly loaded. A placeholder that imports another heavy component can erase the bundle benefit. Prefer lightweight HTML and CSS.

## Understand SSR and Hydration

Without incremental hydration configuration, server rendering generally emits the placeholder for a defer block and activates triggers on the client. That can be correct for below-the-fold content and poor for search-critical or immediately visible content.

Incremental hydration adds separate `hydrate` triggers and changes what the server renders and the client hydrates. Treat it as an application-level SSR decision, not a syntax swap copied into one component. Test event replay, network throttling, JavaScript-disabled output, and crawler-visible content for the deployed Angular version.

Avoid nesting defer blocks with identical immediate triggers. That can create cascading requests and fragmented loading states. Flatten the boundary or use different intent-driven triggers.

## Measure the Whole Navigation

Use a production build and inspect initial chunks, deferred chunks, duplicate dependencies, and source maps. Measure cold-cache and warm-cache navigation on representative mobile hardware and networks.

Track Largest Contentful Paint, Interaction to Next Paint, cumulative layout shift, transferred JavaScript, parsing time, and the delay from user intent to usable deferred content. A smaller initial bundle can still produce a slower task if the user immediately needs the deferred feature.

Test viewport behavior, keyboard interaction, offline or failed chunk loads, slow data, navigation away during load, and a deployment where old HTML references a removed chunk. Configure asset caching so already-served application shells can still retrieve compatible chunks during rollout.

Deferrable views work best when they follow product priority: ship what the user needs now, prepare what they are likely to need next, and delay everything else with an honest, stable loading experience.
