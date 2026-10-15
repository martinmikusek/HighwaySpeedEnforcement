# Generator: implementation plan

**Status:** Ready for implementation. Design: `docs/design/generator.md` (accepted by the owner on 2026-10-14).
This document is written for an implementer who starts without any conversation history. Read it completely before writing code.

## 1. Goal and boundaries

Implement the generator in `HighwaySpeedEnforcement.Generator.Domain`, `HighwaySpeedEnforcement.Generator.Infrastructure` and the host
`HighwaySpeedEnforcement.Generator` (all under `src/Generator/`), with the tests listed per step, so that the scenarios `AC-01`, `AC-02`,
`AC-03` pass, the publishing side of `AC-33` and `AC-34` works (heartbeats, a dead camera), and the generator runs against the broker of
`docker compose up`.

Out of scope: the processor and the client (separate plans), the processor's wire types (they are owned by the processor, see the gate in
section 3), durable anything beyond the plate state file, a metrics endpoint, anything not in the design.

## 2. Read first (in this order)

1. `CLAUDE.md` (rules, commands, code style, layering).
2. `docs/design/generator.md` (the design you implement).
3. `docs/domain/business-rules.md` (`BR-SIM-*`, `BR-TEL-*`), `glossary.md`, `acceptance-scenarios.md` (`AC-01..03`, `AC-33`, `AC-34`).
4. `docs/contracts/telemetry-messages.md` and `docs/contracts/examples/measurement.json`, `heartbeat.json`.
5. ADR-0001 (transport), ADR-0004 (layers and folders), `docs/testing-strategy.md`; AMB-03, AMB-08 in `docs/assignment-ambiguities.md`.
6. `spikes/mqtt/FINDINGS.md` and `spikes/mqtt/MqttSpike/Program.cs` (working MQTTnet 5 code, measured behavior).
7. `docs/design/processor-implementation-notes.md` (on the processor branch, after the merge of step 0): conventions the processor
   implementer settled on. Follow them where they apply.

## 3. Rules that apply to every step

- **Authority.** The documents above are decisions. Do not change `docs/adr/`, `docs/domain/`, `docs/contracts/` or the design to make
  code easier. If a document is wrong, ambiguous or contradicts another, or a step cannot be done as written: **stop that step,
  write the problem into `docs/design/generator-implementation-notes.md` (section "Open questions") and continue with an independent
  step, or finish and report.** Do not guess.
- **Deviation log.** Create `docs/design/generator-implementation-notes.md` at step 0. After each step add what you did that is not
  literally in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason. No
  deviations: write "none".
- **Style and layering** are enforced by the build (`CLAUDE.md`): Allman braces, always braces, file-scoped namespaces, `_camelCase`
  private fields, nullable on, warnings are errors. Never suppress a warning without a comment giving the reason. The architecture
  tests must stay green and unchanged (`DependencyRuleTests`: Generator.Domain has no packages and no project references; the other
  generator projects may use any package). Do not widen the rules.
- **Domain purity.** `Generator.Domain` does no I/O and reads no clock: time arrives as a parameter or through `TimeProvider`, randomness
  only through `DeterministicRandom`. No `System.Random`, no `DateTime.UtcNow`, no `Guid.NewGuid` in Domain.
- **Dependencies.** Allowed new packages: `MQTTnet` (Infrastructure), `Microsoft.Extensions.Hosting` and
  `Microsoft.Extensions.Logging.Abstractions` (host and Infrastructure as needed), `Microsoft.Extensions.TimeProvider.Testing`,
  `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`) and `Testcontainers` (tests). Add with `dotnet add package`; versions land in
  `Directory.Packages.props` (central). Nothing else without a note. `Microsoft.Extensions.TimeProvider.Testing` may already be there.
- **Determinism.** No `Thread.Sleep`/`Task.Delay` to wait for results in tests. Use `FakeTimeProvider`, tasks completing, or awaiting
  the observable effect. Statistical tests use a fixed seed (they are deterministic, not flaky) and print the seed in the failure message.
