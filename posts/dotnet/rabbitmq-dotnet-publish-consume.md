---
title: "RabbitMQ with .NET: Publish and Consume Messages with the Async Client"
excerpt: "Build a practical RabbitMQ producer and .NET worker using long-lived connections, async channels, durable queues, JSON contracts, and manual acknowledgements."
category: ".NET"

seo:
  focusKeyword: "RabbitMQ .NET publish consume"
  description: "Publish and consume RabbitMQ messages in .NET with RabbitMQ.Client 7, async APIs, durable queues, manual acknowledgements, and correct connection lifetimes."
  socialTitle: "RabbitMQ with .NET: Practical Publish and Consume"
  socialDescription: "Run RabbitMQ locally and build an async .NET producer and worker with reliable connection and acknowledgement boundaries."
---

# RabbitMQ with .NET: Publish and Consume Messages with the Async Client

A demo opens a RabbitMQ connection, publishes one message, and exits. In production, opening a connection per request creates unnecessary handshakes and resource churn. The first design decision is therefore lifecycle, not serialization.

> **Quick answer:** Keep RabbitMQ connections and channels long-lived, use the asynchronous `RabbitMQ.Client` 7 APIs, publish a versioned contract with persistent delivery to a durable queue, and consume with manual acknowledgements. Copy or deserialize the received body before the callback returns.

## Run RabbitMQ Locally

Use a local broker for development:

```yaml
services:
  rabbitmq:
    image: rabbitmq:4-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: app
      RABBITMQ_DEFAULT_PASS: local-only-password
```

Do not reuse these credentials outside local development. Production credentials belong in a secret manager, should use a dedicated virtual host, and need narrowly scoped permissions.

Install the client:

```bash
dotnet add package RabbitMQ.Client
```

The [RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) describes the version 7 asynchronous APIs used here. RabbitMQ also recommends evaluating its AMQP 1.0 .NET client for new applications; protocol choice should be explicit rather than accidental.

## Open Long-Lived Connections and Channels

```csharp
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = configuration["RabbitMQ:Host"] ?? "localhost",
    UserName = configuration["RabbitMQ:Username"] ?? "app",
    Password = configuration["RabbitMQ:Password"]
        ?? throw new InvalidOperationException("RabbitMQ password is missing."),
    ClientProvidedName = "orders-api:publisher",
    AutomaticRecoveryEnabled = true
};

IConnection connection = await factory.CreateConnectionAsync(cancellationToken);
IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
```

Connections are TCP resources designed to be shared and long-lived. Channels are lightweight protocol sessions, but they are not a substitute for synchronization: avoid unsafely publishing concurrently on one channel.

Register connection ownership in a hosted singleton that closes and disposes resources during shutdown. Do not hide async initialization in a synchronous constructor.

## Declare a Durable Queue

```csharp
const string queueName = "orders.fulfill";

await channel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null,
    cancellationToken: cancellationToken);
```

Queue declaration is idempotent only when the existing queue has compatible properties. Changing durability or arguments under the same name causes a channel-level protocol error. Manage production topology through controlled deployment changes.

## Publish a Versioned Contract

```csharp
using System.Text.Json;
using RabbitMQ.Client;

public sealed record FulfillOrderV1(
    Guid EventId,
    Guid OrderId,
    DateTimeOffset AcceptedAt);

FulfillOrderV1 value = new(Guid.NewGuid(), orderId, DateTimeOffset.UtcNow);
byte[] body = JsonSerializer.SerializeToUtf8Bytes(value);

var properties = new BasicProperties
{
    ContentType = "application/json",
    Type = "orders.fulfill.v1",
    MessageId = value.EventId.ToString(),
    DeliveryMode = DeliveryModes.Persistent
};

await channel.BasicPublishAsync(
    exchange: string.Empty,
    routingKey: queueName,
    mandatory: true,
    basicProperties: properties,
    body: body,
    cancellationToken: cancellationToken);
```

The default exchange routes by queue name. Persistent delivery plus a durable queue improves restart survival, but publisher confirms are still required when the producer must know whether the broker accepted responsibility.

## Consume with Manual Acknowledgements

```csharp
using RabbitMQ.Client.Events;

await channel.BasicQosAsync(
    prefetchSize: 0,
    prefetchCount: 8,
    global: false,
    cancellationToken: cancellationToken);

var consumer = new AsyncEventingBasicConsumer(channel);

consumer.ReceivedAsync += async (_, args) =>
{
    try
    {
        FulfillOrderV1 message =
            JsonSerializer.Deserialize<FulfillOrderV1>(args.Body.Span)
            ?? throw new InvalidOperationException("Message body is empty.");

        await handler.HandleAsync(message, cancellationToken);
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false,
            cancellationToken);
    }
    catch (JsonException)
    {
        await channel.BasicRejectAsync(
            args.DeliveryTag,
            requeue: false,
            cancellationToken);
    }
    catch (Exception)
    {
        await channel.BasicNackAsync(
            args.DeliveryTag,
            multiple: false,
            requeue: true,
            cancellationToken);
    }
};

string consumerTag = await channel.BasicConsumeAsync(
    queue: queueName,
    autoAck: false,
    consumer: consumer,
    cancellationToken: cancellationToken);
```

Version 7 exposes payloads as `ReadOnlyMemory<byte>`. Deserialize or copy the body before the callback returns; retaining the memory afterward is unsafe.

## Make Shutdown Part of the Contract

On shutdown, cancel the consumer, wait for active handlers within a bounded grace period, then close the channel and connection. If the process stops before acknowledgement, RabbitMQ can redeliver the message. The handler must therefore be idempotent.

RabbitMQ provides transport primitives. The application still owns contract compatibility, safe retry behavior, resource lifetime, and the transaction boundary between business work and acknowledgement.
