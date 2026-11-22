---
title: "Operating RabbitMQ .NET Consumers: Prefetch, Recovery, and Observability"
excerpt: "Operate RabbitMQ .NET consumers with bounded prefetch, safe concurrency, connection recovery, graceful shutdown, metrics, TLS, and integration testing."
category: ".NET"

seo:
  focusKeyword: "RabbitMQ .NET production consumer"
  description: "Run RabbitMQ .NET consumers in production with prefetch tuning, safe channel concurrency, automatic recovery, observability, security, and Testcontainers tests."
  socialTitle: "RabbitMQ .NET Consumers in Production"
  socialDescription: "Tune prefetch and concurrency, recover connections safely, expose useful broker metrics, and test real failure boundaries."
---

# Operating RabbitMQ .NET Consumers: Prefetch, Recovery, and Observability

A consumer sets unlimited prefetch and starts 200 database operations at once. Throughput rises briefly, then connection-pool waits, memory, and unacknowledged deliveries all spike. Broker tuning must respect application capacity.

> **Quick answer:** Keep connections long-lived, isolate channel ownership, set bounded prefetch near measured concurrency, and acknowledge only after durable success. Treat automatic recovery as connection repair rather than message buffering, expose queue and consumer metrics, and test shutdown, network loss, and redelivery with a real broker.

## Align Prefetch with Real Concurrency

Prefetch limits deliveries that are in flight and unacknowledged:

```csharp
await channel.BasicQosAsync(
    prefetchSize: 0,
    prefetchCount: 16,
    global: false,
    cancellationToken: cancellationToken);
```

Start near the number of messages the worker can process concurrently. Higher values can hide network latency, but they also concentrate unacknowledged work on one consumer and increase memory pressure.

Tune with handler latency, consumer capacity, database saturation, and redelivery behavior. The official [consumer guidance](https://www.rabbitmq.com/docs/consumers) describes prefetch as a protection against consumer overload, not a target to maximize.

## Isolate Channels by Responsibility

Connections are long-lived and can be shared. Channels carry protocol state and should have clear ownership:

- one or a bounded pool for publishers;
- one per consumer subscription or concurrency unit according to the design;
- a separate confirmed channel for retry publication;
- separate administrative topology work.

Do not acknowledge a delivery on a different channel. Avoid concurrent publishing on the same channel unless access is serialized as the client guide requires.

Channel-level protocol errors close the channel. Examples include incompatible queue declarations and unknown delivery tags. Automatic connection recovery does not turn an invalid protocol operation into a valid one.

## Know What Automatic Recovery Does Not Do

```csharp
var factory = new ConnectionFactory
{
    HostName = host,
    UserName = username,
    Password = password,
    ClientProvidedName = "orders-worker:consumer",
    AutomaticRecoveryEnabled = true,
    TopologyRecoveryEnabled = true,
    NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
};
```

Recovery can reopen connections, channels, topology, and consumers after detected network failure. There is still a detection window. Publications attempted during an outage can fail, and the client does not provide a durable application outbox.

When topology recovery is enabled, do not run a competing manual recovery loop that declares the same consumers and bindings. Duplicate recovery can create more protocol failures.

Consumers must tolerate redelivery after connection loss. Publishers that cannot lose work still need confirms and a durable retry source.

## Stop Without Abandoning Ownership Silently

A hosted worker should:

1. cancel the consumer so no new deliveries arrive;
2. stop accepting new internal work;
3. wait for active handlers within the shutdown budget;
4. acknowledge successful work;
5. leave unfinished deliveries unacknowledged so the broker can requeue them;
6. close channels and the connection.

Do not acknowledge every outstanding delivery merely to make shutdown fast. Container termination grace periods must exceed the normal bounded handler duration, or the process will routinely stop mid-message.

## Observe Broker and Application Signals

RabbitMQ exposes management and Prometheus metrics. Monitor:

- ready and unacknowledged messages;
- publish, delivery, acknowledgement, and redelivery rates;
- consumer capacity;
- connection and channel churn;
- memory and disk alarms;
- quorum queue leader availability and replica health;
- dead-letter and parking-lot growth.

Application telemetry should record message type, routing key, outcome, duration, and attempt classification. Do not put raw payloads, customer IDs, or message IDs into unbounded metric labels.

Use the client-provided connection name so operators can identify components in broker logs and the management UI.

## Secure the Broker Boundary

In production:

- use TLS and validate server certificates;
- assign one credential per workload;
- restrict virtual-host configure, write, and read permissions;
- disable remote use of the default `guest` account;
- rotate credentials without embedding them in images;
- restrict management UI and metrics endpoints;
- apply resource limits and broker alarms.

Authentication proves the client identity. Consumers must still validate message contracts and authorize business actions.

## Test with a Real Broker

Unit-test handlers without RabbitMQ. Use an integration broker—often through Testcontainers—to verify:

- declarations and bindings;
- mandatory returns;
- publisher confirms;
- manual acknowledgement and redelivery;
- dead-letter and TTL retry routing;
- shutdown with an in-flight delivery;
- connection interruption and recovery.

Keep the broker version close to production. Integration tests should use unique queue names or isolated virtual hosts and remove their topology afterward.

Production RabbitMQ performance is not the highest message rate a benchmark can produce. It is sustainable throughput with bounded in-flight work, explainable failures, and recoverable ownership transitions.
