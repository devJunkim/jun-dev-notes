---
title: "C# Value Types vs Reference Types: A Complete Guide"
excerpt: "Learn how value types and reference types work in C#, how they behave when assigned or passed to methods, and why understanding the difference matters in real-world .NET development."
category: "C#"

seo:
  focusKeyword: "C# value types vs reference types"
  description: "Learn how C# value types and reference types differ, how assignment and method calls behave, and when these differences matter in real-world .NET code."
  socialTitle: "C# Value Types vs Reference Types: A Complete Guide"
  socialDescription: "Learn how C# value types and reference types differ, how assignment and method calls behave, and when these differences matter in real-world .NET code."
---
# C# Value Types vs Reference Types: A Complete Guide

When you assign one variable to another in C#, are you copying the data itself or copying a way to reach that data? The answer depends on whether the type is a **value type** or a **reference type**.

This distinction affects assignments, method calls, equality, mutation, performance, and API design. It is also a common source of bugs—and a frequent topic in senior .NET interviews.

> **Quick answer:** A value-type variable contains its data directly, so assignment creates an independent copy. A reference-type variable contains a reference, so assignment lets two variables refer to the same object.

## Value Types vs Reference Types at a Glance

| Behavior | Value type | Reference type |
| --- | --- | --- |
| Variable contains | The value itself | A reference to an object |
| Assignment copies | The value | The reference |
| Default value | Usually zero-like (`0`, `false`, and so on) | `null` |
| Common examples | `int`, `bool`, `enum`, `struct`, `record struct` | `class`, `record class`, `string`, arrays, delegates |
| Typical design use | Small logical values | Objects with identity or a lifecycle |

## The Core Difference

A variable of a value type contains its value directly. Assigning it to another variable copies that value.

A variable of a reference type contains a reference to an object. Assigning it to another variable copies the reference, so both variables can refer to the same object.

```csharp
int firstNumber = 10;
int secondNumber = firstNumber;
secondNumber = 20;

Console.WriteLine(firstNumber);  // 10
Console.WriteLine(secondNumber); // 20
```

`int` is a value type. `secondNumber` receives an independent copy, so changing it does not affect `firstNumber`.

Now compare that with a class:

```csharp
var firstPerson = new Person { Name = "Alice" };
var secondPerson = firstPerson;

secondPerson.Name = "Bob";

Console.WriteLine(firstPerson.Name);  // Bob
Console.WriteLine(secondPerson.Name); // Bob

public sealed class Person
{
    public string Name { get; set; } = string.Empty;
}
```

`Person` is a reference type. The assignment copies the reference, not the object. Both variables point to the same `Person`, so a mutation made through either variable is visible through the other.

## Which C# Types Belong to Each Group?

Common value types include:

- Integral numeric types such as `int`, `long`, and `byte`
- Floating-point and decimal types such as `double` and `decimal`
- `bool` and `char`
- `enum` types
- `struct` and `record struct` types
- Nullable value types such as `int?`

Common reference types include:

- `class` and `record class` types
- `string`
- Arrays
- Delegates
- Interfaces
- `object`

`string` deserves special attention. It is a reference type, but it is immutable. Operations that appear to change a string produce a new string instead of modifying the existing object.

```csharp
string first = "hello";
string second = first;
second = second.ToUpperInvariant();

Console.WriteLine(first);  // hello
Console.WriteLine(second); // HELLO
```

This can look like value-type behavior, but it is the result of immutability—not because `string` is a value type.

## Assignment Copies the Variable's Value

A precise way to describe assignment is: **C# copies the value stored in the source variable**.

- For a value-type variable, the stored value is the data.
- For a reference-type variable, the stored value is a reference.

This wording avoids the misleading idea that reference-type assignment performs no copy. A copy does occur; it is the reference that is copied.

## Passing Values to Methods

Parameters are passed by value by default in C#. This rule applies to both value types and reference types.

### Passing a value type

```csharp
static void Increment(int number)
{
    number++;
}

int count = 5;
Increment(count);

Console.WriteLine(count); // 5
```

The method receives a copy of `count`. Reassigning or changing that local copy does not change the caller's variable.

### Passing a reference type

