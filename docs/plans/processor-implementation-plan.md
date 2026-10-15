# Processor: implementation plan

**Status:** Ready for implementation. Design: `docs/design/processor.md` (accepted by the owner on 2026-10-14).
This document is written for an implementer who starts without any conversation history. Read it completely before writing code.

## 1. Goal and boundaries

Implement the processor in the four projects `HighwaySpeedEnforcement.Processor.{Domain,Application,Infrastructure,Api}` (plus the
needed parts of `HighwaySpeedEnforcement.Processor.Contracts`) and the tests listed per step, so that the scenarios `AC-01..AC-34` that concern
the processor pass and the system is runnable with `docker compose up`.

Out of scope: the generator and the client (separate plans), durable recovery (tier 3), anything not in the design.

## 2. Read first (in this order)

1. `CLAUDE.md` (rules, commands, code style, layering).
2. `docs/design/processor.md` (the design you implement).
3. `docs/domain/business-rules.md`, `glossary.md`, `domain-model.md`, `acceptance-scenarios.md`.
4. `docs/contracts/telemetry-messages.md`, `rest-api.md` and `docs/contracts/examples/*.json`.
5. ADR-0001, 0003, 0004; `docs/assignment-ambiguities.md` (why the rules are what they are).
6. `spikes/mqtt/FINDINGS.md` (MQTT behavior measured on this machine) and `spikes/mqtt/MqttSpike/Program.cs` (working MQTTnet 5 code).

## 3. Rules that apply to every step

- **Authority.** The documents above are decisions. Do not change `docs/adr/`, `docs/domain/`, `docs/contracts/` or the design to make
  code easier. If a document is wrong, ambiguous or contradicts another, or a step cannot be done as written: **stop that step,
  write the problem into `docs/design/processor-implementation-notes.md` (section "Open questions") and continue with an
  independent step, or finish and report.** Do not guess.
- **Deviation log.** Create `docs/design/processor-implementation-notes.md` at step 0. After each step add what you did that is
  not literally in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason.
  No deviations: write "none".
- **Style and layering** are enforced by the build (`CLAUDE.md`): Allman braces, always braces, file-scoped namespaces, `_camelCase`
  private fields, nullable on, warnings are errors. Never suppress a warning without a comment giving the reason. The architecture
  tests must stay green; allowed packages per layer are in `DependencyRuleTests` (Domain: none; Application: only
  `Microsoft.Extensions.*.Abstractions`; do not widen the rules without writing it into the notes).
- **Dependencies.** Allowed new packages: `MQTTnet` (Infrastructure), `Microsoft.Extensions.Logging.Abstractions` (Application),
  `Microsoft.Extensions.Hosting.Abstractions` (Infrastructure, Api), `Microsoft.Extensions.TimeProvider.Testing`, `Testcontainers`
  (tests). Add with `dotnet add package`; versions land in `Directory.Packages.props`. Nothing else without a note.
- **Determinism.** No `Thread.Sleep`/`Task.Delay` to wait for results in tests. Use `FakeTimeProvider`, channels completing, or
  awaiting the observable effect. Model-based tests use a seeded `Random` and print the seed in the failure message.
- **Test conventions.** xUnit v3; `[Trait("AC","AC-10")]` and `[Trait("BR","BR-LB-03")]` on every test that covers a scenario or
  rule (several traits allowed); `[Trait("Category","E2E")]` for tests that need Docker, `[Trait("Category","Load")]` for load
  tests. Test method names `Subject_condition_result`. Pass `TestContext.Current.CancellationToken` to async calls (analyzer).
- **Wire contracts** are exactly `docs/contracts/`; the example JSON files are the approved files for contract tests.
- **Spike code** is a reference, not a dependency: copy ideas, never reference `spikes/` from `src/`.
- **Comments** only where the reason is not obvious from the code; no restating comments.

## 4. Definition of done for a step

All of: `dotnet build Yunex.HighwaySpeedEnforcement.slnx` has no warnings or errors;
`dotnet format Yunex.HighwaySpeedEnforcement.slnx --verify-no-changes` is clean;
`dotnet test --solution Yunex.HighwaySpeedEnforcement.slnx` passes (E2E and Load excluded until step 19, see below); the deviation
log is updated; one commit exists for the step (Conventional Commits, English, for example `feat(processor): add RankedBoard`;
follow the commit attribution rules of your environment).

Git: work on the branch `feature/processor` created from `main`; commit after each step; push the branch after each phase; do not
push to `main` and do not merge.

## 5. Steps

