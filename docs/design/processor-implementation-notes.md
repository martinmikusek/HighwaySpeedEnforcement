# Processor: implementation notes

Deviation log kept during the implementation of `processor-implementation-plan.md`. One section per step: what was done that is
not literally in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason.

## Deviations by step

### Step 0: setup

none. Baseline on `main` (0301d4d): build, format check and 38 tests green.

### Step 1: basic types and rank order

- `HighwayLayout` also exposes `CameraCount` and `IsCamera(int)`; the validator and the board set will need both.
- `RankOrder` is a singleton (`RankOrder.Instance`), it has no state.

- `BoardEntry.MeasurementCount` is **1 on a camera entry**, not 0 as the design says (owner, 2026-10-14): a camera entry is
  exactly one measurement, so 1 is the true value and no sentinel is needed. The REST contract presents the count only on
  global entries, so nothing on the wire changes. A nullable count and a separate entry type for the global board were
  considered and rejected (nullable noise in the mapper; a second entry type would make `RankedBoard` generic for one field).

### Step 2: liveness policy

none. Invalid values throw `ArgumentOutOfRangeException` from the constructor; settings validation in Application (step 7)
reports all errors as messages before the policy is built.

### Step 3: RankedBoard

- `Top(n)` returns a `BoardView` with a `RankedEntry(Rank, Entry)` list; `Count` and `LatestEventTicks` are exposed for tests
  and status. `Update(watermark, entry)` is the combined operation the design allows.
- Pruning of infinite entries uses a fourth structure, a rank-ordered set of the infinite entries only, so that the worst one
  is found in O(log n) (the expiry-ordered set orders infinite entries by plate, not rank).
- **Precondition of pruning (BR-LB-10):** pruning an infinite entry is only sound if the ten dominating infinite entries never
  leave the board. If one of them is later replaced (an `Upsert` of the same plate) or removed, the pruned entry could have
  been presented and `Top` would differ from the naive recomputation. The model-based pruning test therefore never replaces
  or removes an infinite entry. The pipeline satisfies this because plates are never reused (BR-SIM-03); see the open question
  about reordered messages after completion.

### Step 4: watermark clock

- `WatermarkClock` exposes `GlobalTicks` and `CameraTicks(camera)` as nullable values (null before the first observation) so
  that tests and the status view can read them; `Observe` is the only mutation.
- `Stamp` and `StampedMeasurement` share one file (`Stamp.cs`), they are two-line records.

### Step 5: vehicle tracking

- `TrackResult` is a `readonly record struct` with `IsAccepted`, not a class hierarchy, to keep the hot path allocation-free.
  The only rejection reason of the tracker is out of sequence (`TrackResult.OutOfSequence`), so no reason enum is needed in
  Domain; Application maps it to `RejectReason.OutOfSequence`.
- The global action is `BoardEntry? GlobalEntry` (null = `None`), not a separate action type: the design knows only
  `None` and `Upsert(entry)`.
- `Sweep` returns the number of freed tracks (for a debug log line); the design leaves the return type open.

### Step 6: plate hash

- FNV-1a 32-bit over the UTF-16 code units, low byte first, as in the design. Reference values in the test were computed
  independently in PowerShell. `PlateHash.Partition(plate, count)` is a convenience for the pipeline's routing.

### Step 7: validation and settings

- `ProcessorSettings` holds only the processor's own parameters. The MQTT options of design section 6 (`Mqtt:Host`, port,
  topic, client id, `Mqtt:Enabled`) are an Infrastructure concern and get their own options type in step 13/14; Application
  must not know the transport.
- Timestamp validity is `0 < ticks <= DateTime.MaxValue.Ticks` plus the future tolerance; tick 0 (year 0001) is treated as
  "never set" and rejected, as the plan's test list requires.
