# Processor: component design

**Status:** Accepted (owner, 2026-10-14; implemented). Binding inputs: ADR-0001 (MQTT, two stages), ADR-0003 (pipeline, boards, watermarks), ADR-0004 (layers),
`docs/domain/` (rules `BR-xx`, scenarios `AC-xx`), `docs/contracts/`.

## 1. Purpose and scope

The processor ingests measurements from the broker, maintains ten live camera leaderboards and one live global leaderboard,
and serves them as a snapshot over REST. Tier 2 scope (AMB-06): validation, duplicate and out-of-sequence rejection, orphan
timeout, structured logs, metrics, health, a coherent snapshot. Not in scope: persistence and restart recovery (tier 3).

## 2. Contents of the projects

Namespaces equal assembly names (prefix `HighwaySpeedEnforcement.Processor`). Types are listed with their responsibility;
exact member signatures are left to the implementer, within the rules of section 4.

### Domain (no dependencies, no I/O, deterministic)

| Type | Responsibility |
|---|---|
| `Measurement` | `readonly record struct`: plate, timestamp ticks, speed (`float`), camera id. Domain's own type, not the wire type. |
| `HighwayLayout` | Constants: first camera 1, last camera 10, board size 10, minimum measurements for the global board 3. |
| `LivenessPolicy` | Window, linger, inactivity timeout as `TimeSpan` (`Timeout.InfiniteTimeSpan` = infinite). Methods computing `ExpiresAt` ticks for a camera entry, an in-flight global entry and a completed global entry; saturating arithmetic, infinite gives `long.MaxValue`. |
| `BoardEntry` | `readonly record struct`: plate, score (`double`), event timestamp ticks, `ExpiresAtTicks`, measurement count (0 on camera entries). |
| `RankOrder` | `IComparer<BoardEntry>`: score descending, then plate ascending with `string.CompareOrdinal` (BR-LB-05). |
| `RankedBoard` | Thread-safe leaderboard (section 4.2). Owns its lock. |
| `BoardView`, `RankedEntry` | Immutable result of `Top(n)`: entries with 1-based rank, plus the board's watermark (`LatestEventTicks`, null if never advanced). |
| `Stamp` | `readonly record struct`: global watermark and the camera watermark of the message's camera. |
| `WatermarkClock` | Sequential, not thread-safe: `Observe(camera, timestamp)` raises the camera and global maxima and returns the `Stamp` (BR-LB-11). |
| `StampedMeasurement` | A `Measurement` with its `Stamp`. |
| `VehicleTrack` | Mutable working state of one vehicle (plate, count, speed sum as `double`, last camera, last timestamp). Owned by one tracker. |
| `VehicleTracker` | Owns the tracks of one partition (a plain dictionary, no locking). `Process(StampedMeasurement)` applies the sequence rule and the orphan rule and returns a `TrackResult`; `Sweep(globalWatermark)` frees silent tracks; `Count`. |
| `TrackResult` | Either `Rejected` (reason `OutOfSequence`) or `Accepted` with the camera entry, the global board action (`None`, `Upsert(entry)`) and a completed flag. |
| `PlateHash` | Stable, culture-independent hash of a plate (FNV-1a over UTF-16 chars), used for partitioning. |

### Application (references Domain only)

| Type | Responsibility |
|---|---|
| `ProcessorSettings` | Plain settings object (section 6). No dependency on the options framework. |
| `IncomingMessage` | Port input: `IncomingMeasurement` (raw, possibly invalid values: nullable plate, ticks, speed, camera) or `IncomingHeartbeat` (camera, ticks). |
| `IIngestSink` | Inbound port, implemented by the pipeline: `SubmitAsync(IncomingMessage, CancellationToken)` completes when the message is accepted into a buffer (this carries the acknowledgement and backpressure of ADR-0001), and `ReportUndecodable(RejectReason)` for payloads the adapter could not decode. |
| `RejectReason` | `Malformed`, `UnknownType`, `InvalidPlate`, `InvalidCamera`, `InvalidSpeed`, `InvalidTimestamp`, `OutOfSequence` (and a counter `Ignored` for unsupported types), as in `docs/contracts/telemetry-messages.md`. |
| `MeasurementValidator` | BR-ING-01: validates an `IncomingMeasurement` in the order plate, camera, speed, timestamp; the future-timestamp check uses `TimeProvider` and `FutureTimestampTolerance`. Returns a `Measurement` or a reason. |
| `BoardSet` | The ten camera boards and the global board, created from `ProcessorSettings`/`LivenessPolicy`. |
| `IngestPipeline` | Implements `IIngestSink` (section 3): validation, stamping, routing, bounded partition channels, workers, sweep, shutdown. |
| `SnapshotService` | Builds a `TrafficSnapshot` (10 `BoardView`s, global `BoardView`, `GeneratedAt` from `TimeProvider`). |
| `IIngestMetrics` | Outbound port: counters for received, accepted, rejected (by reason), ignored. |
| `IngestStatus` | Read-only view for health and metrics: tracked vehicles, buffer depth, newest processed event time, ingest lag, last received time. |

