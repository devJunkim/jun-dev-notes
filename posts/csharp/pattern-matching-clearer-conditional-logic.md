---
title: "Pattern Matching in C#: Writing Clearer Conditional Logic"
excerpt: "Use C# type, property, relational, and list patterns to make branching code clearer without turning ordinary conditions into clever syntax."
category: "C#"

seo:
  focusKeyword: "pattern matching in C#"
  description: "Use C# pattern matching with type, property, relational, list, and switch patterns while keeping conditional logic readable and maintainable."
  socialTitle: "Pattern Matching in C#: Clearer Conditional Logic"
  socialDescription: "Learn where modern C# patterns clarify decisions, how ordering affects results, and when a straightforward if statement is still better."
---

# Pattern Matching in C#: Writing Clearer Conditional Logic

Conditional code becomes difficult to read when it repeatedly casts the same value, navigates nested properties, or separates related cases across a long chain of Boolean expressions. C# pattern matching can express those decisions around the shape of the input.

The goal is not to replace every `if`. A useful pattern makes a business case easier to recognize; an over-compressed one makes the reader decode syntax before understanding the rule.

> **Quick answer:** Use `is` for one focused test and a `switch` expression when several branches all produce one result. Prefer patterns that name meaningful cases, order specific cases before broad ones, and return to ordinary `if` statements when the decision depends on sequential work or unrelated conditions.

## Match a Type and Capture It Once

A declaration pattern tests the runtime type, rejects `null`, and introduces a correctly typed variable:

```csharp
public static decimal GetAmount(object adjustment) => adjustment switch
{
    PercentageDiscount percentage => percentage.Rate,
    FixedDiscount fixedAmount => fixedAmount.Amount,
    _ => throw new ArgumentException("Unknown adjustment type.", nameof(adjustment)),
};
```

This is clearer than testing with `is`, casting separately, and repeating the cast. It also keeps the supported alternatives together.

Do not use type patterns to compensate for a model that should own polymorphic behavior. If every operation switches over the same set of concrete classes, a virtual method or another explicit abstraction may be the better design. Pattern matching is helpful at boundaries where heterogeneous data is expected; it is not a substitute for assigning responsibility.

## Describe Object Shape with Property Patterns

Property patterns combine null checks, property access, and nested conditions. Suppose an order may qualify for expedited handling:

```csharp
public sealed record Customer(bool IsActive, string Tier);

public sealed record Order(
    decimal Total,
    string Status,
    Customer? Customer);

public static bool CanExpedite(Order? order) => order is
{
    Status: "Paid",
    Total: >= 100m,
    Customer: { IsActive: true, Tier: "Gold" },
};
```

The outer pattern fails safely when `order` is `null`; the nested customer pattern fails when `Customer` is `null`. The code describes one coherent shape instead of repeating `order.Customer` and several null guards.

Property patterns read best when property names carry the meaning. A deeply nested pattern spanning many lines can hide which failed condition matters. If different failures need distinct messages, use explicit validation branches so each outcome remains visible.

## Use Relational and Logical Patterns for Ranges

Relational patterns work well when branches classify one value:

```csharp
public static string ClassifyTemperature(decimal celsius) => celsius switch
{
    < -50m => "Outside supported range",
    < 0m => "Freezing",
    <= 30m => "Normal",
    <= 45m => "Hot",
    _ => "Outside supported range",
};
```

Arms are evaluated in order. After `< 0m` fails, `<= 30m` effectively describes the interval from zero through 30. That is concise, but it depends on the reader noticing the order.

Logical patterns can make an interval explicit:

```csharp
public static bool IsComfortable(decimal celsius) =>
    celsius is >= 18m and <= 24m;
```

Use parentheses when combining `and`, `or`, and `not` would otherwise make precedence hard to see. A named helper such as `IsComfortable` is often clearer than repeating a dense logical pattern at several call sites.

## Use a Switch Expression When Every Arm Produces a Value

