---
title: "C# ref, out, and in Parameters: What's the Difference?"
excerpt: "Learn how ref, out, and in parameters work in C#, when to use each one, and how they affect argument passing and method behavior."
category: "C#"

seo:
  focusKeyword: "C# ref out in parameters"
  description: "Learn the differences between ref, out, and in parameters in C#, how each one behaves, and when to use them in real-world .NET code."
  socialTitle: "C# ref, out, and in Parameters: What's the Difference?"
  socialDescription: "Learn how ref, out, and in parameters differ in C# and when each one is appropriate in real-world .NET development."
---

# C# ref, out, and in Parameters: What's the Difference?

Most C# method calls pass arguments **by value**. That statement is easy to repeat but easy to misunderstand, especially when the argument is an object. The modifiers `ref`, `out`, and `in` change the call so the method receives an alias to the caller's storage instead of an independent parameter value.

The three modifiers do not grant the same capabilities. A `ref` parameter can be read and replaced, an `out` parameter must be assigned by the called method, and an `in` parameter is exposed as read-only. Choosing the right one requires understanding both argument passing and the difference between value types and reference types.

> **Quick answer:** Use `ref` for intentional two-way access to an existing variable, `out` for an additional value a method must produce, and `in` for read-only by-reference access—primarily when avoiding a copy of a large value type is measurably useful.

## What Passing by Reference Means in C#

A parameter is a variable scoped to a method invocation. With ordinary value passing, C# initializes that parameter by copying the argument's value. Reassigning the parameter changes only the method's local copy.

```csharp
static void ReplaceNumber(int number)
{
    number = 99;
}

int original = 10;
ReplaceNumber(original);

Console.WriteLine(original); // 10
```

The same rule applies when the argument has a reference type. The value being copied is a reference to an object. The method can use its copied reference to mutate that object, but replacing the parameter does not replace the caller's variable.

```csharp
static void UpdateAndReplace(Customer customer)
{
    customer.Name = "Updated";       // Mutates the shared object
    customer = new Customer("Other"); // Reassigns only the local parameter
}

var customer = new Customer("Original");
UpdateAndReplace(customer);

Console.WriteLine(customer.Name); // Updated

public sealed class Customer(string name)
{
    public string Name { get; set; } = name;
}
```

This is the crucial distinction:

- A **reference type** describes what a variable's value represents: a reference to an object.
- Passing **by reference** describes how a method receives an argument: as an alias to the caller's variable.

Reference-type arguments are therefore **not automatically passed by reference**. By default, their references are passed by value.

When a parameter uses `ref`, `out`, or `in`, the callee operates through a managed reference to the caller's storage. The modifier controls what the callee may do through that reference.

## `ref` Parameters

A `ref` parameter provides read-and-write access to an existing variable. The caller must definitely assign the variable before the call because the method is allowed to read its current value. The method may read it, mutate a referenced object, or assign a completely new value back into the caller's variable.

Both the method declaration and the call normally include `ref`, making the possibility of reassignment visible at the call site.

```csharp
static void ApplyDiscount(ref decimal price, decimal percentage)
{
    if (percentage is < 0 or > 100)
    {
        throw new ArgumentOutOfRangeException(nameof(percentage));
    }

    price -= price * percentage / 100m;
}

decimal subtotal = 120m;
ApplyDiscount(ref subtotal, 15m);

Console.WriteLine(subtotal); // 102.00
```

Here, `price` aliases `subtotal`. Assigning `price` changes the storage represented by `subtotal`.

A classic example is swapping two variables:

```csharp
static void Swap<T>(ref T left, ref T right)
{
    (left, right) = (right, left);
}

string first = "north";
string second = "south";

Swap(ref first, ref second);

Console.WriteLine(first);  // south
Console.WriteLine(second); // north
```

Because `string` is a reference type, this example also demonstrates something subtle: `ref` is not needed to access the string objects. It is needed because `Swap` must replace the values held by the caller's variables.

### Caller and callee requirements for `ref`

- The caller must assign the variable before passing it.
- The caller uses the `ref` modifier at the call site.
- The callee may read the incoming value.
- The callee may assign a new value, but it is not required to do so.
- An explicit `ref` argument must be an assignable variable of the parameter's exact type. Normal implicit conversions do not apply. A typical property or computed expression cannot be passed with `ref` because it is not a variable that the method can alias.