- `MeasurementValidator` uses the `TryValidate(..., out, out)` shape instead of a result type, to keep the hot path free of
  allocations; `RejectReason` has no `Ignored` member because an ignored heartbeat is not a rejection (it is a separate
  counter on `IIngestMetrics`, step 9).

### Step 8: boards and snapshot

- `TrafficSnapshot` carries `GeneratedAt` as `DateTimeOffset` (what `TimeProvider.GetUtcNow()` returns) and the ten camera
  boards as `BoardView`s indexed by camera order; the camera id is implied by the index and is added by the REST mapper.

### Step 9: pipeline

- **Out-of-sequence measurements advance the boards.** Design section 3 step 6 says "Rejected: count and log". The watermark
  was already raised by the stamping stage (BR-LB-11 counts valid measurements that the sequence rule rejects), so the
  worker also calls `AdvanceTo` on the camera board and the global board with the stamp; otherwise a stream of duplicates
  would leave the boards' `LatestEventUtc` and expiry behind the watermark the rules describe.
- **Order independence is about workers, not about arrival order.** Stamping happens in arrival order (ADR-0003), so
  reordering messages of different plates by more than `InactivityTimeout` changes the orphan decision and therefore the
  result. BR-LB-13 and AC-30 only promise identical boards for any partition count and any worker interleaving, and that
  is what the tests check: partition counts 1, 2, 4, 7 with the same submission order, and 4 workers forced to apply their
  items strictly one partition after the other in several orders through the test hooks `BeforeApply`/`AfterApply`
  (`internal`, exposed with `InternalsVisibleTo` to the unit test project only).
- `WaitUntilIdleAsync()` (completes when every submitted item is applied) is public: tests and the component tests need
  it, and the host can use it for a clean shutdown. It costs one short lock per message.
- Heartbeats are routed to `camera mod PartitionCount` as in the design; they count as received, not as accepted.
- Status gauges are per-partition fields written by the owning worker with `Volatile.Write` and summed on read; the
  partition class is private, so the `CA1051` suppression for its two fields is local and commented.
- Logging uses the `LoggerMessage` source generator (event ids 1000 to 1013). Rejections: first per reason per minute at
  Warning, the rest at Debug; the ingest lag warning is also throttled to one per minute.
- Processing failures inside a worker are logged at Error and the worker continues with the next item (BR-OPS-01); the
  boards stay consistent because every board operation is atomic under its lock.

### Step 10: wire types

- Namespaces `Contracts.Telemetry` (messages, `TelemetryContract` constants, `TelemetryJsonContext`) and `Contracts.Rest`
  (DTOs, `RestJsonContext`), so that the generator and the client each see only their half. `TelemetryContract` also carries
  the client ids and the 1,024-byte payload limit from the contract document, so step 13 and the generator share one source.
- **Every telemetry property is required.** The contexts are positional records with `RespectRequiredConstructorParameters`,
  so a missing property fails deserialization with `JsonException`, which the decoder (step 11) maps to `Malformed`.
  `NumberPlate` is declared `string?`: a JSON `null` is representable and must become `InvalidPlate` by validation, not `Malformed`.
- `Type` is an ordinary required string property, not a System.Text.Json polymorphic discriminator: the decoder resolves it
  itself, because polymorphism could not tell `UnknownType` from `Malformed` and would require the property to come first.
- **REST times.** DTOs use `DateTimeOffset` with `UtcDateTimeOffsetConverter`, which writes the round-trip format with seven
  fractional digits and `Z` (exactly the example file); the default converter writes `+00:00` and trims the fraction. Reading
  accepts any ISO 8601 offset and normalizes to UTC.
- `CameraEntryDto.SpeedKmh` is `float` (the measured value, BR-TEL-01) and `GlobalEntryDto.AverageSpeedKmh` is `double`. The
  mapper (step 16) casts the camera board's `double` score back to `float`, so the wire text equals the measured speed.