- **Test conventions.** xUnit v3; `[Trait("AC","AC-01")]` and `[Trait("BR","BR-SIM-04")]` on every test that covers a scenario or
  rule (several traits allowed); `[Trait("Category","E2E")]` for tests that need Docker. Test method names `Subject_condition_result`.
  Pass `TestContext.Current.CancellationToken` to async calls (analyzer). Tests live in `HighwaySpeedEnforcement.Unit.Tests/Generator/...`
  (Domain, Infrastructure, Host) and `HighwaySpeedEnforcement.E2E.Tests/Generator/...`; add the project references the tests need
  (the host project `HighwaySpeedEnforcement.Generator` for the runner and settings tests).
- **Wire contracts belong to the processor.** Messages are encoded with the types of `HighwaySpeedEnforcement.Processor.Contracts`
  (ADR-0004). **Never create or change wire types or JSON contexts yourself.** If a type you need is missing, see the gate below.
- **Spike code** is a reference, not a dependency: copy ideas, never reference `spikes/` from `src/`.
- **Comments** only where the reason is not obvious from the code; no restating comments.

### Gate: wire types of the processor

Steps 9 and 10 need `MeasurementMessage`, `HeartbeatMessage`, the telemetry `JsonSerializerContext` and the topic constant from
`Processor.Contracts`. The processor implementer delivers them in its Phase 3 (step 10 of `processor-implementation-plan.md`) on branch
`feature/processor`, in parallel to your work. Before step 9: `git fetch`, `git merge origin/feature/processor`. If the types are
not there yet, **do not write them**: record it in the notes, finish steps 1 to 8, and report (that is a normal stopping point). Use
the types exactly as they are; if their shape makes the generator awkward, write down what you would change in "Open questions".

## 4. Definition of done for a step

All of: `dotnet build Yunex.HighwaySpeedEnforcement.slnx` has no warnings or errors;
`dotnet format Yunex.HighwaySpeedEnforcement.slnx --verify-no-changes` is clean;
`dotnet test --solution Yunex.HighwaySpeedEnforcement.slnx` passes (E2E excluded until step 10, see below); the deviation log is updated;
one commit exists for the step (Conventional Commits, English, for example `feat(generator): add PlatePermutation`; follow the commit
attribution rules of your environment).

**Git.** Work in your own worktree on the branch `feature/generator`, based on the processor branch (it carries the amended
folder layout and `Processor.Contracts` of ADR-0004, which `main` does not have yet):

```
git fetch origin
git worktree add ../Yunex.HighwaySpeedEnforcement-generator -b feature/generator origin/feature/processor
cd ../Yunex.HighwaySpeedEnforcement-generator
git merge --no-edit origin/main     # brings this plan and the generator design
```

Commit after each step; push the branch (`git push -u origin feature/generator`) after each phase; do not push to `main` or to
`feature/processor`, do not merge into them. Another implementer works on `feature/processor` at the same time: **do not edit
files under `src/Processor/`** and keep edits of shared files (`Directory.Packages.props`, `CLAUDE.md`, the test `.csproj` files,
the `.slnx`) minimal and additive so that the later merge is trivial.

## 5. Steps

Each step lists deliverables, tests (with the scenarios they cover) and notes. The order is a dependency order: do not skip ahead.
Delete `AssemblyMarker.cs` of a project when it receives real types.

### Phase 0: setup

**Step 0.** Create the worktree and branch as in section 4 and `docs/design/generator-implementation-notes.md` (headings: Deviations by
step, Open questions). Verify that build, format check and tests are green before any change.

### Phase 1: Domain (`Generator.Domain`, tests in `HighwaySpeedEnforcement.Unit.Tests/Generator/Domain`)

Domain has no package references.

