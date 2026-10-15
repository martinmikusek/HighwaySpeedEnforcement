# Generator: implementation notes

Deviation log kept during the implementation of `generator-implementation-plan.md`. One section per step: what was done that is
not literally in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason.

## Deviations by step

### Step 0: setup

- Baseline on `feature/generator` (merge of `origin/feature/processor` e640ebd and `origin/main` 2436c79): build and format
  check green; tests 137 of 138 green. The one failure is the processor's
  `IngestPipelineTests.Status_reports_tracked_vehicles_buffer_depth_newest_event_and_lag` (expected 1, actual 2); it failed
  alone as well, but passed in later full runs, so it is intermittent. It belongs to the processor implementer; nothing
  under `src/Processor/` is touched here.

### Step 1: DeterministicRandom

- The reference values were computed in PowerShell with `BigInteger` arithmetic modulo 2^64 and the explicit
  `op_ExclusiveOr` / `op_RightShift` operators; PowerShell's `-bxor` and `-shr` silently convert a `BigInteger` and gave
  wrong values on the first attempt. Seed 0 reproduces the published `0xE220A8397B1DCDAF`.
- `NextGaussian` draws two uniforms per value and does not cache the second Box-Muller deviate; simpler and the sequence
  stays deterministic.

### Step 2: PlateFormat

- The radixes are separate constants (`SeriesRadix`, `RegionRadix`, `ThirdRadix`) because `string.Length` is not a constant
  expression and `Size` must be a `const` (it is used in attributes of the tests); a test asserts that each radix equals
  the length of its alphabet.

### Step 3: PlatePermutation and PlateState

- The supported size is 2 to `int.MaxValue` (the plan says "up to 2^31"): one `NextInt` draw then covers `[1, size)` uniformly,
  and size 1 is degenerate (no `A` in `[1, 1)`). `PlateFormat.Size` is far below the limit.
- `PlatePermutation.IsValidState(size, state)` is public so that the file store (step 7) can reject an invalid stored state
  without catching an exception.
- `PlateState` is a `readonly record struct` (three `long`s, no identity).

### Step 4: SpeedModel

- `SpeedOptions` is an immutable record with `init` properties; the host settings (step 6) map to it. The fixed design values
  (pull 0.3, offset -5, clamp 60 to 200, resolution 0.1) are public constants on it, not settings.
- The speed model does not clamp the per-camera speed to the desired-speed range, only to at least 0 (as the design says);
  the test asserts that the mean reversion keeps speeds within 25 km/h of the desired speed anyway.

### Step 5: Simulation and its types

- Vehicles are kept as cohorts (one list per spawn tick, newest first, all vehicles of a cohort at the same camera), so the
  emission order "by camera, then by vehicle age" is the natural iteration order and removal is dropping the oldest cohort.