Use `ref` when changing the caller's variable is a deliberate part of the API contract. Do not use it merely to mutate an object; ordinary value passing already lets a method mutate an object through a copied reference.

## `out` Parameters

An `out` parameter is also passed by reference, but it represents an output the method promises to produce. The caller does not need to initialize the variable first. The callee cannot read the parameter before assigning it and must definitely assign it on every normal return path.

```csharp
static bool TryFindOrder(
    IReadOnlyDictionary<int, Order> orders,
    int orderId,
    out Order? order)
{
    if (orders.TryGetValue(orderId, out var found))
    {
        order = found;
        return true;
    }

    order = null;
    return false;
}

var orders = new Dictionary<int, Order>
{
    [1001] = new Order(1001, 49.95m)
};

if (TryFindOrder(orders, 1001, out var order))
{
    Console.WriteLine(order!.Total);
}

public sealed record Order(int Id, decimal Total);
```

The `out var order` syntax declares the receiving variable at the call site. Its inferred type comes from the method parameter. The variable remains in scope after the call, although its nullability or usefulness may depend on the returned success value.

The .NET `TryParse` pattern is the best-known use of `out`:

```csharp
string input = "2026-09-10";

if (DateOnly.TryParse(input, out var date))
{
    Console.WriteLine(date.DayOfWeek);
}
else
{
    Console.WriteLine("The date is invalid.");
}
```

This design is useful when a method naturally returns a primary result such as success or failure and needs to provide one additional value without throwing for an expected failure.

### Caller and callee requirements for `out`

- The caller does not have to initialize the variable before the call.
- The caller uses `out`, or declares a variable with `out var`.
- The callee must assign the parameter before reading it.
- The callee must assign it before every normal return.
- An explicit `out` argument must be a writable variable of the parameter's exact type; normal implicit conversions do not apply. `out var` can declare a correctly typed variable as part of the call.

An existing value in a variable passed with `out` is not meaningful input. Even if the variable was initialized, the method must treat the parameter as unassigned and produce a value.

## `in` Parameters

An `in` parameter passes an argument by reference while preventing the callee from assigning through that parameter. The caller must provide an initialized value, and the method can read it but cannot replace it.

```csharp
static bool IsWithinRange(in Measurement measurement, double value)
{
    // measurement = new Measurement(); // Compile-time error
    return value >= measurement.Minimum &&
           value <= measurement.Maximum;
}

var measurement = new Measurement(12.5, 84.2, 43.8, 25_000);
Console.WriteLine(IsWithinRange(in measurement, 50)); // True

public readonly record struct Measurement(
    double Minimum,
    double Maximum,
    double Average,
    long SampleCount);
```

The read-only rule applies to the parameter variable: the method cannot assign another `Measurement` to `measurement`. For a value type, it also cannot directly mutate the value through that parameter.

For a reference type, `in` prevents replacing the reference, not changing the referenced object. This mirrors the difference between a read-only variable and an immutable object.

```csharp
static void RenameCustomer(in Customer customer)
{
    // customer = new Customer("Replacement"); // Compile-time error
    customer.Name = "Renamed";                  // The object is still mutable
}

var customer = new Customer("Original");
RenameCustomer(in customer);

Console.WriteLine(customer.Name); // Renamed

public sealed class Customer(string name)
{
    public string Name { get; set; } = name;
}
```

Consequently, `in` is not a general immutability feature. Use immutable types when the object itself must not change.

At many call sites, writing `in` is optional. When `in` is explicit, the argument must be a definitely assigned variable of the parameter's exact type; normal implicit conversions do not apply. When the modifier is omitted, C# can create a temporary for some expressions, including literals, properties, and values that require an implicit conversion.

```csharp
static void Inspect(in int value) => Console.WriteLine(value);

int number = 42;
Inspect(in number); // Explicit: passes the assigned int variable by reference
Inspect(42);        // Omitted: the compiler can create a temporary
```

Omitting `in` can also affect overload resolution. If both `Inspect(int)` and `Inspect(in int)` exist, a call such as `Inspect(number)` selects the by-value overload, while `Inspect(in number)` selects the `in` overload.