**Step 1: `DeterministicRandom`.** SplitMix64: state `ulong`, constants `0x9E3779B97F4A7C15`, `0xBF58476D1CE4E5B9`, `0x94D049BB133111EB`,
shifts 30, 27, 31. API: `NextUInt64()`, `NextDouble()` in [0, 1) from the top 53 bits, `NextInt(int maxExclusive)` without modulo bias,
`NextGaussian()` (Box-Muller; guard against `log(0)`), `NextGaussian(mean, stdDev)`.
Tests: pinned outputs for fixed seeds (compute the expected values independently, for example in PowerShell with `[uint64]` arithmetic,
before pinning; for seed 0 the published first output is `0xE220A8397B1DCDAF`, verify it); same seed gives the same sequence, different
seeds differ; `NextDouble` stays in [0, 1); `NextInt` stays in range and a 100,000 draw over 10 buckets is roughly uniform (±10 %);
`NextGaussian` over 100,000 draws has mean near 0 (±0.02) and standard deviation near 1 (±0.02), never NaN or infinity.

**Step 2: `PlateFormat`.** `Size` = 40,320,000, `Decode(long index)` per design section 4: index is a mixed-radix number, least
significant part first: number `0000..9999` (radix 10,000), then the third character (radix 32, alphabet `0123456789ABCDEFHIJKLMNPRSTUVXYZ`,
that is digits then letters without `G O Q W`), then the region letter (radix 14, `ASCPKULHEJBMZT`), then the series digit (radix 9,
`1`..`9`); the plate is `"{series}{region}{third} {number:D4}"`, 8 characters.
Tests: `Size` equals the product of the radixes; fixed expected plates: `Decode(0)` is `1A0 0000`, `Decode(1)` is `1A0 0001`,
`Decode(9999)` is `1A0 9999`, `Decode(10000)` is `1A1 0000`, `Decode(Size - 1)` is `9TZ 9999`; every plate of 200,000 sampled indices matches the regex
`^[1-9][ASCPKULHEJBMZT][0-9A-FHIJKLMNPRSTUVXYZ] [0-9]{4}$`; the first 100,000 indices decode to distinct plates; indices outside
`[0, Size)` throw `ArgumentOutOfRangeException` (BR-SIM-03, AMB-08).

**Step 3: `PlatePermutation` and `PlateState`.** `PlateState(long A, long B, long NextIndex)`. `PlatePermutation(long size, PlateState state)`
validates `gcd(A, size) = 1`, `0 <= B < size`, `0 <= NextIndex < size`; `static FromSeed(long size, DeterministicRandom random)` draws `A` in
`[1, size)` (retry until coprime) and `B` in `[0, size)`; `long Next()` returns `(A * i + B) mod size` for `i = NextIndex`, then advances `i`
(wrapping to 0 and incrementing `WrapCount`); `State` returns the current `PlateState`. Products fit in `long` for sizes up to 2^31.
Tests: bijection (all outputs distinct and covering `[0, size)`) for size 1,000 across several `(A, B)`; for the real size the first
1,000,000 outputs are distinct; `FromSeed` is deterministic per seed and always yields a coprime `A` (including sizes with many prime factors);
invalid states rejected; wrap: after `size` calls the sequence repeats from the start and `WrapCount` is 1; resuming a permutation from
`State` produces exactly the continuation of the original (BR-SIM-03, AC-03); consecutive outputs look unrelated (the region letter of
14,000 consecutive plates is spread over all 14 regions, each within ±30 % of the mean).

