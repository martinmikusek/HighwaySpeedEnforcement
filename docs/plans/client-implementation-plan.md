# Client: implementation plan

**Status:** Ready for implementation. Design: `docs/design/client.md`. This document is written for an implementer who starts without any
conversation history. Read it completely before writing code.

## 1. Goal and boundaries

Implement the WPF dashboard in the four projects under `src/Client/` (`Client.Application`, `Client.Infrastructure`, `Client.ViewModels`,
`Client.Wpf`) with the tests listed per step, so that the scenarios `AC-25..AC-28` and `AC-32`, `AC-33` hold and the whole system
(generator, processor, client) can be started by a reviewer from a short README.

Out of scope: anything not in the design; UI automation; changes to the processor, the generator or the wire contracts.

## 2. Read first (in this order)

1. `CLAUDE.md` (rules, commands, code style, layering).
2. `docs/design/client.md` (the design you implement).
3. `docs/domain/business-rules.md` (`BR-UI-*`, `BR-API-01`), `glossary.md`, `acceptance-scenarios.md` (`AC-25..28`, `AC-32`, `AC-33`).
4. `docs/contracts/rest-api.md` and `docs/contracts/examples/traffic-snapshot.json`; the REST DTOs and `RestJsonContext` in
   `src/Processor/HighwaySpeedEnforcement.Processor.Contracts/Rest/`.
5. ADR-0002, ADR-0004 (the MVVM mapping), `docs/testing-strategy.md` (no UI automation; view-model tests only).
6. `docs/design/generator-implementation-notes.md` and `processor-implementation-notes.md`: conventions the other implementers settled on.

## 3. Rules that apply to every step

- **Authority.** The documents above are decisions. Do not change `docs/adr/`, `docs/domain/`, `docs/contracts/` or the design to make
  code easier. If a document is wrong, ambiguous or contradicts another, or a step cannot be done as written: **stop that step,
  write the problem into `docs/design/client-implementation-notes.md` (section "Open questions") and continue with an independent
  step, or finish and report.** Do not guess.
- **Deviation log.** Create `docs/design/client-implementation-notes.md` at step 0. After each step add what you did that is not literally
  in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason. No deviations: write "none".
- **Style and layering** are enforced by the build (`CLAUDE.md`): Allman braces, always braces, file-scoped namespaces, `_camelCase` private
  fields, nullable on, warnings are errors; XAML must build without warnings. The architecture tests stay green and unchanged
  (`DependencyRuleTests`): Application has no project reference and only `Microsoft.Extensions.*.Abstractions` packages; ViewModels may add
  `CommunityToolkit.Mvvm`; only `Client.Wpf` targets Windows. Do not widen the rules.
- **Dependencies.** Allowed new packages: `CommunityToolkit.Mvvm` (ViewModels), `Microsoft.Extensions.Logging.Abstractions` (Application and
  ViewModels), `Microsoft.Extensions.Hosting` (Wpf), `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.Extensions.Diagnostics.Testing`
  (tests, already present). Add with `dotnet add package`; versions land in `Directory.Packages.props`. Nothing else without a note.
- **Determinism.** No `Thread.Sleep`/`Task.Delay` to wait for results in tests; use `FakeTimeProvider`, tasks completing, or awaiting the
  observable effect (with a bounded wait).
- **Test conventions.** xUnit v3; `[Trait("AC","AC-27")]` and `[Trait("BR","BR-UI-03")]` on every test that covers a scenario or rule;
  `[Trait("Category","E2E")]` for tests that need Docker. Names `Subject_condition_result`. Pass `TestContext.Current.CancellationToken` to
  async calls (analyzer). Tests live in `HighwaySpeedEnforcement.Unit.Tests/Client/{Application,Infrastructure,ViewModels}` (add the project
  references they need); the view-model tests must run on any OS, so they never reference `Client.Wpf`.
- **Wire contracts belong to the processor.** The client reads the REST DTOs of `Processor.Contracts` as they are and converts them to its
  own model in Infrastructure. Never create or change wire types.
- **Comments** only where the reason is not obvious from the code; no restating comments.

## 4. Definition of done for a step