### Performance considerations

Passing a value type normally copies its fields. Passing a large struct with `in` can avoid that initial copy, which may help in a measured hot path. It is not automatically faster:

- Small structs are often inexpensive to copy and may be optimized efficiently.
- Indirection through a reference can have its own cost.
- Calling non-read-only members on a mutable struct through `in` may cause defensive copies.
- JIT optimizations, inlining, architecture, and calling patterns affect the result.

Prefer `readonly struct` for genuinely immutable value types, design structs to stay reasonably small, and use BenchmarkDotNet or representative production measurements before adding `in` solely for speed. Clarity is usually more valuable than a speculative optimization.

## `ref`, `out`, and `in` Compared

| Characteristic | `ref` | `out` | `in` |
| --- | --- | --- | --- |
| Passed by reference | Yes | Yes | Yes |
| Caller assignment requirement | Variable must be assigned | Variable need not be assigned | Explicit `in` variable must be assigned; an omitted modifier may use a temporary |
| Callee may read the incoming value | Yes | No, not before assignment | Yes |
| Callee must assign before returning | No | Yes | No |
| Callee may replace the caller's value | Yes | Yes | No |
| Modifier normally shown at call site | Yes | Yes | Optional |
| Typical intent | Read and update existing state | Produce an additional result | Read a value without copying it |

The compiler enforces these rules through definite-assignment analysis. They are not merely conventions or documentation.

## Practical Design Examples

### Updating an existing accumulator with `ref`

Suppose a parser processes batches and must update an existing total. A `ref` parameter makes that mutation explicit:

```csharp
using System.Globalization;

static void AddValidAmounts(
    IEnumerable<string> inputs,
    ref decimal total)
{
    foreach (var input in inputs)
    {
        if (decimal.TryParse(
                input,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount) &&
            amount >= 0)
        {
            total += amount;
        }
    }
}

decimal invoiceTotal = 25m;
AddValidAmounts(["10.50", "invalid", "4.25"], ref invoiceTotal);

Console.WriteLine(invoiceTotal.ToString(CultureInfo.InvariantCulture)); // 39.75
```

Returning the new total may be clearer in many APIs. `ref` is appropriate here only if updating existing caller-owned state is the intended contract.

### Returning a parsed value with `out`

A custom `Try` method can return failure details without using exceptions for routine invalid input:

```csharp
static bool TryCreatePercentage(
    string text,
    out decimal percentage,
    out string? error)
{
    if (!decimal.TryParse(text, out percentage))
    {
        error = "The value is not a number.";
        return false;
    }

    if (percentage is < 0 or > 100)
    {
        percentage = default;
        error = "The percentage must be between 0 and 100.";
        return false;
    }

    error = null;
    return true;
}
```

Multiple `out` values are legal, but a dedicated result type often scales better when results have richer meaning or the method needs several outputs.

```csharp
public sealed record PercentageParseResult(
    bool Success,
    decimal Percentage,
    string? Error);
```

### Inspecting a large immutable value with `in`

`in` can express read-only access to a larger value type in performance-sensitive code:

```csharp
static decimal GetRange(in PriceSnapshot snapshot) =>
    snapshot.High - snapshot.Low;

public readonly record struct PriceSnapshot(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTimeOffset Timestamp);
```

This is a candidate for measurement, not proof of an optimization. If snapshots are infrequent or copying cost is insignificant, an ordinary value parameter may produce simpler code with equivalent real-world performance.

## Common Mistakes and Misconceptions

### “Objects are passed by reference by default”

They are not. A class variable contains a reference, and that reference is passed **by value** unless a by-reference modifier is used. Object mutation can be visible to the caller because both copied references identify the same object. Reassigning the ordinary parameter is not visible.

### Using `ref` just to modify an object

This adds unnecessary coupling:

```csharp
static void MarkPaid(Invoice invoice)
{
    invoice.IsPaid = true;
}

public sealed class Invoice
{
    public bool IsPaid { get; set; }
}
```

No `ref` is required because the method does not replace the caller's variable. Whether mutating the object is good design is a separate question.

### Reading an `out` parameter before assigning it