**Step 4: `SpeedModel`.** `SpeedOptions` (`BaselineKmh` 130, `DesiredStdDev` 15, `StepStdDev` 3, constants for the 0.3 pull, the
desired-speed offset of -5, the clamp 60 to 200 and the 0.1 rounding, as named constants with the design's values). `SpeedModel` offers
`double DrawDesired(DeterministicRandom)` and `float Next(float previous, double desired, DeterministicRandom)` per design section 4
(`v_k = v_(k-1) + 0.3 * (v* - v_(k-1)) + N(0, StepStdDev)`, at least 0, rounded to 0.1 through `double` before the cast to `float`).
Tests: results are multiples of 0.1; desired speeds stay within the clamp; mean of 10,000 desired speeds within ±1 of baseline minus 5
and standard deviation within ±1.5 of 15; with `StepStdDev` 0 the step is exactly the pull (100 toward 140 gives 112.0); over 10,000
vehicles the standard deviation of consecutive differences is between 2.5 and 4.5, the largest absolute difference stays below 20 and no
speed strays more than 25 km/h from its vehicle's desired speed (the mean reversion keeps each vehicle's speeds close to its own level);
never negative; among 1,000 first-camera speeds at least one equal pair exists (ties occur, BR-LB-05, BR-SIM-05).

**Step 5: `Simulation` and its types.** `Measurement(string NumberPlate, long TimestampTicks, float CurrentSpeed, int CameraId)`;
`SimulationOptions` (`VehiclesPerTick`, `SpawnUntilTick`, `DisabledCameras`, `HeartbeatsEnabled`, `SpeedOptions`); `TickOutput` (tick
number, timestamp ticks, measurements, heartbeat cameras, spawned, removed and active counts); `IMeasurementPublisher : IAsyncDisposable`;
`PlateState` (step 3) and `IPlateStateStore` (`PlateState? Load()`, `void Save(PlateState)`). `Simulation(SimulationOptions,
PlatePermutation, DeterministicRandom)` and `TickOutput Tick(long timestampTicks)`; tick numbers start at 1. Per tick: spawn
`VehiclesPerTick` vehicles (unless `SpawnUntilTick` is set and passed) with the next plates (`PlateFormat.Decode(permutation.Next())`
and a desired speed), then every active vehicle, new ones included, advances one camera and its measurement is produced, then vehicles at
camera 10 are removed (BR-SIM-02, AMB-03). Measurements are ordered by camera, then by vehicle age. A speed is drawn for **every**
advance, also for a disabled camera, so that disabling a camera does not change the speeds of the others; a disabled camera's
measurement is not emitted. A camera that emitted nothing, is not disabled, and heartbeats are enabled appears in the heartbeat list.
The permutation must have `size == PlateFormat.Size` (checked in the constructor).
Tests: tick `k` emits `100 * min(k, 10)` measurements and from tick 10 on exactly 1,000, 100 per camera, with 100 spawned (AC-02,
BR-SIM-02, BR-SIM-04); over 15 ticks every plate has exactly the cameras 1 to 10 in order, each once, and no camera 11 (AC-01);
plates are unique over 50 ticks (AC-03, BR-SIM-03); all measurements of a tick carry the given timestamp; a vehicle's speeds follow
the speed model (consecutive differences small); the same seed gives identical output, different seeds differ; `VehiclesPerTick` 1 and
1,000; `SpawnUntilTick` 3: no new plates after tick 3, the fleet drains, cameras go idle in order and heartbeats list exactly the
idle enabled cameras (none when heartbeats are disabled), after tick 13 no measurements at all (AC-33); `DisabledCameras = [7]`:
no camera 7 measurement, vehicles still reach camera 10 and the speeds of the other cameras equal those of a run without the
disabled camera (AC-34); removed and active counts add up.

*Push the branch.*

### Phase 2: host core and the plate state file (tests in `.../Generator/Host` and `.../Generator/Infrastructure`)

No wire types are needed in this phase. Add `Microsoft.Extensions.Hosting` to the host project and
`Microsoft.Extensions.Logging.Abstractions` where needed.