All of: `dotnet build Yunex.HighwaySpeedEnforcement.slnx` has no warnings or errors;
`dotnet format Yunex.HighwaySpeedEnforcement.slnx --verify-no-changes` is clean;
`dotnet test --solution Yunex.HighwaySpeedEnforcement.slnx` passes; the deviation log is updated; one commit exists for the step
(Conventional Commits, English, for example `feat(client): add LivenessEvaluator`; follow the commit attribution rules of your environment).

**Git.** Work in your own worktree on the branch `feature/client`, created from `origin/main` (which contains the processor and the generator):

```
git fetch origin
git worktree add ../Yunex.HighwaySpeedEnforcement-client -b feature/client origin/main
cd ../Yunex.HighwaySpeedEnforcement-client
```

Commit after each step; push the branch (`git push -u origin feature/client`) after each phase; do not push to `main`, do not merge.
Do not edit files under `src/Processor/` or `src/Generator/`. Keep edits of shared files (`Directory.Packages.props`, `CLAUDE.md`, test `.csproj`
files, the `.slnx`) minimal and additive.

## 5. Steps

The order is a dependency order. Delete `AssemblyMarker.cs` of a project when it receives real types.

### Phase 0: setup

**Step 0.** Create the worktree and branch as in section 4 and `docs/design/client-implementation-notes.md` (headings: Deviations by step,
Open questions). Verify that build, format check and tests are green before any change (Docker must be running for E2E; if an unrelated
test is flaky, note it and move on).

### Phase 1: Application (`Client.Application`, tests in `.../Client/Application`)

**Step 1: model and liveness.** The client's own model as immutable records: `TrafficSnapshot(GeneratedAt, CameraBoards, GlobalBoard)`,
`CameraBoard(CameraId, LatestEvent, Entries)`, `GlobalBoard(LatestEvent, Entries)`, `CameraEntry(Rank, NumberPlate, SpeedKmh, MeasuredAt)`,
`GlobalEntry(Rank, NumberPlate, AverageSpeedKmh, MeasurementCount, MeasuredAt)` (times as `DateTimeOffset`, `LatestEvent` nullable).
`Liveness` enum (`NoData`, `Live`, `NotLive`), `BoardStatus(Liveness, Lag?)`, `LivenessEvaluator.Evaluate(generatedAt, latestEvent, threshold)`:
null latest event gives `NoData`, lag below the threshold gives `Live`, otherwise `NotLive`; a negative lag (event ahead of the processor
clock) is treated as zero and live.
Tests: each outcome; lag exactly at the threshold is not live; negative lag; zero threshold (AC-28, AC-32, AC-33, BR-UI-04).

**Step 2: settings.** `ClientSettings` (`BaseUrl`, `PollInterval`, `RequestTimeout`, `LiveLagThreshold`) with the design's defaults and a
`Validate()` that returns all error messages (absolute http or https `BaseUrl`, positive durations).
Tests: defaults; each invalid case; several errors at once.