- The example files are linked into the unit test project as content under `Contracts/examples`.
- Fixed a nondeterministic test of step 9 (`Status_reports_tracked_vehicles_buffer_depth_newest_event_and_lag`): it read the
  buffer depth before the worker had necessarily dequeued the first item; it now waits for the worker to reach the hook.

### Step 11: decoder

- `MessageDecoder.Decode` returns a `DecodeResult` (`Decoded` with the message, `Malformed`, `UnknownType`, `Ignored`),
  so that the adapter decides what to do with each outcome (`ReportUndecodable`, `Ignored()` counter, submit).
- `Type` is resolved with a forward-only `Utf8JsonReader` pass over the top level (any position, nested objects skipped),
  then the payload is deserialized through the contract context. The pass reads to the end of the payload, so trailing
  content after the object is `Malformed` instead of silently ignored.
- A heartbeat while heartbeats are disabled is `Ignored` without being deserialized: its shape is not checked.

### Step 12: metrics adapter

- `DiagnosticsIngestMetrics` takes `IMeterFactory` (part of the runtime; the host registers it with `AddMetrics`) and a
  `Func<IngestStatus>` for the gauges, because the pipeline depends on the metrics and the status can only be read lazily.
- Instrument names: `processor.ingest.received|accepted|rejected|ignored` (counters), `processor.ingest.tracked_vehicles`,
  `buffer_depth` (tag `partition`), `lag_seconds`, `seconds_since_last_received` (gauges). The two time gauges report
  nothing while there is no data instead of a sentinel value.

### Step 13: MQTT source

- **No unsubscribe on shutdown.** Design section 5 says "stop the MQTT source (unsubscribe, disconnect)". The subscription
  is what makes the broker queue for the persistent session while the processor is down (ADR-0001, spike S3), so the
  source only disconnects cleanly and leaves the subscription in the session. Covered by the E2E test
  `Messages_published_while_the_source_is_down_are_delivered_after_it_returns`.
- `MqttSourceOptions` lives in Infrastructure (host, port, topic, client id, keep-alive, reconnect delays, `Enabled`) with
  its own `Validate()`; the host binds it from the `Mqtt` section (step 14).
- A message whose submission fails with an exception other than cancellation is logged and left unacknowledged, so it
  is retried after a reconnect rather than lost; MQTTnet keeps delivering the following messages.
- The E2E broker (`MosquittoBroker`) binds a fixed free host port so that the address survives a container restart, and
  copies `deploy/mosquitto/mosquitto.conf` into the container instead of bind-mounting it. Tests use unique topics and
  client ids on one shared broker. `Eventually.TrueAsync` polls conditions that only an external process can make true
  (connected after a broker restart); everything else is awaited through the recording sink.
- The E2E project now runs its tests in the default `dotnet test` run when Docker is available; the plan's exclusion of
  E2E from the default run is decided in step 21 together with the CLAUDE.md commands.

### Step 14: settings binding

- The `Processor` section is read by hand (`ProcessorConfiguration`) because the default binder cannot parse the word
  `Infinite`; `Mqtt` uses the default binder. Binding and validation problems are collected into one list and the host
  logs every one at Critical before it throws, so a misconfigured container exits with all errors in its log.
- `FormatDuration` is the inverse of the binding (`Infinite` or `hh:mm:ss`) and is used by the status endpoint.

### Step 15: composition root

- `IMeterFactory` comes from the ASP.NET Core defaults (`AddMetrics` is implicit); the metrics adapter reads the pipeline
  status lazily, which breaks the metrics-to-pipeline dependency cycle.
- Hosted services: `IngestPipelineHostedService` is registered first and the MQTT source second, so the host stops the
  source first and the pipeline then drains for at most 10 seconds (constant in the hosted service, not a setting).
- With `Mqtt:Enabled=false` the MQTT source is not registered at all; the status and readiness bodies then report the
  broker as `Disabled`.

### Step 16: snapshot endpoint