**Step 6: settings.** `GeneratorSettings` (host) bound from section `Generator` per design section 6, with a `Validate()` that returns
all errors (`VehiclesPerTick` at least 1, cameras within 1 to 10 and no duplicates, `SpawnUntilTick` and `MaxTicks` positive when set,
speed parameters positive, `Mqtt:Port` 1 to 65535, `Mqtt:Host` and `Topic` not blank). `Mqtt` options are a separate type
`MqttPublisherOptions` defined in Infrastructure (the transport is its concern) with its own `Validate()` and the properties host,
port, topic, `PublisherMode` (`PerCamera` default, `SingleConnection`), `RunId`; the host binds `Generator:Mqtt` to it. Mapping
`GeneratorSettings` to `SimulationOptions`.
Tests: defaults equal the design's table; overrides from an in-memory configuration (including `DisabledCameras` as a list and
`Heartbeats:Enabled`); each invalid case yields its message and several errors are reported together; `RunId` is generated (short, random)
when absent and stable within one run.

**Step 7: `FilePlateStateStore`** (Infrastructure, implements `IPlateStateStore`). JSON file, written to a temporary file in the same
directory and moved over the target (atomic replace), directory created when missing. `Load()` returns null when the file is missing,
unreadable, not valid JSON or holds an invalid state (the caller starts a new permutation and a warning is logged through `ILogger`).
Tests with a unique temporary directory per test, cleaned up: round trip; overwrite leaves no temporary file; missing file gives null;
corrupt JSON gives null and a warning; an invalid state (not coprime `A`, index out of range) gives null; a concurrent reader never
sees a partial file (write 200 times while another task reads, every read is null or a valid state) (BR-SIM-03).

**Step 8: `SimulationRunner`** (host, `BackgroundService`) and the permutation start-up logic. Start-up: with `Seed` set, build
the `DeterministicRandom` from it, create the permutation with `FromSeed` and **do not use the store**; without `Seed`, draw a random seed,
log it, and `Load()` the store: use its state when present, otherwise create a permutation from the seed and save it. The tick loop uses
`PeriodicTimer` on the injected `TimeProvider` with the fixed 1-second interval, runs tick 1 immediately, and per tick: take the timestamp
from `TimeProvider.GetUtcNow().UtcTicks`, call `Simulation.Tick`, **save the plate state (when the store is in use) before publishing**
(a plate that may have reached the broker is then always recorded as used), then `await publisher.PublishTickAsync`. Missed periods are not
caught up (the timer coalesces them). A tick that takes longer than the interval logs a warning with the duration and message count.
After `MaxTicks` the runner stops the host (exit code 0). Stop requests are honored between ticks and during waiting; a tick that is
being published is completed first, with a grace limit of 10 s after which it is abandoned and logged. Every 10 ticks an Information
summary (ticks, messages, plates drawn, longest tick duration since the last summary); a final summary on exit; a warning when the
permutation wraps; a start-up summary (effective settings, seed, state path, mode).
Tests with `FakeTimeProvider`, a fake publisher (records calls, can block) and a fake store (records calls and order), logs captured with
`FakeLogger`: one tick per interval and tick 1 at start; the next tick does not start while the previous is being published (a blocked
publisher plus advancing time start no second tick; after release exactly one immediate catch-up tick, no burst); an overrun logs the
warning; `MaxTicks` stops after the last tick with all published; state saved before the publish call of the same tick; with `Seed` set the
store is never called and two runners with the same seed publish identical plates; without `Seed` and with a stored state the runner
resumes it, so a second run never repeats a plate of the first (AC-03, BR-SIM-03); without `Seed` and without a stored state a new state is
created and saved; a stop request during a blocked publish completes the tick (when released) and then stops; a publisher that never
completes is abandoned after the grace limit (advance the fake clock); the summary is logged every 10 ticks.

*Push the branch. This is the first normal stopping point (see the gate in section 3): report to the owner.*

### Phase 3: Infrastructure with the wire types (`Generator.Infrastructure`)

Gate satisfied (section 3). Add `MQTTnet` to Infrastructure.