### Infrastructure (references Application, Contracts, third-party libraries)

| Type | Responsibility |
|---|---|
| `MessageDecoder` | Payload bytes to `IncomingMessage` or a decode reason: size limit, `Type` resolution, JSON through the source-generated context of `Contracts` (`Malformed`, `UnknownType`; a heartbeat when heartbeats are disabled is `Ignored`). |
| `MqttMeasurementSource` | Hosted service: connects to the broker, subscribes, decodes and submits each message, acknowledges after `SubmitAsync` returns, reconnects with backoff, logs connection changes, records the last received time. Rules in section 5. |
| `DiagnosticsIngestMetrics` | `IIngestMetrics` on `System.Diagnostics.Metrics` (counters tagged `reason`; observable gauges from `IngestStatus`). |

### Api (host and composition root)

| Part | Responsibility |
|---|---|
| `Program` | Binds and validates settings, registers everything, JSON logging, `GET /api/traffic-snapshot`, `GET /api/status`, health endpoints. |
| `SnapshotMapper` | `TrafficSnapshot` to `TrafficSnapshotDto` (camelCase, UTC ISO 8601, `MeasuredAtUtc`, `LatestEventUtc`). |
| `IngestPipelineHostedService` | Starts the workers, stops them on shutdown after the MQTT source. |
| Health checks | Liveness, readiness (section 7). |

## 3. Message flow

1. `MqttMeasurementSource` receives a payload, `MessageDecoder` decodes it. Undecodable: `ReportUndecodable(reason)`, acknowledge, next.
2. `IngestPipeline.SubmitAsync` (sequential, in arrival order): count `received`; validate (`MeasurementValidator`); invalid: count
   and log the reason, return. A heartbeat is validated for camera and timestamp only.
3. **Stamp:** `WatermarkClock.Observe(camera, timestamp)` gives the `Stamp`.
4. **Route:** a measurement goes to `partition = PlateHash(plate) mod PartitionCount`; a heartbeat to `camera mod PartitionCount`.
   The item is written to the bounded channel of that partition; `SubmitAsync` waits while it is full.
5. `SubmitAsync` returns, the adapter acknowledges the MQTT message.
6. The partition worker reads the item:
   - Measurement: `VehicleTracker.Process`. Rejected: count and log. Accepted: advance and update the camera board of the
     measurement (`AdvanceTo(stamp.Camera)`, `Upsert(cameraEntry)`), then the global board (`AdvanceTo(stamp.Global)`,
     apply the global action). Count `accepted`. The two boards are never locked at the same time.
   - Heartbeat: advance the camera board and the global board only.
   - If `stamp.Global` is at least one second (event time) past this worker's last sweep: `Sweep(stamp.Global)`.
7. `GET /api/traffic-snapshot`: `SnapshotService` reads `Top(10)` of each board under that board's lock, assembles the snapshot
   (consistency boundary per board, BR-API-02), the endpoint maps it to the DTO.

## 4. Key algorithms

### 4.1 `VehicleTracker.Process(m, stamp)`

