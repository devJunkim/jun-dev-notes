---
title: "Modern Angular Component Design with Signals, Inputs, and Outputs"
excerpt: "Design Angular components around clear state ownership, signal inputs, computed values, meaningful outputs, and deliberate two-way bindings."
category: "Angular"

seo:
  focusKeyword: "Angular component design with signals"
  description: "Design modern Angular components with input, output, model, signal, and computed APIs while avoiding duplicated state and unnecessary effects."
  socialTitle: "Modern Angular Component Design with Signals"
  socialDescription: "Refactor duplicated component state into clear inputs, derived values, and parent-owned decisions using modern Angular APIs."
---

# Modern Angular Component Design with Signals, Inputs, and Outputs

A component becomes difficult to maintain when several fields describe the same fact. A selected ID, a selected object, a selected label, and a `hasSelection` flag all need to agree after every input change.

Signals help express those relationships, but they do not choose the owner of the state. That remains a component design decision.

> **Quick answer:** Give each piece of writable state a clear owner. Use inputs for values supplied by that owner, `computed()` for derived values, and outputs for user intent. Use `model()` when two-way value editing is the component's actual contract.

## Start with Responsibilities

A seat picker should display available seats and report the user's choice. Whether that choice can be reserved, whether payment is required, and what happens after a server rejection belong to the surrounding booking workflow.

This boundary keeps the picker useful in different screens without hiding application behavior inside a reusable control. It does not require splitting every component into a container and a presentation component. A small page that loads and renders one resource may be clearer as one component.

The examples use standalone components and modern Angular APIs that are stable from Angular 19 onward. `output()` is an event API, not a signal that stores a current value. The repository's existing [Angular component communication article](https://dev.jun-kim.net/2026/09/10/angular-components-inputs-outputs-and-component-communication/) covers the underlying parent-child contract and decorator equivalents.

## Recognize State That Can Disagree

This intentionally awkward version copies several values from its inputs:

```typescript
import { Component, Input, OnChanges } from '@angular/core';

export interface Seat {
  readonly id: string;
  readonly label: string;
  readonly available: boolean;
}

@Component({
  selector: 'app-duplicated-seat-summary',
  standalone: true,
  template: `<p>{{ selectedLabel }} — {{ availableCount }} available</p>`,
})
export class DuplicatedSeatSummaryComponent implements OnChanges {
  @Input() seats: readonly Seat[] = [];
  @Input() selectedId: string | null = null;

  selectedSeat: Seat | undefined;
  selectedLabel = 'Choose a seat';
  availableCount = 0;

  ngOnChanges(): void {
    this.selectedSeat = this.seats.find(seat => seat.id === this.selectedId);
    this.selectedLabel = this.selectedSeat?.label ?? 'Choose a seat';
    this.availableCount = this.seats.filter(seat => seat.available).length;
  }
}
```

This can work, but every new derived field adds another synchronization responsibility. A future selection method might update `selectedId` without updating the copied label. An in-place mutation of the input array might not trigger the input-change path the author expected.

The problem is the duplicated representation, not the existence of `ngOnChanges` or decorator inputs. Those APIs remain useful in existing code. Changing syntax alone would not fix the ownership issue.

## Derive Values from Read-Only Inputs

The refactored picker keeps the selected ID in its parent and derives everything it displays:

```typescript
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

export interface Seat {
  readonly id: string;
  readonly label: string;
  readonly available: boolean;
}

@Component({
  selector: 'app-seat-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>{{ selectedLabel() }} — {{ availableCount() }} available</p>
    <div aria-label="Seat selection">
      @for (seat of seats(); track seat.id) {
        <button
          type="button"
          [disabled]="disabled() || !seat.available"
          [attr.aria-pressed]="selectedId() === seat.id"
          (click)="choose(seat.id)">
          {{ seat.label }}
        </button>
      }
    </div>
  `,
})
export class SeatPickerComponent {
  readonly seats = input.required<readonly Seat[]>();
  readonly selectedId = input<string | null>(null);
  readonly disabled = input(false);
  readonly seatSelected = output<string>();

  readonly selectedSeat = computed(() =>
    this.seats().find(seat => seat.id === this.selectedId()));
  readonly selectedLabel = computed(() =>
    this.selectedSeat()?.label ?? 'Choose a seat');
  readonly availableCount = computed(() =>
    this.seats().filter(seat => seat.available).length);

  choose(id: string): void {
    if (!this.disabled() && this.seats().some(seat => seat.id === id && seat.available)) {
      this.seatSelected.emit(id);
    }
  }
}
```

`input.required()` makes the binding required for template consumers. It does not validate the business contents of the array, and the input is not available before Angular supplies it. Avoid reading required inputs eagerly in a constructor or ordinary field initializer. A `computed` derivation is lazy, so defining it does not immediately read the input.

Input signals are read-only from the component's perspective. The `readonly` array and properties also discourage mutation through TypeScript, but neither mechanism performs a deep runtime freeze.

`computed()` memoizes the derived result and invalidates it when a tracked dependency changes. There is no separate label setter to forget. For a very large seat map, repeated searches may justify an indexed representation, but optimize the access pattern only when its size and update frequency warrant it.

## Let the Parent Decide What an Event Means

Here is a parent using the picker from `seat-picker.component.ts`:

```typescript
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { Seat, SeatPickerComponent } from './seat-picker.component';