**Step 9: `MessageEncoder`.** Measurement and heartbeat (`Measurement` of the Domain and a heartbeat for a camera and timestamp) to UTF-8
JSON bytes through the types and the source-generated context of `Processor.Contracts`; `Type` on every message; nothing else is
serialized by reflection.
Tests (contract level, in `.../Generator/Infrastructure`): the encoded bytes equal the example files `docs/contracts/examples/measurement.json`
and `heartbeat.json` as JSON documents (compare parsed trees; add the example files to the test project as linked content copied to the
output directory unless the processor's contract tests already did); property names and order of types as documented; `CurrentSpeed`
written as the shortest round-trip text (`131.2`); payloads never exceed 1,024 bytes; the longest generated plate is accepted by the
contract's limit of 16 characters.

**Step 10: `MqttMeasurementPublisher`** (implements `IMeasurementPublisher`) per design section 5 and the spike findings: MQTTnet 5,
MQTT 3.1.1, QoS 1, retain false, topic from the options, clean session (a publisher needs no session), keep alive 30 s, connect to the
host from the options (the default is `127.0.0.1`). Connects lazily on the first `PublishTickAsync` and disconnects on dispose.
`PerCamera`: one client per enabled camera named `speed-camera-<cameraId>-<runId>` created on first use; the clients publish in
parallel, each sends its camera's messages one after another (about 100 messages per tick, awaited in order). `SingleConnection`:
one client `speed-generator-<runId>`; the tick's messages are started in order without awaiting each one, then awaited together (the spike
measured 74 % of the second for sequential awaiting of 1,000 messages); verify with a test that the broker receives them in the started
order, and if MQTTnet does not guarantee it, fall back to a bounded number of in-flight publishes and write it into the notes. Heartbeats
are published by the idle camera's client (or the single client) after the tick's measurements. A disabled camera has no client and no
messages. Failure handling: when a publish fails or a connection drops, reconnect with exponential backoff capped at 10 s (log each
change as a connection event), then resend from the failed message of that client **in order** (a duplicate is possible and the processor
rejects it by the sequence rule, BR-ING-02); `PublishTickAsync` returns only when every message is acknowledged and throws
`OperationCanceledException` when the token is cancelled while waiting. A pure function builds the client id from mode, camera and run id
(unit tested in `.../Generator/Infrastructure`).
Tests (E2E project, `Category=E2E`; Testcontainers Mosquitto started with `deploy/mosquitto/mosquitto.conf`; a recording subscriber using
MQTTnet; if the processor's `BrokerFixture` exists after the merge, reuse it, otherwise create one under
`HighwaySpeedEnforcement.E2E.Tests/Infrastructure`): both modes deliver every message of a tick, per camera in order, 1,000 messages in a
steady-state tick; a vehicle's ten messages arrive in camera order across ticks; heartbeats arrive on the same topic with the right
camera and timestamp; a disabled camera produces no messages (and no client in `PerCamera` mode); stopping the broker container while a
tick is being published makes `PublishTickAsync` wait, and after the broker is started again the tick completes and every message was
delivered at least once with nothing missing (BR-SIM-04, AC-34, AC-33, ADR-0001 behavior). Remove the temporary
`--ignore-exit-code 8` from `HighwaySpeedEnforcement.E2E.Tests.csproj` when this step adds the first test, unless the processor branch already
removed it.

*Push the branch.*

### Phase 4: composition root and the whole generator

**Step 11: `Program`.** Generic Host (`Host.CreateApplicationBuilder`), `appsettings.json` with the design's defaults (copied to the output
directory), environment variables (`Generator__Seed`) and command line (`--Generator:Seed=42`) through the default providers, JSON console
logging with `TimeProvider.System`, registration of settings, the permutation start-up, `FilePlateStateStore`, the publisher and the runner.
Settings are validated before the host starts: invalid settings print every error to the error stream and end the process with exit
code 1, without a stack trace. `HostOptions.ShutdownTimeout` of 15 s. Exit code 0 after `MaxTicks` or a stop signal.
Tests: building the host with valid settings resolves the runner and the publisher; invalid settings give all error messages and the
exit-code path (extract the start-up validation into a testable method returning errors and an exit code); the default state path is
under `LocalApplicationData`.