**Step 3: port, state and poller.** `ISnapshotClient.GetSnapshotAsync(CancellationToken)` returning `TrafficSnapshot` and throwing
`SnapshotUnavailableException` (with a short message, inner exception kept) when no valid snapshot could be obtained. `DashboardState`
(immutable): last `TrafficSnapshot?`, `BoardStatus` for the global board and the ten camera boards, `IsStale`, `LastSuccessAt?`,
`LastError?`, `HasData`. `SnapshotPoller(ISnapshotClient, ClientSettings, TimeProvider, ILogger)` with `State`, `event EventHandler<DashboardState> StateChanged`
and `Task RunAsync(CancellationToken)`: request immediately, then once per `PollInterval` through `PeriodicTimer` on the `TimeProvider`; each
request is bounded by `RequestTimeout` (a timeout counts as a failure, caller cancellation stops the loop); the loop awaits the request
before waiting for the next period, so requests never overlap and missed periods are not queued; on success a new state with fresh
statuses (`LivenessEvaluator` with the snapshot's own `GeneratedAt`) and `IsStale` false; on failure the previous snapshot and statuses
are kept, `IsStale` is true, `LastSuccessAt` unchanged, `LastError` set; the state is raised on every poll; log a Warning when the loop goes from healthy to failing
and an Information when it recovers (not on every poll).
Tests with `FakeTimeProvider` and a fake client (can block, can fail): first request at start; one request per period; a blocked request means
no second request, and when released exactly one request follows, no burst (AC-26, BR-UI-02); failure keeps the last snapshot, sets stale and
the last success time, polling continues, recovery clears the flag (AC-27, BR-UI-03); a request that exceeds `RequestTimeout` is a failure; the
state carries the right statuses for live, frozen and null boards (AC-28, AC-32); cancellation completes `RunAsync` promptly; the logs on state
changes only.

*Push the branch.*

### Phase 2: Infrastructure (`Client.Infrastructure`, tests in `.../Client/Infrastructure`)

**Step 4: `HttpSnapshotClient`.** Implements `ISnapshotClient` with an injected `HttpClient` (the base address is set by the composition root),
`GET api/traffic-snapshot`, deserialization through the source-generated REST context of `Processor.Contracts`, mapping to the Application model in a
separate `SnapshotMapper` (UTC `DateTimeOffset`, null `latestEventUtc`, empty entry lists). Any failure (network error, non-success status,
cancellation of the request by its own timeout, malformed or incomplete JSON, a snapshot without exactly ten camera boards) becomes
`SnapshotUnavailableException` with a short message; cancellation requested by the caller is not wrapped.
Tests with a fake `HttpMessageHandler`: the approved file `docs/contracts/examples/traffic-snapshot.json` maps correctly (add it to the test project
as linked content copied to the output directory unless it already is: ten boards, camera 10 with null latest event, camera 7 frozen,
global entries with counts); request path and method; 503 and 500 with problem details, malformed JSON, nine boards, handler throwing
`HttpRequestException` all give `SnapshotUnavailableException`; caller cancellation propagates as `OperationCanceledException`.

*Push the branch.*

### Phase 3: ViewModels (`Client.ViewModels`, tests in `.../Client/ViewModels`)

Add `CommunityToolkit.Mvvm` and `Microsoft.Extensions.Logging.Abstractions`. No WPF reference.

**Step 5: dispatcher port and row view models.** `IUiDispatcher.Post(Action)`; `EntryViewModel` (rank, plate, score text, detail text, time text)
built from a `CameraEntry` or a `GlobalEntry`; presentation formatting in one place (speed `0.0` km/h, time `HH:mm:ss.f` in local time, invariant
culture).
Tests: formatting and rounding including .05 boundaries and large and small values; local time conversion with a fixed time zone passed in.

**Step 6: board view model.** `BoardViewModel` (title, `Entries`, `Liveness`, `IsLive`, `IsDimmed`, `LatestEventText`, `AgeText`, `EmptyText`) updated from a board and its
`BoardStatus`: no data ("no data yet"), empty with a latest event ("no live entries"), live, not live with the age ("latest event 12.4 s old").
Tests: each display state (AC-28, AC-32, AC-33).

**Step 7: `MainViewModel`.** Subscribes to `SnapshotPoller.StateChanged` (taking the poller or an `IDashboardStateSource` abstraction of it, whichever keeps
tests simplest; note the choice), copies state into bound properties **only inside `IUiDispatcher.Post`**; exposes `Global` and `Camera`
board view models, `CameraOptions` (1 to 10), `SelectedCamera` (default 1; changing it re-renders `Camera` from the last state without any request), `IsStale`,
`StatusText` (waiting for data / live data / stale with the last success time and the reason), `LastSuccessText`, and disposes its subscription. Use
`[ObservableProperty]` and `[RelayCommand]` where they fit.
Tests with a fake dispatcher (inline and a recording one): initial waiting state; a state fills the global board and the selected camera board
(AC-25, BR-UI-01); switching the camera re-renders from the last state and triggers no request (AC-25); stale display keeps the old rows and shows the last
success time and the reason, recovery clears it (AC-27, BR-UI-03); per-board liveness marking (AC-28, AC-32, AC-33); property changes happen only inside
the dispatcher (the recording dispatcher holds the work until it is run, nothing changes before); unsubscribes on dispose.

*Push the branch.*

### Phase 4: Wpf (`Client.Wpf`)

**Step 8: composition root.** `App.xaml.cs` builds a generic host (configuration from `appsettings.json` section `Client`, environment variables and command
line; logging to the debug and console providers; DI), validates `ClientSettings` (message box with all errors and exit code 1 when invalid), creates one
long-lived `HttpClient` with the base address, `HttpSnapshotClient`, `SnapshotPoller`, `MainViewModel`, `DispatcherUiDispatcher : IUiDispatcher`
(`Dispatcher.BeginInvoke`), starts the poller loop on a background task with a cancellation token that is cancelled on exit, and shows the window.
`appsettings.json` with the design's defaults, copied to the output directory. Remove the placeholder window text.

**Step 9: views.** `Views/MainWindow.xaml`: a status bar (connection state: waiting, live, stale with the last success time and reason; clear color change when stale); two
panels side by side: *Global leaderboard* and *Camera leaderboard* with a `ComboBox` bound to `CameraOptions` and `SelectedCamera`; each panel shows its title, the
`LatestEventText` and `AgeText`, the ten rows in a read-only `ListView` or `DataGrid` (rank, plate, speed, extra column, time) with monospaced plates, and the empty text
when there are no rows; a board that is not live is dimmed (opacity) through a style trigger on `IsDimmed`, with the age text readable. Bind only to view model
properties; no logic in code-behind except `InitializeComponent` and setting `DataContext`. Window around 900x560, resizable, sensible minimum size. Keep the styling modest
and consistent; no external UI libraries.

**Step 10: manual smoke run.** Start the stack (`docker compose up -d --build`), run the generator
(`dotnet run --project src/Generator/HighwaySpeedEnforcement.Generator -- --Generator:Seed=42 --Generator:MaxTicks=120`) and start the client
(`dotnet run --project src/Client/HighwaySpeedEnforcement.Client.Wpf`). Observe: boards fill and refresh about every 3 s, the camera selector works, after the generator
ends the boards freeze and are marked not live (the processor boards freeze, BR-LB-12), `docker compose stop processor` makes the client stale with the last success time and
`docker compose start processor` recovers it. If the environment allows capturing the screen (for example PowerShell with `System.Drawing` `CopyFromScreen` of the window
region, or the window's own `RenderTargetBitmap`), save one screenshot of the live state and one of the stale state under `docs/client/` and look at them to check the
layout (they are part of the review). If a UI cannot be started in your session, say so in the notes and the report; do not fake the result.

*Push the branch.*

### Phase 5: whole-system test and wrap-up

**Step 11: optional E2E.** If the processor's E2E fixtures make it cheap (a running processor container fed by a test publisher), add one E2E test
(`Category=E2E`) in `HighwaySpeedEnforcement.E2E.Tests/Client` that reads a real snapshot through `HttpSnapshotClient` and checks that it maps (ten boards, entries ranked).
Otherwise record in the notes why it was skipped.

**Step 12: README and wrap-up.** Create `README.md` at the repository root (concise, English): what the three applications do, a diagram or a short list of the data flow,
how to build and test, how to start the whole system (`docker compose up -d --build`, the generator command, the client command), the settings that matter for a demo (window,
linger, inactivity timeout via `Processor__...` environment variables; generator `DisabledCameras`, `Heartbeats:Enabled`, `SpawnUntilTick`; client `Client__BaseUrl`), a pointer to
`docs/` (assignment, ambiguity register, domain, ADRs, designs, plans, testing strategy), and an honest "known limitations" list from the notes files (tier 2 scope, no durable recovery,
single instance, clock agreement assumption). Update `CLAUDE.md`: the workflow section's status lines (component designs and implementations are done for the processor,
generator and client), remove the statement that the generator and client are skeletons, and add a short "Client (implemented)" section like the generator's. Run the full test suite
and write the final report (section 7).

*Push the branch.*

## 6. Guidance on details the design leaves open

- **Class shapes.** Immutable records for the model; view models as `partial` classes deriving from `ObservableObject`; no view model references `System.Windows`.
- **Threading.** The poller's `StateChanged` is raised on the background thread; handlers must not touch UI-bound objects.
- **Replacing rows.** Replace the `Entries` contents in one go (clear and add inside one dispatcher call); for ten rows no diffing is needed.
- **Time zone.** Convert event times to local time with `TimeZoneInfo` passed into the formatter, so tests do not depend on the machine's zone.
- **Shutdown.** Cancel the poller token and wait briefly for the loop on `OnExit`; disposing the `HttpClient` last.
- **Logging.** `ILogger` with message templates, no string interpolation.

## 7. Final report (end of step 12)

A short message to the owner containing: steps completed and steps skipped with the reason; test counts per project; the list of deviations and open questions from the notes
file; what the manual smoke run showed (and the screenshots, or why they are missing); anything in the documents that turned out to be wrong or unclear; the exact commands to
run the system and the tests.
