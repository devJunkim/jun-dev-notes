---
title: "Angular Content Projection: Designing Stable Component Contracts"
excerpt: "Design Angular components with ng-content, multiple slots, fallback content, and content queries without hiding ownership or breaking accessibility."
category: "Angular"

seo:
  focusKeyword: "Angular content projection"
  description: "Use Angular ng-content, projection slots, fallback content, and content queries to build stable and accessible component APIs."
  socialTitle: "Angular Content Projection and Component Contracts"
  socialDescription: "Build flexible container components while keeping slot matching, ownership, styling, and accessibility predictable."
---

# Angular Content Projection: Designing Stable Component Contracts

Inputs work well for data, but accepting arbitrary markup as strings or configuration objects makes layout components rigid and can create unsafe HTML paths. Content projection lets the parent provide real Angular template content while the receiving component controls placement.

> **Quick answer:** Use `<ng-content>` for markup owned by the parent, keep slot selectors small and documented, include a catch-all or intentional fallback, and use content queries only when the container truly coordinates projected children. Projection changes placement, not ownership or authorization.

## Define a Small Slot Contract

```typescript
import { Component } from '@angular/core';

@Component({
  selector: 'app-panel',
  standalone: true,
  template: `
    <section class="panel" aria-labelledby="panel-title">
      <header>
        <h2 id="panel-title"><ng-content select="[panelTitle]">Details</ng-content></h2>
        <ng-content select="[panelActions]" />
      </header>
      <div class="panel-body"><ng-content /></div>
    </section>
  `,
})
export class PanelComponent {}
```

```html
<app-panel>
  <span panelTitle>Deployment status</span>
  <button panelActions type="button">Refresh</button>
  <p>The production rollout is healthy.</p>
</app-panel>
```

Angular's [content projection guide](https://angular.dev/guide/components/content-projection) explains that slot matching is compiled and that the unselected placeholder receives content not matched by another slot. Attribute selectors avoid requiring decorative wrapper components.

## Projection Does Not Move Ownership

Projected nodes remain part of the parent's view. They use the parent's injection context and change-detection ownership even though they render inside the child. The receiving component's `viewProviders` do not become providers for projected content.

Styles are also subject to encapsulation boundaries. Avoid relying on deep selectors to reach arbitrary projected markup. Prefer documented classes, CSS custom properties, or semantic wrapper elements.

## Do Not Conditionally Create `ng-content`

Angular processes `<ng-content>` at build time. Placing it inside `@if` does not provide lazy conditional projection; projected nodes may still be created even when the placeholder is hidden. Use template fragments or an explicit `TemplateRef` contract when content must be instantiated conditionally.

Fallback content belongs inside the placeholder and should remain safe and meaningful. If a slot is required for correctness, fail clearly during development or express the requirement with a coordinated directive rather than rendering an inaccessible shell.

## Query Only When Behavior Requires Coordination

Signal-based `contentChild` and `contentChildren` queries let a container find projected directives. Use them for behavior such as roving focus, selection, or registration—not merely to style arbitrary descendants.

A component that manages keyboard navigation also owns ARIA roles, focus order, disabled states, and dynamic child changes. Projection can break third-party components that assume direct ownership of their children, so follow the library's supported composition model.

## Protect Accessibility and Security

Projection of ordinary template nodes does not require `innerHTML`, so keep it that way. Never convert untrusted strings into trusted HTML merely to fit a slot API.

Test missing and extra slots, DOM order, accessible names, keyboard navigation, responsive layout, and projected interactive controls. A stable projection contract should give parents flexibility without making the component's semantics accidental.