Each step lists deliverables, tests (with the scenarios they cover) and notes. The order is a dependency order: do not skip ahead.

### Phase 0: setup

**Step 0.** Create the branch and `docs/design/processor-implementation-notes.md` (headings: Deviations by step, Open questions).
Verify that build, format check and tests are green before any change. Add `Microsoft.Extensions.TimeProvider.Testing` to the
unit and component test projects when first needed.

### Phase 1: Domain (`Processor.Domain`, tests in `HighwaySpeedEnforcement.Unit.Tests/Processor/Domain`)

Delete `AssemblyMarker.cs` of a project when it receives real types. Domain has no package references.

**Step 1: basic types and rank order.** `HighwayLayout`, `Measurement`, `BoardEntry`, `RankOrder`.
Tests: ordering by score descending, equal scores by plate ascending ordinal and case-sensitive (`"AA9" < "AB1" < "aa9"`);
comparer is a total order and consistent (AC-15, BR-LB-05).

**Step 2: liveness policy.** `LivenessPolicy` with `CameraExpiry`, `InFlightExpiry`, `CompletedExpiry`.
Tests: sums; infinite gives `long.MaxValue`; saturation on overflow; linger 0 gives `ExpiresAt == timestamp`; invalid
configurations rejected (window must be greater than 0, timeout finite) (BR-CFG-01, BR-LB-07, BR-LB-08).

**Step 3: `RankedBoard`** per design section 4.2. Thread-safe, owns its lock.
Tests (unit): empty board; `Upsert` replaces a plate's entry; an already expired entry is not inserted and removes the plate's
old entry; `AdvanceTo` expires by `ExpiresAt <= watermark`, never lowers the watermark; `Remove`; `Top(n)` ranks 1..n and returns the
watermark; after expiry the next best candidate takes the place (AC-11, 12, 17); linger 0 case (AC-14); infinite entries are limited
to the best 10 and never change `Top` (AC-14, AC-20, BR-LB-10); late measurement with older timestamp cannot lower the watermark (AC-21).
Tests (model-based, BR-LB-09): a naive oracle holds all entries in a list and recomputes the live top 10 after every operation; run
at least 200 seeded random sequences of `Upsert/Remove/AdvanceTo` (200 operations each, 30 plates, scores with deliberate ties)
and compare `Top(10)` and the entry set after each operation.
Test (concurrency, BR-LB-13): the same random operation set applied by 1 thread and by 4 threads, each plate's operations kept in order,
gives identical final boards; repeat with 20 seeds.

**Step 4: watermark clock.** `Stamp`, `StampedMeasurement`, `WatermarkClock`.
Tests: per-camera and global maxima, older timestamps leave maxima unchanged, stamps returned reflect the state after the observation (BR-LB-11, AC-21).

**Step 5: vehicle tracking.** `VehicleTrack`, `TrackResult`, `VehicleTracker`, per design section 4.1.
Tests with a builder for stamped measurements: any start camera; strictly increasing cameras accepted with gaps; equal or lower camera
rejected `OutOfSequence` and state unchanged (AC-05, 06); camera 10 completes and removes the track, repeated camera 10 creates a
one-measurement track that completes at once and yields no global action (AC-07); no global action before the third accepted
measurement (AC-10); average is sum of accepted speeds over count after gaps (AC-34); decreasing average produces an `Upsert`
with the lower score (AC-11); orphan: a message whose stamp is `InactivityTimeout` or more past the track's last timestamp starts a
new track (AC-08); `Sweep` frees only silent tracks and does not change results of later messages; `Count` (BR-ING-02, 04, 05, 06).

**Step 6: plate hash.** `PlateHash`.
Tests: same plate gives the same value on repeated calls, known expected values for a few plates (fixes the algorithm), values
spread over 4 partitions within a sane tolerance for 10,000 generated plates.

*Push the branch.*

### Phase 2: Application (`Processor.Application`, tests in `.../Processor/Application`)

Add `Microsoft.Extensions.Logging.Abstractions`. The unit test project may reference Domain and Application only for this phase.

**Step 7: validation and settings.** `ProcessorSettings` (design section 6, with validation method returning a list of errors),
`RejectReason`, `IncomingMessage` (`IncomingMeasurement`, `IncomingHeartbeat`), `MeasurementValidator`.
Tests with `FakeTimeProvider`: each invalid case yields its reason (null, empty, blank, 17-character plate; camera 0 and 11; speed
negative, NaN, infinity; ticks 0, negative, above `DateTime.MaxValue.Ticks`, beyond the future tolerance) (AC-04, AC-31);
precedence when several fields are invalid (plate, camera, speed, timestamp); valid boundary values accepted; heartbeat validated
for camera and timestamp only; settings validation messages (BR-ING-01, BR-CFG-01, BR-CFG-03).