A switch expression fits classification and mapping because every arm has the same purpose:

```csharp
public enum PaymentState
{
    Pending,
    Authorized,
    Captured,
    Failed,
}

public static string GetCustomerMessage(PaymentState state) => state switch
{
    PaymentState.Pending => "Payment is pending.",
    PaymentState.Authorized => "Payment is authorized.",
    PaymentState.Captured => "Payment is complete.",
    PaymentState.Failed => "Payment failed.",
    _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
};
```

The final arm handles enum values outside the named members, which can arrive through casts or deserialization. The compiler also detects many unreachable arms and warns about non-exhaustive switch expressions.

Use a switch statement instead when a branch performs several actions, needs local control flow, or benefits from comments between steps. Forcing a workflow into expressions and helper calls can obscure rather than simplify it.

## Match Sequence Shape with List Patterns

List patterns are useful when position and length are part of a small input grammar. This example recognizes command-line tokens without first indexing into an array:

```csharp
public static string ParseCommand(string[] arguments) => arguments switch
{
    ["status"] => "ShowStatus",
    ["deploy", var environment] => $"Deploy:{environment}",
    ["deploy", var environment, "--force"] => $"ForceDeploy:{environment}",
    ["help", ..] => "ShowHelp",
    [] => "ShowHelp",
    _ => "Invalid",
};
```

The slice pattern `..` matches zero or more elements. List patterns test compatible indexable sequence shapes; they do not enumerate an arbitrary `IEnumerable<T>`. That distinction matters when the source is lazy or remote. [LINQ Performance in C#](https://dev.jun-kim.net/2026/09/15/linq-performance-in-c-deferred-execution-multiple-enumeration-and-common-pitfalls/) covers why silently enumerating such a source would be a different operation.

Use list patterns for a bounded grammar, protocol prefix, or a few structural cases. A full parser with quoting, escaping, and detailed diagnostics deserves explicit parsing code.

## Guards Are an Escape Hatch, Not the Default

A `when` guard can express a condition that is not naturally part of a pattern:

```csharp
public static decimal ShippingCost(Order order) => order switch
{
    { Status: "Paid", Total: >= 100m } => 0m,
    { Status: "Paid" } paid when IsRemoteDestination(paid) => 25m,
    { Status: "Paid" } => 10m,
    _ => throw new InvalidOperationException("Only paid orders can ship."),
};
```

The first matching arm wins, so free shipping must appear before the broader paid-order cases. A guard may call code, which makes cost and side effects less obvious. Keep guard functions deterministic and inexpensive. If a guard performs I/O or several calculations, compute that information before the switch or use a conventional workflow.

## Know When `if` Is Clearer

Pattern matching is a poor fit when conditions are sequential rather than alternative shapes:

```csharp
public static async Task SubmitAsync(
    Order order,
    CancellationToken cancellationToken)
{
    if (order.Status != "Draft")
    {
        throw new InvalidOperationException("Only draft orders can be submitted.");
    }

    if (order.Total <= 0m)
    {
        throw new InvalidOperationException("An order must have a positive total.");
    }

    await SaveAsync(order, cancellationToken);
}
```

These guard clauses explain two different failures and then perform work. A switch expression returning exceptions would be shorter but less direct.

Prefer ordinary `if` logic when:

- there are only one or two simple Boolean conditions;
- the order of operations, not just cases, is important;
- branches need different diagnostics or side effects;
- pattern syntax would repeat domain knowledge without naming it;
- the reader must mentally reconstruct complicated ranges or negations.

## Review the Decision, Not the Syntax Count

A good refactoring gives each branch a recognizable meaning, handles `null` deliberately, and makes ordering safe. Add tests at boundaries such as the exact range endpoints, unexpected enum values, missing nested properties, and similar list shapes.

Modern patterns are most valuable when they turn “inspect, cast, and compare” into a small set of explicit cases. Stop compressing when the syntax becomes the most interesting part of the method.