- The 503 before the pipeline runs and the 500 of the exception handler are both RFC 9457 problem details
  (`AddProblemDetails` + `UseExceptionHandler`). The snapshot itself is serialized with the contracts' `RestJsonContext`.

### Step 17: health and status

- Health bodies are small records serialized by the default (reflection) JSON options in camelCase; only the snapshot
  uses the source-generated contract context.
- `DiagnosticsIngestMetrics` keeps plain totals (`Counters`) next to the instruments, because `System.Diagnostics.Metrics`
  counters cannot be read back and BR-OPS-02 wants the numbers without a metrics stack.
- `/api/status` exposes the effective processor settings and the broker address, state and last connection time; the
  `Mqtt` reconnect delays and keep-alive are not repeated there.

### Step 18: scripted E2E

- One collection fixture (`PipelineStack`) holds the Docker network, the broker and the processor image built from the
  real Dockerfile; **every scenario starts its own processor container** (about 2 s each) so that it begins with empty
  boards and can pass its own settings as environment variables. A shared processor would make the scenarios depend on
  each other's leftovers.
- The oracle (`BoardOracle`) is a literal, list-based transcription of the rules; it evaluates liveness lazily against
  the current watermarks, which gives the same result as the processor's eager expiry because watermarks never decrease.
- "The processor applied everything" is observed through `/api/status`: received equals the number sent and accepted plus
  rejected equals received. Snapshots are compared with the oracle only after that point, so worker interleaving is not
  visible.
- AC-18 (freeze) is the one scenario with a wall-clock wait: it is the scenario's subject (time passes, nothing changes),
  not a wait for a result. The wait is one second longer than the inactivity timeout.
- Scenario timestamps are one hour in the past of the processor clock; the future-timestamp tolerance does not interfere
  and nothing depends on the lag.
- The Dockerfile path must use forward slashes for the Docker API, even on Windows.

### Step 19: load test

- The load test lives in the E2E project with both traits `Category=E2E` and `Category=Load`; the project excludes
  `Category=Load` by default through `TestingPlatformCommandLineArguments`, and the explicit filter in `CLAUDE.md` runs it.
  A separate project would have forced a second copy of the container fixtures.
- The steady phase is `HSE_LOAD_SECONDS` long (default 300) so that the harness can be smoke-tested in half a minute.
- The test publisher reproduces the generator's tick (100 new vehicles, every active vehicle advances one camera) and
  publishes each tick in batches of 100 concurrent QoS 1 publishes; a batch completes before the next starts, so the
  order of one plate's messages holds (the spike measured 0.74 s per tick for strictly sequential publishes, too close
  to the 1 s budget).
- Memory is read through the Docker stats API (`ContainerMemory`), with the same endpoint Testcontainers uses. The leak
  criterion compares the mean of the last steady minute with the mean of the first minute after warm-up (at most +25 %
  and 20 MiB), because a GC-managed process never shows a perfectly flat line.
- The first harness sampled the lag with a timer phase-locked to the ticks (right before the next tick), which showed a
  constant lag of about 1.0 s with an empty buffer: an artifact of sampling, not of the processor. The lag is now read
  right after each tick was published, which measures the distance to the head of the stream.
- **Measured on 2026-10-14** (Windows 11, Docker Desktop, 16 cores; steady phase 300 ticks, 20 ticks warm-up):

  | Measure | Value |
  |---|---|
  | Messages sent / received / accepted / rejected | 315,500 / 315,500 / 315,500 / 0 |
  | Ingest lag after a tick, p50 / p99 / max | 0.466 s / 0.597 s / 0.632 s (the publisher needs about 0.45 s per tick) |
  | Tracked vehicles | 800 to 900 |
  | Buffer depth after a tick | at most 1 |
  | Memory (container) | first minute 62.6 MiB, last minute 60.8 MiB, peak 75.6 MiB |
  | Burst of 10,000 messages | published in 0.79 s; lag right after 0.79 s with an empty buffer; back under 1 s on every recovery tick |

  The processor is never the bottleneck at this load: the lag is the publisher's own duration of a tick.

