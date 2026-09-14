---
title: "Global Exception Handling in ASP.NET Core: A Clean Approach"
excerpt: "Build consistent ASP.NET Core error responses with IExceptionHandler, Problem Details, explicit exception mapping, validation, and safe production logging."
category: ".NET"

seo:
  focusKeyword: "global exception handling in ASP.NET Core"
  description: "Use IExceptionHandler and Problem Details in ASP.NET Core to map errors consistently, handle validation, and log failures without exposing internals."
  socialTitle: "Global Exception Handling in ASP.NET Core: A Clean Approach"
  socialDescription: "Build consistent ASP.NET Core error responses with IExceptionHandler, Problem Details, explicit exception mapping, validation, and safe production logging."
---

# Global Exception Handling in ASP.NET Core: A Clean Approach

An endpoint catches a missing order and returns 404. Another catches the same exception and returns 400. A third returns the exception message as a 500 response. Each endpoint looks reasonable in isolation, but clients now depend on an inconsistent error contract.

Centralized exception handling gives the API one place to translate failures into HTTP responses. It also separates the business decision that failed from the transport format used to report it.

> **Quick answer:** Use `IExceptionHandler` with exception-handling middleware and Problem Details. Map known failures explicitly, return generic responses for unexpected exceptions, and define one logging policy. Keep normal validation and expected outcomes readable rather than throwing exceptions for every branch.

## Why Scattered `try/catch` Blocks Become a Problem

Repeated endpoint catches tend to drift. Developers choose different status codes, omit logs, log the same failure several times, or accidentally expose connection strings through exception text. Every new endpoint must remember the same conventions.

Moving that boilerplate to a handler makes the policy testable. It does not mean that local catches are forbidden. Catch locally when the code can recover, translate a provider-specific exception into a meaningful application failure, or perform a narrowly defined compensation. Let failures that the operation cannot resolve reach the boundary.

Avoid catching `Exception` merely to log and rethrow at every layer. That often produces several records for one failure without adding useful context. When rethrowing the same exception, use `throw;` to preserve its stack.

## Define an Error Contract Before Writing the Handler

Problem Details is a standard response shape. ASP.NET Core represents it with `ProblemDetails`; validation responses can include a field-error dictionary.

| Field | Purpose |
| --- | --- |
| `type` | Identifier for the kind of problem |
| `title` | Short description of that problem |
| `status` | HTTP status associated with the response |
| `detail` | Safe explanation specific to this occurrence |
| `instance` | Optional identifier for this occurrence |
| Extensions | Application fields such as an error code or trace ID |

A client should branch on a documented status and stable code or problem type, not parse English exception messages. Do not put SQL, stack traces, request bodies, or tokens in these fields.

Decide mappings according to the operation. An order that does not exist may be 404. An order that exists but cannot be cancelled in its current state may be 409. An unexpected database failure is not automatically 400 simply because it happened while processing input.

## Application Exceptions Without HTTP Dependencies

These types describe failures without importing ASP.NET Core:

```csharp
public sealed class OrderNotFoundException : Exception
{
    public OrderNotFoundException() : base("Order was not found.") { }
}

public sealed class OrderStateConflictException : Exception
{
    public OrderStateConflictException() : base("Order state prevents this operation.") { }
}

public sealed class RequestValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public RequestValidationException(
        IReadOnlyDictionary<string, string[]> errors)
        : base("Request validation failed.")
    {
        Errors = errors;
    }
}
```

The validation dictionary must contain deliberately authored, client-safe messages. Do not copy an arbitrary provider exception into it.

Exceptions are one option for communicating failure across layers. A typed result can be clearer when rejection is an ordinary, frequent outcome. For example, checking whether a discount applies need not throw when the answer is no. Centralized handling still protects the API from unexpected failures even if most use cases return results.

## Implement `IExceptionHandler`

The following handler targets ASP.NET Core 10. Place it and the exception types in their own files in a Web SDK project.

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
            return false;

        if (exception is OperationCanceledException &&
            context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        ProblemDetails problem = exception switch
        {
            OrderNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Order not found"
            },
            OrderStateConflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Order cannot be changed in its current state"
            },
            RequestValidationException validation =>
                new HttpValidationProblemDetails(
                    validation.Errors.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Request validation failed"
                },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred"
            }
        };

        string code = exception switch
        {
            OrderNotFoundException => "order_not_found",
            OrderStateConflictException => "order_state_conflict",
            RequestValidationException => "validation_failed",
            _ => "internal_error"
        };

        string traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled API failure. TraceId: {TraceId}",
                traceId);
        }

        context.Response.StatusCode = problem.Status!.Value;

        bool written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem,
                Exception = exception
            });

        if (!written)
        {
            // This API documents a JSON error response even when the
            // request Accept header does not match a registered writer.
            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }

        return true;
    }
}
```

Returning `true` means the handler has dealt with the exception. Returning `false` lets the middleware try another registered handler or its fallback behavior. Register specific handlers before a catch-all if you split this example into several types.

The explicit fallback matters because a registered Problem Details writer may decline a response based on content negotiation. This example **intentionally returns JSON even when the request's `Accept` header does not support it**. That is an API contract choice, not the universal default. An API that enforces content negotiation should implement and document its own fallback rather than claim an exception was handled while returning an empty response.

The cancellation guard avoids treating a disconnected client as an unexpected server defect. It does not map every `OperationCanceledException` to a client disconnect: an internal timeout needs a separate policy. Once response headers or streaming output have started, middleware cannot reliably replace the response with a fresh Problem Details document.

## Register the Pipeline in `Program.cs`

This small endpoint demonstrates application validation and a known failure. A set of known order IDs stands in for a repository lookup; replace it with an injected use-case handler in a real application.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<IReadOnlySet<Guid>>(
    new HashSet<Guid>
    {
        Guid.Parse("e28c877d-b92a-4372-b342-69b3e912961b")
    });

var app = builder.Build();

// Register early enough to catch failures from downstream middleware.
app.UseExceptionHandler();

app.MapPost("/orders/{id:guid}/cancel",
    (Guid id, CancelOrderRequest request, IReadOnlySet<Guid> knownOrders) =>
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new RequestValidationException(
                new Dictionary<string, string[]>
                {
                    ["reason"] = new[] { "A cancellation reason is required." }
                });
        }

        // The in-memory set stands in for a use-case lookup.
        if (!knownOrders.Contains(id))
            throw new OrderNotFoundException();

        return Results.NoContent();
    });

app.Run();

public sealed record CancelOrderRequest(string? Reason);
```