```
track = tracks[m.Plate] (or none)
if track exists and stamp.Global - track.LastTimestamp >= policy.InactivityTimeout:
    remove track; track = none                                   // orphaned (BR-ING-06)
if track exists and m.Camera <= track.LastCamera:
    return Rejected(OutOfSequence)                                // BR-ING-02
if track is none: create track and store it                       // any start camera
track.Add(m)                    // count++, sum += speed, lastCamera = m.Camera, lastTimestamp = max(lastTimestamp, m.Timestamp)
cameraEntry = BoardEntry(plate, speed, m.Timestamp, policy.CameraExpiry(m.Timestamp), count: 0)
completed   = (m.Camera == HighwayLayout.LastCamera)
if completed: remove track                                        // BR-ING-05
globalAction =
    track.Count >= 3 ? Upsert(BoardEntry(plate, track.Average, track.LastTimestamp,
                                         completed ? policy.CompletedExpiry(track.LastTimestamp)
                                                   : policy.InFlightExpiry(track.LastTimestamp),
                                         track.Count))
                     : None
return Accepted(cameraEntry, globalAction, completed)
```

`Sweep(globalWatermark)`: remove every track with `globalWatermark - LastTimestamp >= InactivityTimeout`. It only frees memory;
behavior does not depend on when it runs.

### 4.2 `RankedBoard`

State: dictionary by plate, a sorted set in `RankOrder`, a sorted set by `(ExpiresAt, plate)`, the watermark, and one lock.

- `Upsert(entry)`: remove the plate's existing entry from all three structures; if `entry.ExpiresAt <= Watermark` stop (the new
  version is already expired, so the plate leaves the board); otherwise insert. If the entry's lifetime is infinite and the board
  already holds the maximum number of infinite entries (10), insert and then evict the worst infinite entry.
- `Remove(plate)`.
- `AdvanceTo(watermark)`: `Watermark = max(Watermark, watermark)`; remove every entry with `ExpiresAt <= Watermark`.
- `Top(n)`: the first `n` entries of the rank-ordered set with ranks 1..n, and the watermark, under the lock.
- All operations O(log n). An implementation may offer one combined `Update(upsert, watermark)` to take the lock once.

Properties that tests must establish: the final content is independent of the order of operations on different plates
(BR-LB-13); `Top` always equals the naive recomputation from all live entries; pruning of infinite entries never changes `Top`.

### 4.3 Expiry arithmetic

`ExpiresAt = timestamp + window` for a camera entry, `+ linger` for a completed global entry, `+ InactivityTimeout` for an
in-flight global entry; saturating addition, infinite gives `long.MaxValue`. An entry is live while `ExpiresAt > watermark`
(BR-LB-07, BR-LB-08). Linger 0 makes a completed entry expire immediately: it is not inserted, and the plate's in-flight entry
is removed by the same `Upsert`.

## 5. Concurrency and threading

