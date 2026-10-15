# Generator: component design

**Status:** Accepted (owner, 2026-10-14). Binding inputs: the assignment PDF (section 1), ADR-0001 (MQTT), ADR-0004 (layers), `docs/domain/`
(`BR-SIM-01..05`, `BR-TEL-01..03`, `AC-01..03`), `docs/contracts/telemetry-messages.md`, AMB-03 and AMB-08.

## 1. Purpose and scope

A console application that simulates the ten cameras of the highway and publishes telemetry over MQTT. It is the mock data
producer of the assignment; it must follow the state machine of the assignment exactly and be reproducible with a seed.

Beyond the assignment (all small, all optional by configuration): publishing through one MQTT connection per camera, a
simulated camera outage, a pause of new traffic, heartbeats, a finite run. They exist to demonstrate and test the processor
(AC-18, AC-33, AC-34), not as a traffic model.

## 2. Behavior

Once per second (the tick interval is fixed at one second; the timer is `PeriodicTimer` on the injected `TimeProvider`):

1. **Spawn** `VehiclesPerTick` new vehicles (default 100) with unique number plates and camera counter 0.
2. **Advance and emit:** every active vehicle, new ones included, advances its counter by one and emits a measurement for that camera.
3. **Exit:** vehicles whose counter reached 10 are removed after emission (AMB-03). A vehicle emits exactly ten measurements,
   cameras 1 to 10, never 11 (BR-SIM-04).

Steady state: 1,000 measurements per tick, 100 per camera; during warm-up the tick `k` emits `100 * min(k, 10)`.
All measurements of a tick carry the same timestamp, the UTC time at the start of the tick (`TimeProvider`). A tick is complete only
when every message of it has been acknowledged by the broker; the next tick starts after that (see 5).

## 3. Contents of the projects

### Domain (`HighwaySpeedEnforcement.Generator.Domain`, no dependencies)

| Type | Responsibility |
|---|---|
| `Measurement` | Generator's own record: plate, timestamp ticks, speed (`float`), camera id. |
| `DeterministicRandom` | Small seeded PRNG (SplitMix64) with `NextDouble`, `NextInt`, `NextGaussian`. Own implementation, because `System.Random` seeded sequences are not guaranteed stable across runtime versions; reproducibility is a requirement. |
| `PlateFormat` | The simplified Czech-like plate space (section 4): regions, character sets, size `N`, decoding of an index in `[0, N)` into a plate such as `1AB 2345`. |
| `PlatePermutation` | Affine permutation `p(i) = (a * i + b) mod size` with `gcd(a, size) = 1` (size is a parameter so that tests can use a small one; the simulation uses `PlateFormat.Size`); `Next()` returns the next permuted index and advances the counter, `PlateFormat.Decode` turns it into a plate; exposes `PlateState` `(a, b, nextIndex)` for persistence and counts wraps. |
| `SpeedModel` | Generates the speed of a vehicle at each camera (section 4). |
| `SimulationOptions` | Vehicles per tick, spawn limit, disabled cameras, heartbeats on or off, speed parameters. |
| `Simulation` | The state machine of section 2. `Tick(timestampTicks)` returns a `TickOutput`: the tick number, the timestamp, the list of measurements, the cameras that send a heartbeat (cameras that emitted nothing and are not disabled, only when heartbeats are enabled), and counters (spawned, removed, active). Pure and deterministic for a given seed. |
| `IMeasurementPublisher` | **Output port**, implemented in Infrastructure: `PublishTickAsync(TickOutput, CancellationToken)` completes when all of the tick's messages are acknowledged; disposing it disconnects. |
| `IPlateStateStore`, `PlateState` | **Port** for the persisted permutation `(a, b, nextIndex)`: `Load()` returns the state or null, `Save(state)`. Implemented in Infrastructure. |

### Infrastructure (`HighwaySpeedEnforcement.Generator.Infrastructure`, references Domain and `HighwaySpeedEnforcement.Processor.Contracts`)

