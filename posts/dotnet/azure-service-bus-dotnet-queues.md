---
title: "Azure Service Bus with .NET: Send and Process Queue Messages"
excerpt: "Build a practical Azure Service Bus queue workflow in .NET with managed identity, dependency injection, explicit settlement, and graceful processor lifecycle."
category: ".NET"

seo:
  focusKeyword: "Azure Service Bus .NET queue"
  description: "Send and process Azure Service Bus queue messages in .NET using Azure.Messaging.ServiceBus, managed identity, dependency injection, and explicit settlement."
  socialTitle: "Azure Service Bus Queues with .NET"
  socialDescription: "Build a practical .NET producer and worker with secure authentication, explicit message completion, and graceful shutdown."
---

# Azure Service Bus with .NET: Send and Process Queue Messages

An API accepts an order, calls a slow downstream service, and keeps the HTTP request open until every step finishes. A temporary dependency outage now becomes an API outage. A queue separates accepting the work from processing it, but only if the producer, consumer, and settlement boundary are designed together.

> **Quick answer:** Use the current `Azure.Messaging.ServiceBus` client, keep `ServiceBusClient` long-lived, authenticate with `DefaultAzureCredential`, and send a versioned contract with a stable message ID. Process with `ServiceBusProcessor`, disable auto-completion when business success must control settlement, and close the processor during host shutdown.

## Create the Queue Outside Application Startup

Provision the namespace and queue with Bicep, Terraform, or another deployment tool. Do not let every application instance race to create production infrastructure at startup. A queue named `orders-to-fulfill` is enough for this example.

Grant the API the **Azure Service Bus Data Sender** role and the worker the **Azure Service Bus Data Receiver** role. Locally, `DefaultAzureCredential` can use the Azure CLI identity. In Azure, the same code can use managed identity without a connection string.

Install the client packages:

```bash
dotnet add package Azure.Messaging.ServiceBus
dotnet add package Azure.Identity
```

The current [.NET client overview](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/messaging.servicebus-readme) documents `ServiceBusClient`, senders, receivers, and processors. Avoid retired Service Bus SDKs in new code.

## Register One Long-Lived Client

`ServiceBusClient` manages AMQP connections and is safe to reuse. Creating one for every request adds connection churn and makes recovery harder.

```csharp
using Azure.Identity;
using Azure.Messaging.ServiceBus;

string fullyQualifiedNamespace = builder.Configuration[
    "ServiceBus:FullyQualifiedNamespace"]
    ?? throw new InvalidOperationException("Service Bus namespace is missing.");

builder.Services.AddSingleton(_ => new ServiceBusClient(
    fullyQualifiedNamespace,
    new DefaultAzureCredential()));

builder.Services.AddSingleton<OrderQueue>();
```

The namespace value looks like `contoso-orders.servicebus.windows.net`. Keep entity names in validated configuration rather than scattering string literals across handlers.

## Send a Versioned Contract

The transport message should identify one logical operation and describe its content type. Do not serialize an EF Core entity or expose private domain state as an integration contract.

```csharp
using Azure.Messaging.ServiceBus;

public sealed record FulfillOrderV1(
    Guid OrderId,
    string CustomerId,
    DateTimeOffset AcceptedAt);

public sealed class OrderQueue(ServiceBusClient client)
{
    private readonly ServiceBusSender _sender =
        client.CreateSender("orders-to-fulfill");

    public async Task EnqueueAsync(
        FulfillOrderV1 order,
        CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(order))
        {
            MessageId = $"fulfill-order:{order.OrderId}",
            Subject = "orders.fulfill.v1",
            ContentType = "application/json"
        };

        message.ApplicationProperties["contractVersion"] = 1;

        await _sender.SendMessageAsync(message, cancellationToken);
    }
}
```

The `MessageId` supports diagnosis and optional broker duplicate detection. It does not make the consumer idempotent by itself.

## Process Only After Durable Success

Use a processor when the application should continuously receive messages. Disable automatic completion so the code completes only after the business operation succeeds.

```csharp
using Azure.Messaging.ServiceBus;

public sealed class OrderProcessor : IHostedService, IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _processor = client.CreateProcessor(
            "orders-to-fulfill",
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 4,
                PrefetchCount = 8
            });

        _processor.ProcessMessageAsync += ProcessAsync;
        _processor.ProcessErrorAsync += LogErrorAsync;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _processor.StartProcessingAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _processor.StopProcessingAsync(cancellationToken);

    private async Task ProcessAsync(ProcessMessageEventArgs args)
    {
        FulfillOrderV1 order = args.Message.Body.ToObjectFromJson<FulfillOrderV1>()
            ?? throw new InvalidOperationException("Message body is empty.");

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<OrderHandler>();

        await handler.HandleAsync(order, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task LogErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus failure in {EntityPath} from {ErrorSource}",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _processor.DisposeAsync();
}
```

`OrderHandler` should commit its database transaction before completion. If the process stops after commit but before completion, Service Bus can redeliver the message. The handler therefore still needs a durable idempotency key.

## Treat Cancellation and Failure Deliberately

Pass the processor cancellation token into database and HTTP work. During graceful shutdown, `StopProcessingAsync` stops new delivery and waits for active handlers subject to the host's shutdown budget.

Do not catch every exception and complete anyway. A transient failure should leave the message available for another attempt. A permanently invalid contract should be dead-lettered with a safe reason. Those decisions belong in a reliability policy, not in a blanket catch block.

The queue creates a time boundary between the API and worker. Reliability comes from the contract, authentication, settlement point, and idempotent business effect—not from enqueueing alone.