| Where | Threads | Rule |
|---|---|---|
| MQTT callbacks, validation, stamping, routing | one logical sequence (MQTTnet delivers a client's messages one at a time) | `WatermarkClock` is not thread-safe and is touched only here |
| Partition workers | `PartitionCount` tasks, one per bounded channel (default 4) | a worker owns its `VehicleTracker`; no locking on tracks |
| Boards | any worker and the REST threads | one lock per `RankedBoard`; never two board locks at once |
| REST handlers | Kestrel pool | read only `Top` of each board; no shared mutable state otherwise |

- Channels: `Channel.CreateBounded` with `FullMode = Wait`, capacity `IngestChannelCapacity`; the reader is single, the writer count is not assumed.
- Backpressure: a full channel blocks `SubmitAsync`, so the MQTT acknowledgement is delayed; the broker absorbs the backlog (ADR-0001).
- MQTT adapter rules from the spike: connect to `127.0.0.1` by default (configurable), attach the message handler **before**
  connecting, manual acknowledgement after `SubmitAsync`, persistent session with the fixed client id `speed-processor`,
  QoS 1, reconnect with exponential backoff, publishes are not needed.
- Shutdown order: stop the MQTT source (unsubscribe, disconnect), then complete the channels and let the workers drain with a
  timeout, then stop the host.

## 6. Configuration

Section `Processor` of `appsettings.json`, overridable by environment variables (`Processor__PartitionCount`); validated at
startup (invalid settings stop the host with a clear message). Durations are `hh:mm:ss` or the word `Infinite`.

| Setting | Default | Notes |
|---|---|---|
| `PartitionCount` | 4 | at least 1 |
| `IngestChannelCapacity` | 10000 | per partition |
| `CameraBoardWindow` | 00:00:02 | greater than 0, or `Infinite` (BR-CFG-01) |
| `GlobalBoardLinger` | 00:00:03 | 0 or more, or `Infinite` |
| `InactivityTimeout` | 00:00:05 | finite, greater than the interval between a vehicle's measurements plus jitter |
| `FutureTimestampTolerance` | 00:01:00 | measurements further ahead of the processor's clock are `InvalidTimestamp`; the metric shows a clock problem |
| `IngestLagWarning` | 00:00:05 | warning log when exceeded |
| `Mqtt:Host` / `Port` / `Topic` / `ClientId` | `127.0.0.1` / 1883 / `highway/telemetry/v1` / `speed-processor` | inside compose the host is the service name `broker` |
| `HeartbeatsEnabled` | true | when false, heartbeats are counted as `Ignored` |

## 7. Observability and health

- **Logs:** structured JSON to the console. Information: lifecycle (start, stop, settings summary), MQTT connected/disconnected.
  Rejections: the first rejection per reason per minute at Warning (with reason, no payload dump), the rest at Debug, so a
  flood cannot flood the log. Warning when ingest lag exceeds `IngestLagWarning`. Never one Information line per measurement.
- **Metrics (`System.Diagnostics.Metrics`):** counters `received`, `accepted`, `rejected{reason}`, `ignored`; gauges tracked
  vehicles, channel depth per partition, ingest lag, seconds since last received message. The plate is never a tag.
- **`GET /api/status`** (readable without any metrics stack, BR-OPS-02): JSON with the same counters and gauges, broker
  connection state and the effective settings.
- **Health:** `GET /health/live` always 200 while the process runs. `GET /health/ready` is 200 once the pipeline workers run;
  an idle input and a disconnected broker do not make it unready (the processor still serves its boards); the broker state is
  part of the response body (BR-API-03).

## 8. Edge cases

| Situation | Behavior |
|---|---|
| Duplicate or reordered measurement | `OutOfSequence`, no state change (BR-ING-02) |
| Camera missing or dead | later cameras still accepted; averages from accepted measurements (AC-34) |
| Vehicle silent longer than `InactivityTimeout` | its global entry expires with the watermark, its track is freed by the sweep; a later message of the plate starts a new track |
| Message with a far-future timestamp | `InvalidTimestamp`, watermarks untouched (BR-ING-01) |
| Processor lags | boards show the data applied so far; ingest lag grows; no data is dropped (ADR-0001) |
| Broker down | reconnect with backoff; boards freeze; snapshot still served |
| Shutdown with queued items | workers drain until the timeout, remaining items are lost (no recovery in tier 2) |
| Heartbeat | validated (camera, timestamp), advances watermarks, no vehicle state |

## 9. Test map

| Area | Level | Scenarios |
|---|---|---|
| `RankedBoard`, rank order, expiry, pruning | unit plus model-based against a naive oracle | AC-11, 12, 14, 15, 16, 17, 20 |
| `WatermarkClock` | unit | AC-21 |
| `VehicleTracker` | unit | AC-05, 06, 07, 08, 10, 13, 34 |
| `MeasurementValidator` | unit | AC-04, 31 |
| `IngestPipeline` (order independence, backpressure, shutdown) | unit with a fake clock and stub boards, plus shuffled interleavings | AC-09, 30 |
| `MessageDecoder` | unit with `docs/contracts/examples` and each reason code | contract |
| `MqttMeasurementSource` | integration against a real broker (E2E project) | ADR-0001 behavior |
| REST endpoint, health, status | component (`WebApplicationFactory`, in-memory source) | AC-22, 23, 24, 29 |
| Whole pipeline | scripted E2E with a test publisher | AC-10 to AC-24 end to end |
| Load | 1,000 messages/s and 10x burst | testing strategy thresholds |

## 10. Decisions the owner should confirm

1. `RankedBoard` owns its lock (thread-safe domain structure) rather than a lock wrapper in Application.
2. Pure `VehicleTracker` in Domain returning board actions; Application only applies them.
3. Heartbeats are implemented now (small: validate, route, advance) and controlled by `HeartbeatsEnabled`.
4. `/api/status` JSON endpoint as the way to read metrics without a stack, next to `System.Diagnostics.Metrics`.
5. Readiness means "pipeline running", broker state informational.
6. Defaults of section 6, especially `FutureTimestampTolerance` of one minute (generous because of Docker Desktop clock drift).
