---
title: "Angular Built-In Control Flow: @if, @for, and Stable Tracking"
excerpt: "Use Angular built-in template control flow with deliberate list identity, empty states, aliases, and tests that catch incorrect DOM reuse."
category: "Angular"

seo:
  focusKeyword: "Angular built-in control flow"
  description: "Use Angular @if, @for, @switch, and @empty blocks with stable tracking keys, readable templates, and predictable DOM reuse."
  socialTitle: "Angular Control Flow and Stable List Identity"
  socialDescription: "Write readable Angular templates without introducing stale rows, lost focus, or unnecessary DOM replacement."
---

# Angular Built-In Control Flow: @if, @for, and Stable Tracking

Angular's built-in control-flow blocks remove directive imports and make conditional templates easier to scan. The syntax is small; the behavior still depends on data identity, state ownership, and what should happen when collections reorder or become empty.

> **Quick answer:** Use `@if` and `@switch` for mutually exclusive UI states, and use `@for` with a stable key that represents the same business item across updates. Reserve `$index` for truly static lists, provide an `@empty` state, and test reordering with interactive child content—not only rendered text.

## Keep Conditions Close to Their UI State

`@if` supports an alias for a value used inside the branch:

```html
@if (account(); as currentAccount) {
  <h2>{{ currentAccount.displayName }}</h2>
  <p>{{ currentAccount.planName }}</p>
} @else {
  <p>No account is available.</p>
}
```

Use aliases to avoid repeating a longer expression, not to hide several transformations in the template. Compute derived business state in the component, then let the template select the presentation.

For three or more exclusive states, `@switch` can communicate intent better than nested conditions:

```html
@switch (requestState()) {
  @case ('loading') { <app-spinner /> }
  @case ('ready') { <app-results /> }
  @case ('failed') { <app-error-summary /> }
  @default { <p>Request has not started.</p> }
}
```

The default branch is useful even when a TypeScript union appears exhaustive. Runtime data can be stale, corrupted, or received from an older server.

## Track Business Identity, Not Position

Angular requires a `track` expression in `@for` blocks. The [control-flow guide](https://angular.dev/guide/templates/control-flow) explains that tracking lets Angular associate data items with existing DOM nodes.

```typescript
interface Deployment {
  id: string;
  environment: string;
  status: 'queued' | 'running' | 'complete';
}
```

```html
<ul>
  @for (deployment of deployments(); track deployment.id) {
    <li>
      <strong>{{ deployment.environment }}</strong>
      <span>{{ deployment.status }}</span>
    </li>
  } @empty {
    <li>No deployments match the current filters.</li>
  }
</ul>
```

An ID should remain stable for the logical item and be unique among siblings. Tracking by `$index` is safe only when the list never inserts, removes, sorts, or filters. Otherwise a row can be reused for a different item, which is especially confusing when it contains an input, focus, animation state, or child component state.

Tracking the object reference can work when references remain stable, but immutable updates often replace objects. A durable business key is usually clearer.

## Do Not Mutate the Tracking Key

Changing an item's tracked ID in place changes its identity contract. Angular's current control-flow implementation prioritizes view reuse in some in-place mutation scenarios, which differs from assumptions developers may carry from `*ngFor` and `trackBy`.

Treat identifiers as immutable. If an optimistic client-generated ID must later become a server ID, decide whether that represents the same UI item and test focus, pending edits, and child state during reconciliation. A separate stable client key can avoid accidental remounts.

Duplicate keys are a correctness problem. Validate API mapping and merge logic so two rows cannot claim the same identity.

## Use Context Variables for Presentation

`@for` exposes values such as `$index`, `$count`, `$first`, `$last`, `$even`, and `$odd`. Alias them when nesting makes ownership unclear:

```html
@for (group of groups(); track group.id; let groupIndex = $index) {
  <section>
    <h2>Group {{ groupIndex + 1 }}: {{ group.name }}</h2>

    @for (member of group.members; track member.id; let memberIndex = $index) {
      <p>{{ memberIndex + 1 }}. {{ member.name }}</p>
    }
  </section>
}
```

Use the index for labels or visual position, not durable identity. If users can reorder items, accessible names should update while focus remains with the logical item.

## Model Loading, Empty, and Error Separately

`@empty` runs when the iterable contains no items. It does not distinguish an empty result from a request that has not completed or failed.

Keep those states explicit:

```html
@if (loading()) {
  <app-spinner />
} @else if (error()) {
  <app-error-summary [error]="error()!" />
} @else {
  @for (item of items(); track item.id) {
    <app-result-row [item]="item" />
  } @empty {
    <p>No results found.</p>
  }
}
```

This prevents a transient empty array from flashing a misleading “no results” message during loading.

## Test Identity, Not Only Text

Component tests should insert, remove, reorder, and replace items. Put an input or stateful child in each row, interact with one row, reorder the data, and verify that the state follows the same ID.

Also test empty, error, and default switch branches; keyboard focus after changes; and accessible announcements when content appears. A template that renders the right strings once can still reuse the wrong DOM nodes later.

Built-in control flow makes intent more visible. Stable identity and explicit async states are what make that intent remain correct as the UI changes.