**Step 8: boards and snapshot.** `BoardSet`, `TrafficSnapshot`, `SnapshotService`.
Tests: ten camera boards plus global created with the right policy; empty snapshot has ten empty boards, null latest event times,
`GeneratedAt` from the fake clock (AC-22); snapshot is a detached copy (mutating the boards afterwards does not change it)
(BR-API-02).

**Step 9: pipeline.** `IIngestMetrics`, `IngestStatus`, `IngestPipeline` (implements `IIngestSink`): validation, stamping, routing by
`PlateHash` / camera, bounded channels per partition, workers, sweep (design section 3, step 6), `StopAsync` with drain timeout.
Make it testable: partition count and channel capacity come from settings; no static state; time only from `TimeProvider`.
Tests: accepted measurements reach the boards; one plate's messages are applied in submission order (AC-09); results identical for
`PartitionCount` 1, 2, 4 and for shuffled interleavings of different plates' messages (AC-30, BR-LB-13), implemented by submitting from
one task and completing the workers in different orders using a controllable worker gate; rejected messages counted by reason and not
applied (AC-29, BR-ING-07); `SubmitAsync` waits while a partition channel is full and completes when the worker makes room
(backpressure); heartbeat advances watermarks and expires entries without vehicle state (AC-33); sweep runs once per second of event
time and frees memory only; `StopAsync` drains queued items; a far-future timestamp is rejected and expires nothing (AC-31);
`IngestStatus` values (tracked vehicles, buffer depth, newest event time).

*Push the branch.*

### Phase 3: Contracts (`HighwaySpeedEnforcement.Processor.Contracts`, tests in `.../Contracts`)

**Step 10: wire types.** `MeasurementMessage`, `HeartbeatMessage` (PascalCase properties exactly as in `telemetry-messages.md`),
topic constant `highway/telemetry/v1`, REST DTOs `TrafficSnapshotDto`, `CameraBoardDto`, `CameraEntryDto`, `GlobalBoardDto`,
`GlobalEntryDto` per `rest-api.md`, source-generated `JsonSerializerContext` for both (camelCase for the REST context).
Contracts has no package references.
Tests: contract tests that read `docs/contracts/examples/*.json` (add them to the test project as linked content copied to the output
directory) and assert they deserialize into the DTOs and serialize back to equivalent JSON (property names, ISO 8601 with `Z`, null
`latestEventUtc`).

### Phase 4: Infrastructure (`Processor.Infrastructure`, tests in `.../Processor/Infrastructure`)

Add `MQTTnet` and `Microsoft.Extensions.Hosting.Abstractions`.

**Step 11: decoder.** `MessageDecoder` per design.
Tests: the two example files; every reason (`Malformed`: not JSON, not an object, missing `Type`, missing or mistyped property,
payload over 1,024 bytes; `UnknownType`); extra properties ignored; heartbeat when disabled is reported as ignored; mapping of
the decoded values into `IncomingMeasurement` (no semantic validation here, that is Application).

**Step 12: metrics adapter.** `DiagnosticsIngestMetrics`.
Tests with `MeterListener`: counters with the `reason` tag, gauges read `IngestStatus`; the plate never appears as a tag.

**Step 13: MQTT source.** `MqttMeasurementSource` per design section 5 and the spike findings.
Tests (E2E project, `Category=E2E`, Testcontainers Mosquitto started with `deploy/mosquitto/mosquitto.conf`, a test publisher using
MQTTnet): messages arrive in order into a recording `IIngestSink`; acknowledgement happens only after `SubmitAsync` completes (a sink
that blocks keeps the message unacknowledged); redelivery after a connection loss with a persistent session is handled; the source
reconnects after the broker container restarts; undecodable payloads are reported and do not stop the source; shutdown disconnects cleanly.
Remove the temporary `--ignore-exit-code 8` from `HighwaySpeedEnforcement.E2E.Tests.csproj` when this step adds the first test.

*Push the branch.*

### Phase 5: Api (`Processor.Api`, tests in `HighwaySpeedEnforcement.Component.Tests`)

**Step 14: settings binding.** Section `Processor`, durations as `hh:mm:ss` or `Infinite`, validation at startup (the host does not start
with invalid settings and logs every error). Add `Mqtt:Enabled` (default true) so tests can run without a broker.
Tests: binding of defaults, of overrides, of `Infinite`, validation failures.