```csharp
static void Rename(Person person)
{
    person.Name = "Bob";
}

var person = new Person { Name = "Alice" };
Rename(person);

Console.WriteLine(person.Name); // Bob
```

The method receives a copy of the reference. That copied reference still points to the same object, so the method can mutate the object.

However, assigning the parameter to another object does not replace the caller's reference:

```csharp
static void Replace(Person person)
{
    person = new Person { Name = "Charlie" };
}

var person = new Person { Name = "Alice" };
Replace(person);

Console.WriteLine(person.Name); // Alice
```

The method changes only its local copy of the reference.

### Using `ref`, `out`, and `in`

Parameter modifiers change the default behavior:

- `ref` passes a variable by reference and allows the method to read or replace it.
- `out` passes a variable by reference and requires the method to assign it.
- `in` passes a variable by readonly reference, helping avoid a copy for some large structs.

```csharp
static void Replace(ref Person person)
{
    person = new Person { Name = "Charlie" };
}

var person = new Person { Name = "Alice" };
Replace(ref person);

Console.WriteLine(person.Name); // Charlie
```

Here, `ref` gives the method access to the caller's variable, so the method can replace the stored reference.

## Stack and Heap: A Useful but Incomplete Explanation

Value types are often described as “stored on the stack,” while reference types are described as “stored on the heap.” That shortcut is not a reliable definition.

Storage depends on context:

- A value-type local may be stored on the stack, in a CPU register, or optimized away.
- A value-type field is stored as part of the object that contains it. If that object is on the managed heap, the field is there too.
- An array of value types stores its elements inline inside the array object.
- A captured local may become part of a compiler-generated object.
- Boxing a value type creates an object on the managed heap.

The important semantic distinction is copying:

- Value-type assignment copies the value.
- Reference-type assignment copies a reference to an object.

The runtime's actual storage strategy is an implementation detail unless you are investigating performance or interoperability.

## Value Types Inside Reference Types

A class can contain value-type fields. Those values are stored as part of the class instance.

```csharp
public sealed class Order
{
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

`Order` is a reference type, while `Quantity` and `UnitPrice` are value types. Copying an `Order` variable copies the reference to the same `Order`; it does not independently copy its fields.

## Reference Types Inside Value Types

A struct can contain reference-type fields. Copying the struct copies each field, including any references.

```csharp
public struct Team
{
    public string[] Members { get; set; }
}

var members = new[] { "Alice", "Bob" };
var firstTeam = new Team { Members = members };
var secondTeam = firstTeam;

secondTeam.Members[0] = "Charlie";

Console.WriteLine(firstTeam.Members[0]); // Charlie
```

`firstTeam` and `secondTeam` are separate struct values, but their `Members` fields contain references to the same array. This is a **shallow copy**.

This is one reason mutable structs—especially structs containing mutable reference types—can be surprising.

## Equality Is a Separate Concern

Value versus reference semantics does not completely determine how `==` behaves. Each type can define its own equality rules.

For many value types, equality compares contained values:

```csharp
int a = 10;
int b = 10;

Console.WriteLine(a == b); // True
```

For ordinary classes that do not override equality, equality typically uses object identity:

```csharp
var first = new Person { Name = "Alice" };
var second = new Person { Name = "Alice" };

Console.WriteLine(first == second); // False
```

The two objects contain the same text but are different instances.

Records provide value-based equality by default, even though a `record class` is a reference type:

```csharp
public sealed record Customer(string Name);

var first = new Customer("Alice");
var second = new Customer("Alice");

Console.WriteLine(first == second); // True
```

This is another reason not to equate “reference type” with “always compared by reference.”

## Boxing and Unboxing

Boxing converts a value type to `object` or to an interface it implements. The runtime creates an object containing a copy of the value.

```csharp
int number = 42;
object boxed = number; // Boxing

number = 100;