| Type | Responsibility |
|---|---|
| `MessageEncoder` | `Measurement` or heartbeat to UTF-8 JSON through the source-generated context of `HighwaySpeedEnforcement.Processor.Contracts` (PascalCase, `Type` on every message). |
| `MqttMeasurementPublisher` | Implements the port with MQTTnet 5 (MQTT 3.1.1, QoS 1, no retain, topic `highway/telemetry/v1`). Two modes (section 5). Reconnect with backoff; connection changes logged. |
| `FilePlateStateStore` | Implements `IPlateStateStore`: reads and writes `(a, b, nextIndex)` to a small JSON file (atomic replace) so plates stay unique across restarts. |

### Host (`HighwaySpeedEnforcement.Generator`)

| Part | Responsibility |
|---|---|
| `Program` | Generic Host, settings binding and validation, JSON console logging, composition root. |
| `SimulationRunner` | `BackgroundService`: tick loop, calls `Simulation.Tick`, then the publisher, stops after `MaxTicks`, logs a run summary, handles shutdown. |
| `GeneratorSettings` | Section `Generator` of `appsettings.json`, environment variables and command line. |

## 4. Plates and speeds

### Plate format (approximation, AMB-08)

Modeled on current Czech plates, `1AB 2345`: one digit 1 to 9, one regional letter, one more character (a digit or a letter),
a space, four digits. It is an approximation, not an authoritative reproduction of the registration rules.

| Part | Allowed | Size |
|---|---|---|
| Series digit | `1`..`9` | 9 |
| Region letter | `A S C P K U L H E J B M Z T` (the 14 regions) | 14 |
| Third character | digits `0`..`9` and letters `A`..`Z` except `G O Q W` | 32 |
| Number | `0000`..`9999` | 10,000 |

`N = 9 * 14 * 32 * 10,000 = 40,320,000` plates. An index in `[0, N)` is decoded as a mixed-radix number into the four parts.

### Affine permutation

`p(i) = (a * i + b) mod N` with `a` coprime to `N` (N is `2^10 * 3^2 * 5^4 * 7`, so `a` must not be divisible by 2, 3, 5 or 7) is
a bijection of `[0, N)`. The generator draws `plate(k) = decode(p(counter + k))` for `k = 0, 1, 2, ...`. Consecutive plates look unrelated,
and the first `N` plates are unique without keeping a set of used plates. `a` and `b` come from the seed.

- **Uniqueness within a run:** guaranteed up to `N` plates, that is about 4.7 days at 100 vehicles per second. After that the counter
  wraps and plates are reused; the generator logs a warning when it wraps (documented limit).
- **Uniqueness across restarts (BR-SIM-03):** the state `(a, b, nextIndex)` is stored in a file
  (`<LocalApplicationData>/HighwaySpeedEnforcement/generator-state.json`, configurable) after every tick, **before the tick's messages are published** (atomic replace), so a plate that may have reached the broker is always recorded as used. A restart
  continues with the same permutation and the next index, so no plate of an earlier run is used again. With a fixed `Seed` the state
  file is not used (tests and reproducible runs).

### Speed model

Speeds are `float` km/h, rounded to 0.1 km/h (the resolution of a measuring device). With that resolution, equal speeds between
vehicles occur naturally, which exercises the tie-breaking rule (BR-LB-05) without staging it.

- At spawn each vehicle draws a desired speed `v* ~ Normal(BaselineSpeed - 5, 15)` (baseline 130 km/h), clamped to `[60, 200]`.
- Speed at camera `k`: `v_k = v_(k-1) + 0.3 * (v* - v_(k-1)) + Normal(0, 3)`, with `v_0 = v*`, clamped to at least 0 and rounded to 0.1.

Consecutive speeds of a vehicle therefore differ by a few km/h and the population fluctuates around the baseline, with a tail of
speeders. All parameters are settings with these defaults.

## 5. Publishing

`IMeasurementPublisher.PublishTickAsync` publishes all messages of a tick and returns when every one is acknowledged (QoS 1).
Messages of one tick may be sent concurrently: they belong to different vehicles, and a vehicle's consecutive measurements are one
tick apart, so per-plate order holds because the tick is complete before the next starts (the processor needs no more, ADR-0001).

| Mode | Behavior |
|---|---|
| `PerCamera` (default) | Ten MQTT clients `speed-camera-<cameraId>-<runId>`, each publishing its camera's messages in order; the closest to real cameras and it makes a dead camera a real disconnected publisher. |
| `SingleConnection` | One client `speed-generator-<runId>`, messages pipelined (not awaited one by one: the spike measured 74 % of the tick budget for sequential awaiting). |