**Step 15: composition root.** Register settings, `TimeProvider.System`, `BoardSet`, `IngestPipeline` (as `IIngestSink`),
`SnapshotService`, metrics, the MQTT source (when enabled), hosted services with the shutdown order of design section 5, JSON console
logging with the rules of design section 7.

**Step 16: snapshot endpoint.** `GET /api/traffic-snapshot` with `SnapshotMapper`, `Cache-Control: no-store`, problem details: 503
before the pipeline runs, 500 on unexpected failure.
Tests (component, in-memory pipeline, `Mqtt:Enabled=false`): empty snapshot shape (AC-22); entries appear after submitting messages
through `IIngestSink`; ranks, ordering, camelCase and ISO 8601 text; `latestEventUtc` and `measuredAtUtc`; JSON matches the shape of
the example file; concurrent requests during ingestion return valid, consistently ordered boards (AC-23).

**Step 17: health and status.** `GET /health/live`, `GET /health/ready` (pipeline running; broker state in the body, not decisive),
`GET /api/status` (counters, gauges, broker state, effective settings).
Tests: liveness and readiness with an idle input (AC-24); readiness 503 before start; status values after some accepted and
rejected messages (AC-29, BR-OPS-02).

*Push the branch.*

### Phase 6: whole-system tests

**Step 18: scripted E2E** (`HighwaySpeedEnforcement.E2E.Tests`, `Category=E2E`). Fixture: Testcontainers broker (tuned configuration) and
the processor container built from `src/Processor/HighwaySpeedEnforcement.Processor.Api/Dockerfile`; a test publisher (MQTTnet) that sends
exactly the measurements a scenario needs with synthetic timestamps based on the processor clock; REST polling with a retry helper
(no fixed sleeps); an independent oracle that computes the expected boards from the same measurements.
Scenarios: AC-10, 11, 12, 15, 16, 17, 18 (freeze), 19, 22, 24, 34, and one run with duplicate delivery (AC-05). Container
settings for short windows are passed as environment variables (`Processor__CameraBoardWindow=00:00:01` and so on).

**Step 19: load tests** (`Category=Load`, excluded from the default run by a filter documented in `CLAUDE.md`). 1,000 messages/s for
5 minutes and a 10x burst against the processor container; assert the thresholds of `docs/testing-strategy.md` (ingest lag p99 under 1 s,
tracked vehicles stable near 900, zero unexpected rejections, no memory growth after warm-up); print measurements for the notes.

**Step 20: traceability script** `tools/ac-coverage.ps1`: lists scenario ids from `docs/domain/acceptance-scenarios.md` that no test
covers through `[Trait("AC", ...)]`, grouped by processor / generator / client relevance when the id is in a processor list given as a
parameter. It reports, it does not fail.

**Step 21: wrap-up.** Update `CLAUDE.md` (commands for E2E and Load, status of the processor), make sure `docker compose up -d --build`
starts a working processor, run the full test suite including E2E, and write the final report (section 7).

*Push the branch.*

## 6. Guidance on details the design leaves open

- **Class shapes.** Prefer immutable records and `readonly record struct`; keep the hot path (`Process`, `Upsert`) free of avoidable
  allocations but do not optimize before the load test (step 19) says it is necessary.
- **`RankedBoard` internals.** `SortedSet<BoardEntry>` with `RankOrder`, `SortedSet<(long ExpiresAt, string Plate)>`, a `Dictionary` by
  plate; `System.Threading.Lock`. Enumerating the first n elements of a `SortedSet` is O(n + log m).
- **Lock hygiene.** Never call out of a board while holding its lock; never hold two board locks at once.
- **Option infinity.** Represent as `Timeout.InfiniteTimeSpan` in settings and `LivenessPolicy`.
- **Logging.** `ILogger` with message templates (no string interpolation), event ids for the lifecycle messages.
- **MQTT client.** MQTTnet 5, MQTT 3.1.1, QoS 1, clean session false, `AutoAcknowledge = false` with `AcknowledgeAsync`, handler attached
  before connect, keep alive 30 s, reconnect loop with exponential backoff capped at 10 s.

## 7. Final report (end of step 21)

A short message to the owner containing: steps completed and steps skipped with the reason; test counts per project; the list of
deviations and open questions from the notes file; measured results of the load test; anything in the documents that turned out to be
wrong or unclear; the exact commands to run the system and the tests.