### Step 20: traceability script

- `tools/ac-coverage.ps1` reads the scenario ids from the markdown table and the traits from `tests/**/*.cs`; ids passed
  in `-ProcessorScenarios` are grouped as processor, the rest as generator / client. It reports and never fails.
- Result after step 18: every processor scenario has at least one trait; uncovered are AC-01..03 (generator) and
  AC-25..28, AC-32 (client), which belong to the other components.

### Step 21: wrap-up

- `docker compose up -d --build` started a processor that could not reach the broker: the compose file did not set
  `Mqtt__Host=broker` (design section 6 says the host is the service name inside compose). Added; verified that
  `/health/ready` reports the broker as connected and the snapshot is served.
- The default `dotnet test --solution` run includes the E2E project (needs Docker, about 1.5 minutes); only the load test
  is excluded. Final count: 250 tests green (unit 216, component 15, E2E 19) plus the load test on request.

## Open questions

- **OQ-1 (step 3, BR-LB-04 / BR-LB-10), closed 2026-10-14 by AMB-10.** A plate that reappears after completion (reordered
  late messages, or in the real world a second passage) starts a new track and its third measurement replaces the
  completed global entry with an in-flight one. Decision: this is the intended "newest passage wins" behavior, no code
  change; the pruning precondition above is documented in BR-LB-10 and a passage identity is out of scope. Covered by the
  test `Second_passage_after_completion_replaces_the_completed_global_entry`.

### Status page (added 2026-10-14, owner's request)

`GET /ui` serves one embedded, self-contained HTML page (`Processor.Api/Status/StatusPage.html`, no external scripts, styles or fonts; mint palette
and parody logo as in the client) that polls `/api/status` once per second and draws throughput (change of the accepted counter), ingest lag with the warning
threshold, the ingest buffer with its total capacity, tracked vehicles, the rejected counters and the settings. It adds no data and no endpoint other than the
page, needs no package, and changes no contract (`/api/status` is unchanged). The page states that the ingest buffer is the processor's own partition queues,
not the broker's queue. Tested by two component tests (served as HTML, loads nothing from outside); the drawing code is checked by a headless-browser screenshot only.

Status page follow-ups (2026-10-14): the buffer chart scales its axis to the data (the capacity line is drawn only when the queue gets near it); a measurement with a queue capacity of 50 and
one partition at about 6,000 messages per second gave a buffer of at most 3 items while the ingest lag grew to 16 s on a loaded machine: the MQTT receive path (decode, submit,
acknowledge one message at a time) is the slower part, so under overload the backlog waits in the broker (invisible to the processor) and the buffer stays near zero. Noted on the page and in the README.

### Latency per message (owner's request, 2026-10-14)

The ingest lag (wall-clock time minus the timestamp of the newest processed event) saw-tooths between the one-second bursts of the generator, so it says little about the real delay.
The pipeline now also measures, for every applied message (measurements and heartbeats, not rejected ones), `now - ownEventTime` (never negative: a timestamp ahead of the clock counts as zero),
keeps count, sum and maximum in ten one-second buckets (`LatencyWindow`, one short lock) and reports the average and maximum of the last ten seconds in `IngestStatus.Latency` and `/api/status`
(`latencyAvgSeconds`, `latencyMaxSeconds`, `latencySamples`), and records a histogram `processor.ingest.latency` (seconds, bucket boundaries 0.05 to 10) through the new port method
`IIngestMetrics.Latency`. BR-OPS-02 names it; the status page (`/ui`) shows it as a card and a chart (average and maximum), keeps the old lag as "age of the newest event", and scales the
latency and age charts to the data. Measured on the local stack at 1,000 messages per second: latency about 0.1 to 0.15 s on average and 0.3 to 0.45 s at the maximum (it includes the generator
publishing the tick's messages), while the age of the newest event ran between 0 and 1 s.