Console.WriteLine(boxed); // 42
```

Unboxing extracts the value from the boxed object and requires a compatible value type:

```csharp
object boxed = 42;
int number = (int)boxed; // Unboxing
```

Boxing creates an allocation and adds conversion overhead. Generics usually avoid unnecessary boxing:

```csharp
var numbers = new List<int>(); // Stores int values without boxing each item
numbers.Add(42);
```

Avoid boxing in hot paths when measurement shows it matters, but do not sacrifice clarity for speculative micro-optimization.

## Nullable Value Types and Nullable References

`int?` is shorthand for `Nullable<int>`, which is itself a value type. It represents either an `int` value or no value.

```csharp
int? age = null;
age = 30;
```

Nullable reference type annotations, such as `string?`, are different. They are compiler-assisted annotations that help detect possible `null` usage; they do not create a new runtime wrapper type.

```csharp
string? middleName = null;
```

The two features both express possible absence, but their runtime models are different.

## Common Mistakes

### Assuming a method cannot mutate an object

Passing a reference type by value prevents the method from replacing the caller's variable, but it does not prevent mutation of the referenced object.

Use immutable models, readonly interfaces, or defensive copies when callers must not observe changes.

### Assuming every struct copy is completely independent

A struct copy is independent at the field level, but reference-type fields may still point to shared mutable objects.

### Using large mutable structs

Large structs can be expensive to copy, while mutable structs can behave unexpectedly when returned from properties or used in collections. Prefer small, immutable structs that represent a single value.

### Relying on the stack-versus-heap shortcut

It can produce incorrect conclusions about captured variables, fields, arrays, boxing, and runtime optimizations. Reason from language semantics first.

### Confusing immutability with value semantics

`string` is immutable but remains a reference type. A type's mutability, storage, copying behavior, and equality behavior are related design concerns, but they are not the same thing.

## Choosing Between a Class and a Struct

Consider a struct when the type:

- Represents a single logical value
- Is small
- Is immutable
- Has meaningful value-based equality
- Is unlikely to be boxed frequently

Examples include coordinates, measurements, dates, ranges, and identifiers.

Consider a class when the type:

- Has identity that matters independently of its current data
- Is relatively large
- Is mutable or has a complex lifecycle
- Is shared across multiple parts of an application
- Participates in an inheritance hierarchy

These are guidelines, not hard size-based rules. Measure performance-sensitive code and choose the model that best expresses the domain.

## A Real-World Example

Money is often a good candidate for an immutable value type because two amounts with the same currency and quantity represent the same logical value.

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                "Cannot add amounts with different currencies.");
        }

        return new Money(Amount + other.Amount, Currency);
    }
}

var subtotal = new Money(100m, "CAD");
var tax = new Money(13m, "CAD");
var total = subtotal.Add(tax);

Console.WriteLine(total); // Money { Amount = 113, Currency = CAD }
```

An `Order`, in contrast, is usually modeled as a class because it has identity and a lifecycle. Its status and contents can change while it remains the same order.

## Senior Developer Interview Questions

### Are reference types passed by reference in C#?

Not by default. A reference-type variable is passed **by value**, which copies the reference. The callee can mutate the referenced object but cannot replace the caller's variable unless the parameter uses `ref` or `out`.

### Are value types always allocated on the stack?

No. Their location depends on context and runtime optimization. A value-type field can live inside a heap-allocated object, and boxing creates a heap object containing a copied value.

### What happens when a struct contains a reference-type field?

Copying the struct copies the reference field. The struct instances are distinct, but they may refer to the same underlying object.

### Is `string` a value type?

No. It is an immutable reference type. Its immutability makes many operations appear value-like.

### How do records relate to value and reference types?

A `record class` is a reference type with value-based equality by default. A `record struct` is a value type with value-based equality by default.

### Why can boxing affect performance?

Boxing generally creates a managed allocation and copies a value into it. Repeated boxing in frequently executed code increases allocation and garbage-collection pressure.

## Key Takeaways

- Value-type assignment copies the data; reference-type assignment copies a reference.
- Both value types and reference types are passed by value unless a parameter modifier changes that behavior.
- Mutating a shared object is different from replacing a variable's reference.
- Stack versus heap is not the definition of value versus reference semantics.
- Structs can contain shared references, so their copies are not necessarily deep copies.
- Equality, immutability, and value/reference classification are separate concepts.
- Prefer small immutable structs for logical values and classes for entities with identity or complex lifecycles.
- Use generics to avoid unnecessary boxing, and optimize only when measurement supports it.

Understanding these rules makes C# code easier to reason about and helps prevent subtle bugs involving shared state, method calls, and copying.