An `out` parameter has no incoming value from the callee's perspective. Assign it first. The compiler rejects attempts to read it before definite assignment and rejects return paths that leave it unassigned.

### Treating `in` as deep immutability

`in` prevents assignment through the parameter. It does not recursively freeze objects reachable through a reference, and it cannot turn a mutable class into an immutable one.

### Assuming `in` always improves performance

Avoid sprinkling `in` across APIs based only on struct size guesses. Defensive copies and indirection may erase the expected benefit. Measure the complete workload and consider API clarity.

### Trying to pass properties with `ref` or `out`

Most properties are accessor calls rather than directly aliasable storage, so they cannot be used as `ref` or `out` arguments. Copy the property value to a variable, call the method, and assign the result back—or redesign the method to return a value.

### Using by-reference parameters in asynchronous APIs

Methods declared with `async` cannot have `ref`, `in`, or `out` parameters. An asynchronous operation may complete after the original call has returned, which does not fit the required lifetime and assignment model. Return a value, tuple, or result object instead.

## When to Use Each Modifier

Use `ref` when:

- The method intentionally reads and updates an existing caller variable.
- Replacing that variable is central to the operation, as with swapping.
- A carefully measured low-level API benefits from writable by-reference access.

Avoid `ref` when:

- The method only needs to mutate an object.
- Returning a new value would make data flow clearer.
- It would expose surprising side effects in an otherwise simple API.

Use `out` when:

- Implementing a familiar `Try...` pattern with a success result and one output.
- Interoperating with APIs or native code whose contract uses output parameters.
- The output is simple, immediate, and unmistakably tied to the call.

Avoid `out` when:

- Several outputs would be clearer as a record, tuple, or result object.
- Failure needs rich information or asynchronous processing.
- Exceptions are appropriate because failure is genuinely exceptional.

Use `in` when:

- A method only reads a value type.
- The value type is large enough that copying may matter.
- Measurement shows a benefit, or a low-level API has a clear read-only-reference contract.

Avoid `in` when:

- The type is a small value type such as `int`, `bool`, or a small enum.
- It is being used as a substitute for object immutability.
- It makes an ordinary application API harder to understand without a demonstrated benefit.

## Interview-Oriented Questions

### Are reference-type arguments passed by reference by default?

No. The object reference is passed by value. The callee and caller can reach the same object, but assigning a new object to the ordinary parameter does not change the caller's variable. A `ref` parameter is required to replace that variable.

### What is the definite-assignment difference between `ref` and `out`?

A `ref` argument must be assigned by the caller before the call, and the callee may read it immediately. An `out` argument need not be initialized, but the callee must assign it before reading it and before every normal return.

### Does `in` make a reference-type object immutable?

No. It prevents the parameter from being reassigned through that alias. Members of the referenced object can still be mutated when the object's API permits it.

### Why might `in` make code slower?

It adds indirection, can inhibit some optimizations, and may trigger defensive copies when non-read-only members of mutable structs are invoked. Its effect depends on the type, runtime, hardware, and call pattern, so it should be measured.

### How do `ref`, `out`, and `in` affect overloading?

Methods cannot be overloaded solely by changing a parameter among `ref`, `out`, and `in`. For example, a type cannot declare both `Process(ref int)` and `Process(out int)`, or both `Process(ref int)` and `Process(in int)`. A by-value overload and a by-reference overload can coexist where C# permits it, such as `Process(int)` and `Process(in int)`. In that case, `Process(value)` selects the by-value overload, while `Process(in value)` explicitly selects the `in` overload.

### Why are by-reference parameters often a design signal?

They expose mutation or multiple-output behavior directly in a method's contract. That can be exactly right for focused low-level operations, but frequent use in business APIs may indicate that returning a meaningful result object would be clearer.

## Summary

`ref`, `out`, and `in` all pass an argument by reference, but they communicate different contracts:

- `ref` aliases an initialized variable that the method may read and replace.
- `out` represents a value the method must assign before returning.
- `in` aliases an initialized value through a read-only parameter.

These modifiers are independent of whether a type is a value type or a reference type. Reference types are normally passed by value—the copied value happens to be an object reference. Use by-reference parameters when their semantics make data flow clearer or when a measured performance need justifies them, not as a default alternative to ordinary parameters and return values.
