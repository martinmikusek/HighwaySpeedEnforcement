# ADR-0001: Transport from Generator to Processor

- **Status:** Accepted (2026-10-13)
- **Date:** 2026-10-13
- **Related rules:** BR-TEL-01, BR-ING-02, BR-ING-03, BR-ING-06, BR-LB-07, BR-LB-08, BR-CFG-01, BR-SIM-02; AMB-04, AMB-06

## Context

The assignment leaves the transport "entirely up to you" (gRPC, MQTT, WebSockets, HTTP REST, raw TCP).

Forces:

- **Load.** About 1,000 small messages per second (4 fields, under 100 bytes each), emitted in a burst at every tick.
- **Ordering.** Measurements of one plate must reach per-plate processing in emission order (BR-ING-03).
- **Resource bounds.** No silent message loss and no unbounded buffers.
- **Realism.** In practice, roadside cameras publish events over unreliable (often cellular) links. Common delivery
  options are MQTT, HTTP push and FTP (vendor dependent); there is no single standard. A backbone stream (Kafka, Event Hubs)
  usually sits behind a gateway. (Based on a limited web search plus general knowledge; not exhaustive.)
- **Deployment.** The processor runs in a container (Docker Desktop); the generator and the WPF client run on the host.
- **Scope.** Tier 2 is the minimum; tier 3 (restart recovery) is a stretch goal and must not be ruled out (AMB-06).
- **Time.** The prototype must be reviewable soon, so the first stage must be cheap.

## Options considered

1. **gRPC client streaming.** Typed contract, ordered per stream, flow control, no broker. Rejected as the direction:
   not how field devices deliver data; no durability, so tier 3 is a rewrite of the transport; cleartext HTTP/2 needs a
   dedicated endpoint.
2. **MQTT directly to the processor** (broker container, e.g. Mosquitto). Realistic edge protocol, producer and consumer
   decoupled in time, QoS 1 (at-least-once) with persistent sessions. Weaknesses: no consumer-driven backpressure, the
   broker can drop messages silently when queue limits are exceeded, no replay.
3. **Partitioned stream (Kafka API: Kafka, Redpanda, Event Hubs) with MQTT ingress.** Per-key ordering by partition,
   buffering as consumer lag instead of loss, replay for recovery. Heavier infrastructure and an extra bridge.
4. **HTTP batch per tick.** Simple but polling in disguise and a hand-made contract.
5. **WebSockets, raw TCP.** Hand-made framing, schema and backpressure; no advantage.

## Decision

**Target architecture (stage 2, tier 3 stretch):**

```
cameras/generator --MQTT--> broker --> bridge --> partitioned stream (key = NumberPlate) --> processor
```

**Prototype (stage 1, tier 2):**

```
generator --MQTT--> broker --MQTT subscribe--> processor
```

The generator publishes over MQTT in both stages and does not change between them. Stage 2 adds a bridge and a
Kafka-compatible stream and swaps the processor's input adapter. The domain core only sees an asynchronous sequence of
`Measurement` and does not know the transport.

**Stage 1 settings:**

- MQTT broker in the compose file; QoS 1 (at-least-once); persistent session with a fixed client id.
- One topic and one publishing connection, so one stream of ordered messages. Per-camera topics are not used, because MQTT
  guarantees ordering only within a topic.
- Duplicates caused by QoS 1 redelivery are rejected by the sequence rule (BR-ING-02), so at-least-once delivery is safe.
- The processor reads with a single subscriber into a bounded buffer, and acknowledges a message only after it is
  accepted into the buffer. This protects the processor's memory but, as the spike showed, **does not slow the publisher**
  (the broker absorbs the backlog).
- Broker queue and in-flight limits are configured explicitly and shipped with the compose file. The spike measured the
  Mosquitto default (1,000 queued messages) dropping 80 % of 5,000 messages and 65 % of a 10,000-message burst without any
  signal to publisher or consumer. The tuned values (`max_queued_messages 200000`, `max_inflight_messages 1000`) lost nothing.
- Backlog is made visible instead of hidden: the processor exposes the age of the newest processed measurement against the
  wall clock (ingest lag) as a metric (BR-OPS-02) and logs a warning above a configurable threshold. It is not used for readiness: lag is a symptom to observe, and tying readiness to it would make the service flap under load.
- The publisher must not wait for every acknowledgement one by one: sequential awaited publishing used 74 % of the tick
  budget. It pipelines publishes while keeping the order of calls.
- Message encoding is decided in the contract design (candidate: JSON with a documented schema).

**Stage 2 recovery by replay.** Because board content depends only on accepted measurements and their timestamps
(domain invariant 8), the processor can rebuild its state by replaying the stream tail on start, without a state store.
Replay depth must cover `max(CameraBoardWindow, GlobalBoardLinger + ~10 s)` (a completed vehicle's average needs its whole
passage of about 10 s). With the default parameters (about 2 s and 3 s) 15 s is sufficient. Infinite window or linger
cannot be recovered this way (it needs full retention or a state store). Vehicles that are in flight at the replay start are tracked from their first replayed measurement, so
their averages come from a partial passage (fewer measurements); replay must start early enough to avoid that.

**Delivery semantics.**

| | Stage 1 | Stage 2 |
|---|---|---|
| Delivery | at-least-once, ordered within the topic from one publisher | at-least-once, ordered per partition key |
| Duplicates | rejected by the sequence rule | rejected by the sequence rule |
| Loss | possible if broker queue limits are exceeded or the broker restarts without persistence; the broker does not report it | none while within stream retention |
| Backpressure | none toward the publisher; the broker queue absorbs the backlog (silent loss at the limit, or growing lag if the limit is large) | consumer lag |
| Recovery | live data heals within seconds | replay of the stream tail |

## Consequences

- Realistic edge protocol, and a path to the real-world architecture without changing the generator.
- Stage 1 needs one broker container; stage 2 adds a bridge and a stream. Kafka-API code also runs on Event Hubs.
- Stage 1 has two known gaps, silent broker drops and weak backpressure, both closed by stage 2. They are documented, not
  hidden.
- Ordering depends on one topic and one publisher connection; in a real deployment every camera is a separate producer and
  ordering between cameras is not guaranteed, so a real system would tolerate reordering (AMB-04, option 2). The prototype
  relies on the simplification of a single generator.
- Open for later decisions: broker choice and limits, message encoding, ingest buffer capacity, per-plate routing inside
  the processor, generator behavior when publishing is blocked, bridge implementation.
- Stage 1 cannot give a bounded buffer and no loss at the same time: the choice is loss (small queue) or unbounded lag
  (large queue). We choose a large queue plus visible ingest lag; stage 2 removes the trade-off.

## Spike results

Details in `spikes/mqtt/FINDINGS.md` (Mosquitto 2.1.2, MQTTnet 5.2, MQTT 3.1.1, QoS 1):

- **Ordering holds.** 10,500 messages at the assignment's load: 0 out of order, 0 duplicates, latency p99 about 1 ms.
- **At-least-once works with the sequence rule.** After a crash between applying and acknowledging, the broker redelivered
  the 20 in-flight messages with the DUP flag and BR-ING-02 rejected exactly those; 800 of 800 accepted once.
- **Queue limits and backpressure behave as the stage 1 settings and consequences describe** (silent loss at the default limit, no
  backpressure toward the publisher).
- **Implementation rules learned:** connect to `127.0.0.1` rather than `localhost` (44 ms per publish otherwise on this
  machine); attach the message handler before connecting; pipeline publishes.
- Not tested: MQTT 5 flow control, broker restart persistence, several publishers.