- **Disabled cameras** (`DisabledCameras`, for example `[7]`): the camera's measurements are not emitted and, in `PerCamera` mode,
  its client does not connect; vehicles still advance past it (AC-34).
- **Heartbeats** (`Heartbeats:Enabled`, default false, BR-TEL-03): each enabled camera that emitted no measurement in the tick sends a
  heartbeat with the tick's timestamp. This matters when `SpawnUntilTick` stops new traffic: boards then expire to empty instead of freezing.

### Slow or unavailable broker

- **Slow publishing:** the tick loop waits for the tick to be acknowledged; the periodic timer does not fire for missed periods, so the
  simulation runs slower instead of dropping data. This also matches real cameras, which buffer measurements and deliver them late rather than losing them (owner's reasoning). A warning is logged when a tick takes longer than the interval, including the
  duration and the number of messages.
- **Broker unavailable:** publishing waits for the reconnect (backoff capped at 10 s); the simulation pauses and no tick is lost or
  half-emitted, so every vehicle still emits exactly ten messages. Dropping messages instead would be a different, explicit policy
  and is not implemented.

## 6. Configuration

Section `Generator` (overridable by environment variables such as `Generator__Seed` and command-line arguments).

| Setting | Default | Notes |
|---|---|---|
| `Seed` | none | when set: reproducible run, state file not used; when absent: a random seed is drawn, logged at startup, and the state file is used |
| `StatePath` | `<LocalApplicationData>/HighwaySpeedEnforcement/generator-state.json` | |
| `VehiclesPerTick` | 100 | the assignment value; larger for load tests (10x burst: 1000) |
| `SpawnUntilTick` | none | after this tick no new vehicles spawn; active vehicles still drain |
| `MaxTicks` | none (run until stopped) | the process exits with code 0 after this tick |
| `DisabledCameras` | empty | camera ids 1 to 10 |
| `Heartbeats:Enabled` | false | |
| `Speed:BaselineKmh`, `Speed:DesiredStdDev`, `Speed:StepStdDev` | 130, 15, 3 | section 4 |
| `Mqtt:Host` / `Port` / `Topic` | `127.0.0.1` / 1883 / `highway/telemetry/v1` | host, not `localhost` (spike) |
| `Mqtt:PublisherMode` | `PerCamera` | or `SingleConnection` |
| `Mqtt:RunId` | short random id | used in client ids (the transport's concern, so it lives with the MQTT options) |

Validation at startup: `VehiclesPerTick` at least 1, cameras within 1 to 10, `Mqtt:Port` valid. Invalid settings stop the process with exit
code 1 and a message listing all errors.

## 7. Observability

Structured JSON console logs. Information: startup summary (settings, seed, state path), connections, a summary every 10 s (ticks,
messages, plates drawn, longest tick duration), the final summary on exit. Warning: tick overrun, reconnecting, plate counter wrap.
Debug: per tick. No metrics endpoint (a console tool); the summaries are the visibility.

## 8. Edge cases

| Situation | Behavior |
|---|---|
| Stop signal during a tick | the current tick is completed and acknowledged, the state file written, clients disconnected, exit 0 |
| Process killed | the state file already contains the plates of the unfinished tick (saved before publishing), so no plate is reused; in-flight messages of the unfinished tick may be lost (vehicles of that tick miss messages, which the processor tolerates) |
| State file missing, unreadable or corrupt | a new permutation from a random seed is created, a warning logged |
| Counter reaches `N` | wraps, warning logged (limit documented) |
| `SpawnUntilTick` passed | no new vehicles spawn; the active ones drain, cameras that no longer see a vehicle send heartbeats if enabled, and once the fleet is empty a tick emits no measurements |
| Camera disabled in `SingleConnection` mode | its messages are skipped; the rest are unaffected |

## 9. Test map

| Area | Level | Scenarios |
|---|---|---|
| `DeterministicRandom` | unit | fixed expected values for a seed (pins the algorithm) |
| `PlateFormat`, `PlatePermutation` | unit | format of every decoded plate, bijection on a small `N`, `a` coprime, determinism per seed, wrap, resume from `nextIndex` (BR-SIM-03, AC-03) |
| `SpeedModel` | unit | rounding to 0.1, bounds, mean and spread near the settings, small steps between cameras, ties occur (BR-SIM-05) |
| `Simulation` | unit | per-tick counts 100, 200, ..., 1000, 1000; each vehicle cameras 1 to 10 in order once; none for camera 11; unique plates over 50 ticks (AC-01, 02, 03); disabled cameras; `SpawnUntilTick`; idle cameras (AC-34, 33) |
| `MessageEncoder` | unit / contract | output equals `docs/contracts/examples` |
| `PlateStateStore` | unit | round trip, atomic replace, corrupt file |
| `SimulationRunner` | unit with `FakeTimeProvider` and a fake publisher | one tick per interval, tick completes before the next, overrun logged, `MaxTicks`, shutdown completes the tick |
| `MqttMeasurementPublisher` | integration (E2E project, Testcontainers broker) | both modes: all messages arrive, per-vehicle ten in order, client ids, broker restart pauses and resumes |
| Whole generator | E2E: real process and a recording subscriber | AC-01, 02, 03 end to end, a disabled camera |

## 10. Decisions (confirmed by the owner, 2026-10-14)

1. **Default publisher mode `PerCamera`** (ten connections), with `SingleConnection` as the fallback.
2. **Plate state file** (default on, outside the repository) for uniqueness across restarts, instead of only probabilistic uniqueness.
3. **Own PRNG** (SplitMix64) instead of `System.Random`, for reproducibility across runtime versions.
4. **Speed rounded to 0.1 km/h**, so that ties occur naturally.
5. **Pause, not drop,** when the broker is slow or unavailable.
6. Plate format and region list as in section 4 (an approximation).

## 11. Interactive console (added 2026-10-14, owner's request)

For a live demo the operator can change the traffic while the generator runs, from the keyboard, with a status bar at the bottom of the
console and the log lines scrolling above it. It is a thin layer on top of the simulation and **never changes the non-interactive behavior**.

**When it is active.** Only when both standard input and standard output are a terminal (`Console.IsInputRedirected` and `Console.IsOutputRedirected`
are false) and `Generator:Console:Interactive` is not `false` (a boolean, default `true`). Redirected, piped or hosted runs (the E2E tests,
`> file`, containers) behave exactly as before: JSON logs on standard output, no keys, no bar. If the terminal cannot do ANSI escapes the
interactive mode falls back to plain compact log lines plus keys, without the bar.

**Keys.**

| Key | Action |
|---|---|
| `P` | pause / resume new traffic: no new vehicles are created, the ones on the road finish, cameras without a vehicle send heartbeats when they are enabled. Resuming creates vehicles again from the next tick. |
| `H` | heartbeats on / off |
| `+` / `-` | more / fewer `VehiclesPerTick`: steps of 10 up to 100 and of 100 above it (keys range 10 to 2000; a smaller configured value is kept until the first key press) |
| `1`..`9`, `0` | switch camera 1 to 9, and 10 (`0`), on or off (a camera that is off emits nothing, as in `DisabledCameras`; vehicles still pass it) |
| `Q` | quit (same as Ctrl+C: the running tick is completed first) |

**Mechanism.** The runner reads a thread-safe `RuntimeControl` (paused, vehicles per tick, heartbeats, disabled cameras) once at the start of every tick and gives the
simulation the resulting `SimulationOptions` for that tick; the keyboard handler only writes `RuntimeControl`. Changes take effect at the next tick and are logged
(Information, "operator: ..."). A run in which no key is pressed is exactly as reproducible as before. Startup values come from the settings.

**Screen.** The bottom three rows are fixed (ANSI scroll region): a separator, the status line and the key help; everything above scrolls. Status line example:
`tick 123 | traffic RUNNING | 100 veh/tick | heartbeats off | cameras off: 7 | broker connected | sent 12,345 | last tick 0.13 s | seed 42`.
In this mode logs use a compact one-line console format (`HH:mm:ss level message`) instead of JSON. The bar is redrawn at the end of every tick, on a key press and
on terminal resize; console writes from logging and from the bar go through one lock so they never interleave.

**Structure.** Domain: `RuntimeControl` and its snapshot. Host: key mapping (pure), status line formatting (pure, width aware), `InteractiveConsole` (the only class that
touches `System.Console`, behind a small interface), and the logging switch. No new package.
