---
title: "C# Exception Filters: Selective Handling Without Losing Stack Traces"
excerpt: "Use C# exception filters, precise catch clauses, and correct rethrow semantics to preserve diagnostics and handle only failures your code understands."
category: "C#"

seo:
  focusKeyword: "C# exception filters"
  description: "Use C# exception filters and correct rethrow syntax to handle expected failures selectively while preserving useful stack traces."
  socialTitle: "C# Exception Filters and Correct Rethrow Semantics"
  socialDescription: "Handle only the exceptions your code understands while preserving the original failure context for diagnostics."
---

# C# Exception Filters: Selective Handling Without Losing Stack Traces

A broad `catch` can make an operation look reliable while hiding the failure that actually needs attention. The useful question is not whether code can catch an exception, but whether it can make a correct decision from that failure and leave the application in a known state.

> **Quick answer:** Catch the narrowest exception your boundary can interpret, use a `when` filter when handling depends on exception data or operation context, and use `throw;` when the same exception must continue upward. Do not use `throw ex;`, because it resets the apparent throw point in the stack trace.

## Catch Only What the Boundary Can Interpret

An exception type is part of a dependency's failure contract. A storage adapter may translate a provider-specific conflict into a domain result, but it should not turn authentication failures, timeouts, and programming defects into the same fallback.

```csharp
public sealed class CustomerAlreadyExistsException : Exception
{
    public CustomerAlreadyExistsException(string customerId, Exception innerException)
        : base($"Customer '{customerId}' already exists.", innerException)
    {
        CustomerId = customerId;
    }

    public string CustomerId { get; }
}

public static async Task CreateCustomerAsync(
    string customerId,
    ICustomerStore store,
    CancellationToken cancellationToken)
{
    try
    {
        await store.InsertAsync(customerId, cancellationToken);
    }
    catch (StoreException ex) when (ex.Code == StoreErrorCode.Conflict)
    {
        throw new CustomerAlreadyExistsException(customerId, ex);
    }
}
```

The filter handles only the conflict the adapter understands. Other `StoreException` values continue to the next compatible handler. The translated exception retains the provider failure as `InnerException`, while its public message avoids exposing connection details or raw server responses.

Microsoft's [exception-handling reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements) documents that filters are evaluated before the stack is unwound. This preserves the original failure context when a filter does not match.

## Filter on Stable Data, Not Message Text

Prefer typed properties, error codes, or known exception subtypes. Exception messages can change with library versions, localization, or added context.

```csharp
catch (StoreException ex) when (
    ex.Code is StoreErrorCode.Throttled or StoreErrorCode.TemporarilyUnavailable)
{
    throw new TransientStoreException("The customer store is unavailable.", ex);
}
```

Keep filter expressions small and free of side effects. If a filter itself throws, C# treats the filter as false and continues searching for a handler. That behavior prevents the filter failure from replacing the original exception, but it can make an unreliable filter silently stop matching.

Logging from a filter that returns false is technically possible, but it spreads diagnostics across stack frames and can log the same failure repeatedly. Centralized boundary logging is usually easier to reason about. Use filters primarily to select behavior.

## Preserve the Original Throw Point

When a catch block records context but cannot recover, rethrow with an empty `throw` statement:

```csharp
try
{
    await processor.ProcessAsync(message, cancellationToken);
}
catch (InvalidDataException ex)
{
    logger.LogWarning(ex, "Message {MessageId} contained invalid data", message.Id);
    throw;
}
```

`throw;` continues the current exception with its original throw location. `throw ex;` throws the expression again and changes the stack trace so the catch block appears to be the origin. The latter removes evidence needed to find the actual failing line.

Create a new exception only when the boundary is deliberately translating abstractions. Pass the original exception as the inner exception and avoid copying secrets or personal data into the new message.

## Cancellation Is Not an Ordinary Failure

Cancellation normally means the caller no longer wants the operation. A broad handler must not convert it into a retry, an empty result, or a generic error.

```csharp
try
{
    return await gateway.LoadAsync(orderId, cancellationToken);
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    throw;
}
catch (GatewayException ex) when (ex.IsTransient)
{
    return OrderLookup.Unavailable();
}
```

The filter distinguishes cancellation requested through this operation's token from a dependency that may use `OperationCanceledException` to report its own timeout. Whether dependency timeouts should be retried or translated depends on the gateway contract and the idempotency of the operation.

Do not log expected cancellation as an application error at every layer. Record it where operational policy requires it, with enough context to distinguish user cancellation, shutdown, and a dependency deadline.

## `finally` Is for Cleanup, Not Outcome Repair

A `finally` block runs whether the try block succeeds, throws, or exits early. Use it for cleanup that cannot be expressed with `using` or `await using`. Avoid returning from `finally` or throwing a new cleanup exception that masks the primary failure.

For disposable resources, lexical ownership is clearer:

```csharp
await using Stream stream = await source.OpenAsync(cancellationToken);
await serializer.WriteAsync(stream, payload, cancellationToken);
```

The compiler-generated cleanup still runs when serialization fails. If cleanup can also fail, decide which exception owns the operation outcome and preserve the other as diagnostics rather than accidentally replacing it.

## Review the Failure Contract

Before adding a catch clause, ask:

- Can this layer leave state consistent after handling the exception?
- Is the decision based on a stable type or property?
- Does translation preserve the original exception without leaking sensitive data?
- Does cancellation continue to behave as cancellation?
- Is the failure logged once at a boundary with useful context?
- Would a result type communicate an expected business outcome more clearly?

Exception filters make selective handling precise, but they do not make an unknown failure recoverable. The safest handler is often the one that declines to handle and lets the owning boundary decide.