- `Simulation` exposes `PlatesDrawn` and `Permutation` (for the runner's summaries and the state save) and the constant
  `CameraCount` = 10; `TickOutput.Active` is the count after the removal of the tick.
- `Measurement` is a `readonly record struct` (four fields, no identity); `TickOutput` is a record class because it carries
  lists.
- The constructor validates `VehiclesPerTick` and the camera ids as well (cheap, and the Domain must not rely on the host's
  settings validation).

### Step 6: settings

- `RunId` is bound from `Generator:Mqtt:RunId` (a property of `MqttPublisherOptions`, as the plan says), not from
  `Generator:RunId` as the design's settings table lists it; see OQ-1.
- `StatePath` must not be blank when no `Seed` is set (not in the plan's list; a blank path would fail at the first save).
- Speed settings are a nested class `GeneratorSettings.SpeedSettings` with its own `Validate()`; `GeneratorSettings.Validate()`
  prefixes nested errors with `Speed:` and `Mqtt:` so the message names the configuration key.
- `MqttPublisherOptions.Validate()` also rejects an undefined `PublisherMode` value and a blank `RunId` (both only reachable
  through configuration).
- `PublisherMode` is an enum in Infrastructure next to the options.

### Step 7: FilePlateStateStore

- The atomic replace is `File.Replace(temporary, target)` (the `ReplaceFileW` path, which renames the old file away) rather
  than `File.Move(temporary, target, overwrite: true)`: on Windows the latter fails with "access denied" while a reader holds
  the target open, even with delete sharing, and the concurrent-reader test showed it. A short bounded retry (10 × 5 ms)
  remains as a safety net for the rare sharing collision; it is not what makes the test pass.
- The reader opens the file with `FileShare.ReadWrite | FileShare.Delete`, so a replace can proceed during a read.
- The file holds a small explicit document `{ "A", "B", "NextIndex" }` written through a source-generated JSON context
  (the Domain record is not serialized directly, so the file format does not follow a refactoring by accident).
- Load logs a warning (event ids 2001, 2002) for an unreadable or invalid file; a missing file is silent (the normal first run).
- The temporary file name carries a GUID, so two generator processes saving into the same path do not share a temporary file.

### Step 8: SimulationRunner

- The permutation start-up is a separate record `SimulationStartup.Create(settings, store, randomSeed)` in the host; the
  runner draws the random seed (`Random.Shared.NextInt64()`) and passes it in, so the start-up itself is deterministic and
  testable. The seed is a `long` in the settings and is cast unchecked to the PRNG's `ulong`.
- The same `DeterministicRandom` instance feeds the permutation (`FromSeed`) and the speeds, as the domain tests do.
  With a resumed state the permutation comes from the file and the random source (speeds) from the fresh random seed.
- The grace limit uses a timer of the injected `TimeProvider` that is armed by the stop request and cancels the publisher's
  token; the publisher's `OperationCanceledException` is logged as "abandoned" (event id 3008) and the loop ends.
- Any other exception from the publisher propagates out of `ExecuteAsync`; the Generic Host then logs it and stops the
  host (`BackgroundServiceExceptionBehavior.StopHost`, the default). The design says the publisher waits and reconnects
  rather than throws, so this is the last resort, not a policy.
- The start-up summary prints `StatePath=not used` with a configured seed (the `LoggerMessage` generator renders null as
  an empty string, which reads as a missing value).
- Log event ids: 3001 start-up, 3002 tick (Debug), 3003 overrun, 3004 summary, 3005 final summary, 3006 wrap, 3007 MaxTicks,
  3008 abandoned; the state store uses 2001 and 2002. Disjoint from the processor's 1000 range.
- The tests feed the `FakeLogger` output into the same ordered call log as the fake publisher and store, so a test can
  await a log line as the observable end of a tick (the plan forbids sleeping); `WaitAsync` with a 10 s bound turns a
  wrong expectation into a failure instead of a hang.

### Gate before step 9

Merged `origin/feature/processor` ab7a970 (wire types 7866712, decoder, MQTT source, E2E support). One conflict in
`Directory.Packages.props` (both sides added package versions), resolved by keeping every entry. After the merge: build and
format clean, 330 tests green (the processor's E2E tests ran against Docker). Reused from the processor branch: the example
files linked as `Contracts/examples` in the unit test project, `MosquittoBroker` (Testcontainers, tuned configuration, fixed
host port), `Eventually`, `TestOutputLogger`; the `--ignore-exit-code 8` line is already gone from the E2E project.

### Step 9: MessageEncoder

- A static class with `EncodeMeasurement(Measurement)` and `EncodeHeartbeat(cameraId, timestampTicks)` returning UTF-8 bytes;
  no state, nothing to inject.
- The 1,024-byte and 16-character checks run over real simulation output (14 ticks with heartbeats) rather than hand-made
  values; the longest generated plate is 8 characters.

### Step 10: MqttMeasurementPublisher

- The publisher does not receive the disabled cameras: connections are created on first use, and a disabled camera never
  has a message, so it never gets a client (`ClientIds` exposes the connections for the E2E assertion).
- `SingleConnection` pipelines without a bound: every publish of the tick is started in order and the tasks are awaited
  together. MQTTnet 5 serializes packet writes through a FIFO `SemaphoreSlim`, and the E2E order test confirms the start
  order on the wire, so the bounded-window fallback of the plan was not needed. On a failure the client resends from the
  first task that did not complete successfully (duplicates possible, BR-ING-02).
- Reconnect backoff 1, 2, 4, 8, 10 s (constants, not options); the first connect attempt is immediate. `Task.Delay` uses
  the injected `TimeProvider`.
- Each connection has its own IMqttClient; connecting is guarded by a semaphore so that a reconnect is attempted once at a time.
- Event ids 2101 to 2105 (connected, connect failed, connection lost, publish failed, disconnected).
- The E2E `RecordingSubscriber` uses a persistent session and reconnects after the broker restart (500 ms retry loop, the
  kind of polling the processor's `Eventually` also does); tests use unique subtopics on the shared broker like the
  processor's tests. The outage test publishes a 6,000-message tick so that the restart lands inside the publish.

### Step 11: Program

- The start-up validation is `GeneratorHost.Check(IConfiguration)` returning the bound settings, every error and the exit
  code (0 or 1); `GeneratorHost.Configure(builder, settings)` registers the services. `Program.cs` stays a dozen lines.
- The settings object is registered as a singleton instance (not `IOptions<T>`): it is read once at startup (BR-CFG-01),
  and the runner and the publisher take plain objects.
- JSON console logging with UTC round-trip timestamps replaces the default providers; the `Microsoft.Hosting.Lifetime`
  lines stay at Information (they show start and stop in the same JSON stream).
- `appsettings.json` carries the design defaults for the keys that have one; `Seed`, `StatePath`, `SpawnUntilTick` and
  `MaxTicks` are absent (their default is "none" or computed).

### Step 12: whole-generator E2E

- The generator process is started from the host project's own build output (`src/Generator/.../bin/<Configuration>/net10.0`),
  located from the repository root like the architecture tests do; the E2E project references the host project with
  `ReferenceOutputAssembly="false"` only to make sure it is built first.
- `GeneratorRunFixture` owns its own `MosquittoBroker` (a class fixture cannot depend on another fixture) and runs the shared
  15-tick run once; the further runs reuse that broker on unique topics. Runs are sequential; the whole class takes about 45 s.
- The heartbeat run uses `VehiclesPerTick=5`, `SpawnUntilTick=3`, 8 ticks: idle cameras appear from tick 4 on, which is
  enough to check "heartbeats exactly for idle cameras, never together with a measurement of the same camera in a tick".
- Tick timestamps of the real run are checked to be 0.9 to 1.5 s apart (the `PeriodicTimer` cadence with process jitter).

### Step 13: wrap-up

- `CLAUDE.md` got one small separate section "Generator (implemented)" with the run command; nothing else in it changed.
- Manual check against `docker compose up -d --build`: 30 ticks with `--Generator:Seed=42`, exit code 0, no warning or error
  in the generator log, all ten camera clients connected, longest tick 0.35 s (tick 1, which includes the connects) and
  about 0.15 s afterwards. The processor container of this branch is still the skeleton host (its MQTT source is not wired
  into the API yet, processor plan step 14 and later), so no accepted-measurement log could be observed there.
- The E2E test projects' `TestResults/*.trx` files are build output and are ignored by git.

## Interactive console

Deviation log of `generator-console-plan.md` (design section 11), one entry per step.

### Step 1: RuntimeControl

- `RuntimeControl` takes its initial values as a `SimulationOptions` (the host maps the settings to it with the existing
  `ToSimulationOptions()`), because the Domain cannot see the host's settings class.
- A configured `VehiclesPerTick` above 2,000 (allowed by the settings, for example a load test with 5,000) is kept as
  is; only `+` and `-` clamp to 1..2,000. Clamping the initial value would change a run in which no key is pressed.
- The pause is a new option `SimulationOptions.TrafficPaused` (default false), so that one `SimulationOptions` carries
  everything a tick needs (step 2); the snapshot's `ToSimulationOptions(baseline)` applies the live values on top of the
  fixed ones (`SpawnUntilTick`, `Speed`).
- `RuntimeControlSnapshot` has value equality including the camera list, so the runner can detect a change by comparing
  two snapshots.

### Step 2: Simulation per-tick options

- The overload is `Tick(timestampTicks, SimulationOptions options)`; `Tick(timestampTicks)` calls it with the constructor
  options, so there is one code path and the "identical output" test compares the two overloads with the same options.
- The per-tick options are validated on every call (vehicles at least 1, cameras within 1..10; a few comparisons) and the
  disabled-camera table is rebuilt per tick. `Speed` of the per-tick options is ignored: the speed model is fixed at
  construction, as the design's key table changes nothing about speeds.
- The pause check sits next to `SpawnUntilTick` in `Spawn`: either one means "no cohort this tick"; everything else
  (advance, removal, heartbeats) is untouched, so a paused run drains exactly like a run after `SpawnUntilTick`.

### Step 3: runner integration

- `SimulationRunner` takes `RuntimeControl` as a constructor parameter (registered as a singleton in `GeneratorHost`), so the
  existing runner tests gained one argument; nothing else in them changed. The initial "applied" snapshot is built from the
  settings, so a key pressed before tick 1 is logged too.
- One log line per changed value (event ids 3011 traffic, 3012 vehicles per tick, 3013 heartbeats, 3014 cameras off), all
  `operator: <what> <old> -> <new>` at Information; a change reverted before the tick produces no line.

### Step 4: key mapping and status line

- The host's console types live in the folder and namespace `Interactive`, not `Console`: a namespace
  `HighwaySpeedEnforcement.Generator.Console` would shadow `System.Console` inside the host (`Program.cs` writes to
  `Console.Error`).
- Keys are mapped by the reported character first (`+`, `-`, digits, letters, case-insensitive) and by `ConsoleKey` as a
  fallback (`Add`, `Subtract`, `NumPad0..9`, `D0..D9`, `OemPlus`, `OemMinus`, `P`, `H`, `Q`) for terminals that report no
  character for a keypad key.
- The ellipsis is the ASCII `...` (three cells), not U+2026: the Windows console's default output encoding is the OEM code
  page, where the single character may not exist, and the interactive mode must not change the output encoding.
- The status line prints the last tick duration with two decimals (`0.13 s`) and the broker state as `connecting`
  (no connection yet), `connected` or `reconnecting`.

### Step 5: InteractiveConsole

- `Generator:Console:Interactive` was first bound as `Auto`/`true`/`false`; `true` and `Auto` behaved the same (the design requires
  a terminal on both ends in every case), so the owner simplified it to a boolean (default `true`, used only in a real terminal),
  see OQ-2 (resolved).
- The runner's status is a `RunnerStatus` singleton (tick, messages sent, last tick duration, configured seed) passed to the
  runner's constructor and raising `Changed` after every tick; the broker state is derived in `InteractiveConsoleService`
  from the publisher's `ConnectionCount`/`ConnectedCount` (no connection yet: `connecting`; all: `connected`; otherwise
  `reconnecting`), so the Domain port `IMeasurementPublisher` did not grow a UI concern.
- `InteractiveConsole` is created in `GeneratorHost.Configure` (the logging provider needs it before the service provider
  exists) and run by a small `InteractiveConsoleService : IHostedService` registered before the runner, so the bar is on
  screen before tick 1 and the terminal is restored after the runner's final summary. `Q` calls
  `IHostApplicationLifetime.StopApplication()`, the same path as Ctrl+C.
- `GeneratorHost.Configure(builder, settings)` keeps its signature and delegates to an overload with an `IConsoleTerminal`;
  `Program.cs` is unchanged. The real terminal (`SystemConsoleTerminal`) is a private nested class of `InteractiveConsole`.
- ANSI on Windows: `GetConsoleMode`/`SetConsoleMode` with `ENABLE_VIRTUAL_TERMINAL_PROCESSING` through source-generated
  `LibraryImport`, which needs `<AllowUnsafeBlocks>` in the host project (generated marshalling only, no hand-written
  unsafe code). On other OSes ANSI is assumed. When it fails: plain compact lines, keys without the bar.
- Screen protocol: `ESC[?25l` (hide cursor), `ESC[1;{H-3}r` (scroll region), cursor parked at row `H-3`; the bar is drawn
  with `ESC7` ... `ESC8` (save and restore cursor) and `ESC[2K` per row, lines cut to `width - 1` so the last column never
  triggers auto-wrap; log lines end with `\r\n`. A resize (polled with the keys every 50 ms) resets the region, clears the
  screen and sets everything up again. Dispose: `ESC[r`, cursor to the last row, `\r\n`, `ESC[?25h`.
- The compact log line uses local time (`HH:mm:ss`), no category and the exception on the following lines; the JSON
  provider is only replaced when the console is active, so redirected runs keep the exact previous output.

### Step 6: manual run and documentation

- The implementer's session has no interactive terminal of its own. The check was done with a throwaway harness (outside
  the repository): it starts the generator in its own console window against the compose broker, attaches to that console,
  injects keys with `WriteConsoleInput` and reads the screen buffer back with `ReadConsoleOutputCharacter`. Observed in
  a real 120x30 conhost: the output mode changed from `0x3` to `0x7` (ANSI enabled by the generator), the compact log lines
  scrolled in rows 0..26 and the bar stayed on rows 27..29 (separator, status line, key help); `P` gave
  `operator: traffic RUNNING -> PAUSED` and `traffic PAUSED` in the bar at the next tick, `+`, `7`, `H` gave the three
  `operator:` lines and `200 veh/tick | heartbeats on | cameras off: 7`, `P` again resumed, `Q` ended the process with exit
  code 0 after "Generator stopped" was printed above the bar, and the publisher's disconnect lines then printed plainly
  below the restored bar with the cursor on the last row. Not observed: colors (none are used), a resize, Windows Terminal
  (the harness used a classic console window), and whether the key help is readable on a narrow window by eye.
- A redirected run of the same build (`> file`, `MaxTicks=5`) produced 17 JSON lines, no escape character, exit code 0.
- At 120 columns the status line (about 135 characters) is cut after `last tick` with `...`; the seed is the part that
  falls off first. The format follows the design's example as is; shortening the labels would be a design change.
- The generator E2E class was run four times at the end (twice on purpose, once in the full solution run, once more):
  `Shared_run_steady_state_ticks_...` failed once (first run) and passed the other three times. Its only real-time
  assertion is the 0.9 to 1.5 s spacing of the live process's ticks, so this is the cadence jitter the step 12 note
  already mentions, not the console (which is inactive in that redirected run). Left as is; a wider tolerance or a retry
  is a decision for the owner.
- `README.md` got the "Interactive console" subsection, the sentence in the application table, the line under the run
  command and the settings row; `CLAUDE.md` one line in the "Generator" section.

## Open questions

- **OQ-1 (step 6): where `RunId` lives.** Design section 6 lists `RunId` as a top-level key of the `Generator` section;
  plan step 6 makes it a property of `MqttPublisherOptions` bound from `Generator:Mqtt`. Implemented as the plan says
  (`Generator:Mqtt:RunId`), because the client ids are the transport's concern. The design table should say `Mqtt:RunId`,
  or the owner wants the top-level key and the host copies it into the options.
- **OQ-2 (console step 5): `Interactive=true` versus `Auto`. Resolved by the owner (2026-10-14):** the setting is a boolean,  default `true`; it activates the console only when both standard streams are a terminal, so the third value was dropped.

### Console follow-ups (owner's feedback after a live run, 2026-10-14)

- `+` / `-` no longer double and halve (100, 50, 25, 12 ...): they step by 10 up to 100 and by 100 above it (80, 90, 100, 200, 300; 200, 100, 90, 80),
  the keys range from 10 to 2,000 (`RuntimeControl.IncreaseVehicles` / `DecreaseVehicles`, pure `NextUp` / `NextDown`); a configured start value outside
  the grid snaps to it on the first press, a start value below 10 stays until `+` raises it to 10, one above 2,000 goes to 2,000 on `-`.
- The status line no longer cuts the end off when the terminal is narrow: it drops the least important parts first (seed, then messages sent, then
  the camera list when no camera is off, then heartbeats) so the tick duration stays visible; a tick longer than the one-second interval is marked `SLOW`.
- Observation: at 500 to 2,000 vehicles per tick the generator reported ticks of 1.8 to 2.8 s ("longer than the interval") while the machine was at about
  90 % CPU (the owner's other Docker containers and Aspire); the generator pauses instead of dropping data, as designed, so simulated time slows down.