**Step 12: whole-generator E2E** (`HighwaySpeedEnforcement.E2E.Tests/Generator`, `Category=E2E`). Fixture: Testcontainers Mosquitto
and a recording subscriber; the real generator process is started from the built output (`dotnet <path>/HighwaySpeedEnforcement.Generator.dll`
with arguments or environment variables for host, port, `MaxTicks`, a temporary `StatePath`), standard output captured. One shared run
of 15 ticks serves several assertions (a class fixture): every plate has cameras 1 to 10 in order and no camera 11 (AC-01); 10,500
messages in total; ticks are the distinct timestamps, each steady-state tick has 1,000 messages and 100 per camera, and 100 new plates (AC-02);
all plates unique (AC-03). Further runs: two runs one after the other without a seed and with the same `StatePath` (3 ticks each) share no
plate (AC-03); two runs with the same `Seed` produce identical plates and speeds (reproducibility); a run with `DisabledCameras=[7]` has
no camera 7 message and the other cameras are normal (AC-34); a run with heartbeats and `SpawnUntilTick=3` has heartbeats exactly
for idle cameras and never a heartbeat and a measurement of the same camera in the same tick (AC-33); invalid settings end with exit
code 1 and the error text; the logs are JSON lines with the start-up summary and the seed. Real time: runs take seconds, keep their number low.

**Step 13: wrap-up.** Update `CLAUDE.md` minimally (how to run the generator, for example
`dotnet run --project src/Generator/HighwaySpeedEnforcement.Generator -- --Generator:Seed=42 --Generator:MaxTicks=15`, and the status of
the generator; keep the edit small and separate from other sections to avoid merge conflicts with the processor branch), check by hand
that the generator publishes into the broker of `docker compose up -d --build` without errors for about 30 s (and that the processor
log shows accepted measurements if the processor is runnable at that point), run the full test suite including E2E, and write the final
report (section 7).

*Push the branch.*

## 6. Guidance on details the design leaves open

- **Class shapes.** Prefer immutable records and `readonly record struct`; `Simulation` may keep mutable private vehicle state (a
  small struct or class with plate, camera counter, desired speed, last speed). Measurements of a tick go into one pre-sized list.
- **Arithmetic.** Plate permutation uses `long`; the products stay below 2^63 for the real size. Use `checked` arithmetic in the
  permutation constructor to prove it for the given size.
- **Time.** One timestamp per tick from `TimeProvider`; the tick loop never uses `DateTime.UtcNow`. The overrun check uses
  `TimeProvider.GetTimestamp` and `GetElapsedTime`.
- **Logging.** `ILogger` with message templates (no string interpolation), event ids for lifecycle messages, no per-message Information
  logging.
- **MQTT client.** Copy the structure of the spike: `MqttClientFactory`, `MqttClientOptionsBuilder` with
  `WithProtocolVersion(MqttProtocolVersion.V311)`, `WithTcpServer(host, port)`, `WithClientId`, `WithCleanSession`, `WithKeepAlivePeriod`;
  `MqttApplicationMessageBuilder` with `WithQualityOfServiceLevel(AtLeastOnce)`; use `127.0.0.1`, never `localhost`.
- **Process handling in E2E.** Start the process with redirected output, wait for exit with a timeout, kill the process tree on failure,
  and make sure no generator process outlives a failed test.

## 7. Final report (end of step 13)

A short message to the owner containing: steps completed and steps skipped with the reason; test counts per project; the list of deviations
and open questions from the notes file; anything in the documents or in the processor's wire types that turned out to be wrong or
unclear; the measured duration of a tick at 1,000 messages for both publisher modes against the real broker; the exact commands to
run the generator and the tests.