@Component({
  selector: 'app-booking',
  standalone: true,
  imports: [SeatPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-seat-picker
      [seats]="seats()"
      [selectedId]="selectedId()"
      (seatSelected)="selectSeat($event)" />
    <p>The selection is a preference, not a confirmed reservation.</p>
  `,
})
export class BookingComponent {
  readonly seats = signal<readonly Seat[]>([
    { id: 'A1', label: 'A1', available: true },
    { id: 'A2', label: 'A2', available: false },
  ]);
  readonly selectedId = signal<string | null>(null);

  selectSeat(id: string): void {
    if (this.seats().some(seat => seat.id === id && seat.available)) {
      this.selectedId.set(id);
    }
  }

  replaceSeats(next: readonly Seat[]): void {
    const selected = this.selectedId();
    this.seats.set(next);
    if (!next.some(seat => seat.id === selected && seat.available)) {
      this.selectedId.set(null);
    }
  }
}
```

The child reports a choice. The parent owns whether that choice remains valid as availability changes. A production booking operation must still validate and reserve the seat on the server; disabling a button is not concurrency control.

`replaceSeats` updates authoritative state in response to a meaningful application event. It is different from an effect that continuously copies a derived label into another signal. Routing refreshes through this method also makes the invalidation rule testable.

For a larger feature, move this transition into a feature-scoped service so all updates follow the same rule. Choose the service's lifetime deliberately: two independent booking widgets should not accidentally share one global selection. See [Angular Services and Dependency Injection](https://dev.jun-kim.net/2026/09/11/angular-services-and-dependency-injection-a-practical-guide/) for scope and ownership considerations.

## Use `model()` for Value-Editing Controls

A numeric stepper has a different contract: editing a value is its purpose. A model input can express that without a hand-written input/output pair.

```typescript
import { ChangeDetectionStrategy, Component, model, signal } from '@angular/core';

@Component({
  selector: 'app-ticket-count',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" [disabled]="count() <= 1" (click)="decrease()">Fewer</button>
    <span>{{ count() }}</span>
    <button type="button" (click)="count.update(increment)">More</button>
  `,
})
export class TicketCountComponent {
  readonly count = model(1);
  readonly increment = (value: number) => value + 1;

  decrease(): void {
    this.count.update(value => Math.max(1, value - 1));
  }
}

@Component({
  selector: 'app-ticket-form',
  standalone: true,
  imports: [TicketCountComponent],
  template: `<app-ticket-count [(count)]="ticketCount" />`,
})
export class TicketFormComponent {
  readonly ticketCount = signal(1);
}
```

Angular creates a corresponding `countChange` output for the model. The parent binds the writable signal itself in this two-way binding, rather than calling `ticketCount()`.

This minimal stepper assumes an integer input of at least one; it is not a complete validated form control. Validate incoming values and business limits at the appropriate form boundary. `model()` does not replace Angular Forms integration when a control needs that contract.

Prefer an explicit output for an action such as `reservationRequested`, where the parent may reject the request or perform an asynchronous operation. Automatically updating a two-way value would hide the difference between an attempted action and an accepted result.

Editable drafts are another legitimate reason to have local state. If a form must support Save and Cancel, its draft is intentionally distinct from the saved input. Define when a new input resets that draft and how unsaved changes are handled; do not call every copy of input data a design mistake.

## Effects and Observables Still Have Specific Jobs

Use `signal()` for local writable state and `computed()` for values derived from reactive state. Reach for `effect()` when a change must synchronize with a non-reactive API, such as an imperative chart adapter. Consider lifecycle cleanup, browser-only APIs, and repeated execution when designing that effect.

Do not use an effect merely to set `selectedLabel` from `selectedSeat`. It adds an extra mutable value and scheduling behavior to a relationship that a computed value expresses directly. Similarly, user-triggered saving usually belongs in an event handler, rather than an effect that saves whenever any dependency changes.

An Observable remains a good abstraction for a cancellable request sequence, debouncing, retries, or multiple asynchronous events. A component can expose the current result as a signal at its boundary. [Signals vs RxJS Observables in Angular](https://dev.jun-kim.net/2026/09/13/signals-vs-rxjs-observables-in-angular-when-to-use-which/) covers that lifecycle and interoperability in detail.

## Change Detection Does Not Remove Ownership Rules

Angular tracks signal reads in an `OnPush` template and schedules the component for checking when those dependencies change. The examples specify `OnPush` explicitly so the intended strategy is visible across projects.

Mutating an object nested inside a signal does not itself notify the signal. Replace values through the owning signal's `set` or `update` operation and provide a new reference when the value changed. Other change-detection triggers can sometimes make a mutation appear to work, which is a poor basis for a component contract.

Stable `track` keys help Angular preserve the identity of rendered list items. They do not repair duplicate IDs or stale application state; those remain data-contract concerns.

## Test the Public Behavior

Set signal inputs through Angular's supported input mechanism, such as `fixture.componentRef.setInput('seats', seats)`, then run change detection before checking the DOM. Do not try to call `.set()` on an input signal.

For this picker, verify that input changes update the label and count, unavailable seats do not emit, valid clicks emit the correct ID, and the parent clears a selection when refreshed availability invalidates it. Include a host-component test so bindings and event handling are exercised together.

For the stepper, verify the two-way binding and its lower-bound behavior. Compilation catches template type errors; it does not prove these interactions or accessibility behavior in a browser.

## A Small Decision Guide

| Need | Start with |
| --- | --- |
| Parent-supplied value | `input()` or `input.required()` |
| Component-owned state | `signal()` |
| Value derived from state | `computed()` |
| User intent for the parent to handle | `output()` |
| A control that edits a bound value | `model()` |
| Synchronization with an imperative API | A focused `effect()` |
| Shared workflow or asynchronous composition | A scoped service and, where useful, RxJS |

When reviewing a component, trace one user action and one incoming data update. If both paths have an obvious owner and derived values follow automatically, the design is usually easier to maintain than one with many manually synchronized fields.