The in-memory lookup makes the not-found path concrete but does not perform cancellation. In a production API, the use case loads the order, checks access, invokes domain behavior, and saves the change. Authenticate and authorize before exposing order information or changing state; a not-found policy may also need to avoid revealing another tenant's resource.

`AddExceptionHandler<T>` registers handlers as singletons. Do not constructor-inject a scoped `DbContext` into one. HTTP translation should rarely need a database query; if request-scoped services are necessary, resolve them from the request scope deliberately.

See [Dependency Injection in .NET](https://dev.jun-kim.net/2026/09/10/dependency-injection-in-net-what-it-is-and-why-it-matters/) for the scoped-versus-singleton lifetime rules behind that restriction.

## Validation Is Not One Pipeline

There are several distinct failure paths:

- Malformed JSON or parameter binding can fail before the endpoint executes.
- Controllers using `[ApiController]` can return automatic model-validation responses.
- Application validation checks use-case inputs and can return a result or raise a known exception.
- Domain rules protect valid state even when invoked outside HTTP.

An exception handler only processes exceptions that reach it. It does not automatically rewrite every existing 400 response or every authorization rejection. Configure model validation and other response paths separately if clients require the same extension fields everywhere.

For a simple endpoint, returning `Results.ValidationProblem(errors)` directly can be clearer than throwing. The exception example is useful when validation happens deeper in the application and the project already uses that convention. Pick one consistent application contract without forcing every layer to reference `IResult`.

Do not broadly map `ArgumentException` or `InvalidOperationException` to 400. Those exceptions can indicate programming bugs. Use specific failure types or result cases whose client-facing meaning is known.

## Logging in ASP.NET Core 10

The handler above logs unexpected failures explicitly. ASP.NET Core 10 suppresses exception-handler diagnostics by default when a handler returns `true`. Earlier versions behave differently; the [ASP.NET Core error-handling documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0) describes the version-specific diagnostic policy.

If operations needs middleware diagnostics for handled exceptions, configure `ExceptionHandlerOptions.SuppressDiagnosticsCallback` intentionally. Avoid enabling overlapping logs without deciding which component owns the error event. The same choice affects diagnostic coverage beyond the explicit log statement, so review metrics and tracing with the logging configuration.

Treat expected conflicts and validation failures differently from unexpected defects. They may deserve counters or structured informational events, but logging every invalid form submission as an error makes alerts less useful.

The response trace ID allows support to find the server-side failure. Restrict access and retention for those logs: recording an exception object can still capture sensitive provider details even when the response is safe.

## What Central Handling Does Not Solve

The middleware does not roll back arbitrary side effects. A failed request may already have charged a payment provider or published a message. Transactions, idempotency, and an outbox address those consistency problems.

It also does not catch detached background tasks, failures in another process, or errors after the request has ended. Background workers need their own failure, retry, and observability policies. Do not start untracked tasks from an endpoint and assume this handler protects them.

For infrastructure failures, translate only what you understand. A unique-constraint violation may map to a conflict if it represents a documented business rule. A database outage should not be disguised as that same conflict.

## Recommended Production Approach

Start with a small mapping of known application failures and a generic 500 fallback. Keep HTTP details at the API boundary, make validation paths consistent, and document stable client-facing codes.

Verify the contract with integration tests through the real middleware pipeline:

| Scenario | Verify |
| --- | --- |
| Missing resource | 404 and stable code |
| Invalid state transition | 409 without internal exception text |
| Validation failure | 400 and the expected field errors |
| Unexpected exception | Generic 500, trace ID, and expected logging |
| Unsupported Accept header | Documented fallback behavior |
| Authentication or binding failure | Consistent response policy outside the exception handler |

Also exercise cancellation and streaming where the API uses them. A direct unit test of the exception switch cannot prove middleware ordering, JSON serialization, or response-started behavior.

The goal is a dependable boundary: clients receive a useful contract, operators retain diagnostic evidence, and business code can report failure without becoming an HTTP formatter.
