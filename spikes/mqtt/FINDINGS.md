# MQTT spike findings (for ADR-0001)

Throwaway code; not part of the solution. Run date 2026-10-13.

**Environment.** Windows 11, Docker Desktop (16 cores). Broker `eclipse-mosquitto:2` (resolved to 2.1.2), client MQTTnet
5.2.0.1603, MQTT 3.1.1, QoS 1, one topic, one publishing connection, payload `plate,camera,utcTicks`.

**Reproduce.** `spikes/mqtt/run.sh <default|tuned> <order|redeliver|queue|slow>` (needs Docker and the .NET SDK; build
`MqttSpike` first). `mosquitto-default.conf` keeps Mosquitto's defaults for queue and in-flight limits;
`mosquitto-tuned.conf` raises `max_queued_messages` to 200000 and `max_inflight_messages` to 1000.

## Results

| # | Question | Result |
|---|---|---|
| S1 | Is order preserved at the assignment's load (steady state 1,000 messages per tick, 15 ticks)? | **Yes.** 10,500 sent, 10,500 received, 0 out of order, 0 duplicates. End-to-end latency p50 0.7 ms, p99 1.1 ms, max 8.9 ms. |
| S1 | Can one publisher keep up? | Publishing 1,000 messages with each QoS 1 publish awaited one after another took about 0.74 s per tick: **74 % of the 1 s budget**, little headroom. |
| S2 | What happens on a crash after applying messages but before acknowledging them? | The 20 unacknowledged in-flight messages (Mosquitto's default in-flight window) were **redelivered with the DUP flag** after reconnecting with the same client id and a persistent session, followed by the messages queued while offline, in order. The sequence rule (BR-ING-02) rejected exactly the 20 duplicates: 800 accepted, 20 rejected, 0 out of order. |
| S3 | Does the broker queue for an offline consumer? | **Default config: silently drops.** Broker acknowledged all 5,000 published messages; only **1,000 were delivered** (the oldest kept, the newest dropped). Tuned config: 5,000 of 5,000 delivered. |
| S4 | Does a slow consumer slow the producer? | **No.** Consumer at 250 messages/s against 1,000 messages/s for 10 s: publish acknowledgement latency unchanged (p50 0.8 ms). Default config: **6,527 of 10,000 lost silently**. Tuned config: 0 lost, but end-to-end lag grew to **31 s** and keeps growing with the backlog. |

## Gotchas found

1. **`localhost` vs `127.0.0.1`.** Connecting to `localhost` made every publish take about 44 ms (100 messages in 4.5 s); with
   `127.0.0.1` it took about 1.8 ms cold, 0.74 ms steady. Cause not investigated (suspected IPv6 / Docker Desktop port
   forwarding). Use `127.0.0.1`.
2. **Register the message handler before connecting.** With a persistent session the broker delivers redelivered and queued
   messages immediately on CONNECT. A handler attached afterwards missed them (1 of 800 messages lost in the first run).
3. **Timer granularity.** `Task.Delay(4)` rounds up to about 15 ms on Windows; relevant for any paced loop (generator tick
   scheduling uses a periodic timer, not delays).

## Not tested

MQTT 5 features (Receive Maximum, session and message expiry), broker restart with persistence, several publishers or
topics, larger payloads, TLS and authentication, Kafka/stream stage.
