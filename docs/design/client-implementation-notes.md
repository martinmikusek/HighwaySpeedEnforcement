# Client: implementation notes

Deviation log kept during the implementation of `client-implementation-plan.md`. One section per step: what was done that is
not literally in the design (a deviation, a decision on a detail the design leaves open, a problem found), with the reason.

## Deviations by step

### Step 0: setup

- Worktree `../Yunex.HighwaySpeedEnforcement-client` on `feature/client` from `origin/main` b4a6bd8. Baseline: build and
  format check green; 395 tests green (unit, component and E2E against Docker, 52 s).

### Step 1: model and liveness

- `TrafficSnapshot.CameraCount` (10) is a constant on the model, so `DashboardState` and the tests do not repeat the number.
- `BoardStatus.NoData` is a shared instance (null lag); `LivenessEvaluator` returns it for a null latest event.

### Step 2: settings

- `ClientSettings.SectionName` = `Client`, as the generator's settings expose theirs. Validation messages follow the
  generator's wording (`<Name> must ... (was ...)`), so the message box of step 8 reads like the generator's console output.

### Step 3: port, state and poller

- `RunAsync` completes **without throwing** when the caller's token is cancelled (the plan says "cancellation stops the loop"
  but not how); the host then awaits it on exit without a try/catch. Cancellation while a request is running is observed
  through the linked token, so the loop ends promptly.
- The request timeout is a `CancellationTokenSource(RequestTimeout, TimeProvider)` linked with the caller's token, so the
  underlying request is cancelled, not abandoned. An `OperationCanceledException` with the caller's token not cancelled is
  the timeout (reason `request timed out after 10 s`).
- The poller catches only `SnapshotUnavailableException` and the timeout; anything else is a bug of the adapter (which
  wraps every failure, step 4) and would end the loop. Not widened on purpose.
- `DashboardState.Initial` is the state before the first poll (no data, not stale); a failure before the first success is
  stale without data, so the view model can show "waiting for data" together with the reason.
- `LastSuccessAt` is the client's clock (`TimeProvider.GetUtcNow()`) at the moment of success, not the snapshot's
  `GeneratedAt`: the design shows it as "time of the last success" on the client side (decision 6).
- Tests: `FakeSnapshotClient` (return, fail, block until released, honoring cancellation) and a state recorder with bounded
  `WhenCalledAsync` / `WhenStatesAsync` waits (10 s), as the generator's runner tests do; no sleeps. The blocked-request
  test advances the fake clock by three periods and checks that exactly one request follows the release.

### Step 4: HttpSnapshotClient

- Failure messages are short and typed, not the exception text: `processor answered 503 Service Unavailable`,
  `processor unreachable (ConnectionError)` (the `HttpRequestError` of the exception), `response is not a valid snapshot`,
  `request timed out`, `snapshot has 9 camera boards, expected 10`. The cause stays as the inner exception for the log.
- The problem-details body of a 503/500 is not read; the status line is enough for the status bar.
- `SnapshotMapper` throws `SnapshotUnavailableException` itself for an incomplete DTO (a null list or plate, which the REST
  context does not reject because it has no required-member check) or a wrong number of boards; the client has no second
  wrapping step.
- Times are passed through `ToUniversalTime()` although the contract's converter already normalizes, so the model's "UTC"
  does not depend on the converter.
- The `HttpClient.Timeout` case is tested by a handler that throws the `TaskCanceledException` with a `TimeoutException`
  inside while the caller's token is not cancelled, exactly what `HttpClient` throws; no real waiting. The poller's own
  timeout (step 3) is the one that is normally hit, because the host leaves `HttpClient.Timeout` at its default of 100 s.

### Step 5: dispatcher port and row view models

- Formatting lives in `DisplayFormatter(TimeZoneInfo)` (speed, event time `HH:mm:ss.f`, time of day `HH:mm:ss`, age
  `12.4 s`); `DisplayFormatter.Local` is the production instance, tests pass a custom fixed zone (UTC+02) so they do not
  depend on the machine's zone or on the system's zone database.
- `EntryViewModel` is an immutable plain class (not an `ObservableObject`): rows never change, a board replaces them.
  `DetailText` is the measurement count on a global row and empty on a camera row; the view decides which columns to show.
- Speed rounding is `ToString("0.0")` on the exact binary value (midpoint away from zero on the exact decimal expansion:
  149.75 reads 149.8). The score text carries no unit; the column header does.

### Step 6: board view model

- Texts: `LatestEventText` = `Latest event: 16:03:09.4` or `Latest event: none`; `AgeText` = `live`, or
  `not live, latest event 12.4 s old`, or empty without data; `EmptyText` = `no data yet` / `no live entries` / empty
  (with `IsEmpty` for the view's visibility binding). `IsDimmed` is true for `NotLive` **and** `NoData` (the design dims a
  board that is not live; a board without data is not live either).
- `Title` is observable because the camera board's title follows the selection (`Camera 5`).

### Step 7: MainViewModel

- The view model takes **`IDashboardStateSource`** (an interface in Application with `State` and `StateChanged`, implemented
  by `SnapshotPoller`) instead of the poller: the tests then raise states by hand without a loop, a clock or a fake client.
- No `[RelayCommand]`: the only user action is the camera selection, which is a bound property; a command would be a
  forwarding layer. `[ObservableProperty]` is used for every bound scalar.
- If the source already has a state when the view model is constructed (the poller started first), that state is applied
  through the dispatcher as well, so the "only inside `Post`" rule holds for the first state too.
- `StatusText` wording: `Waiting for data`, `Waiting for data: <reason>` (failure before the first success),
  `Connected, snapshot generated HH:mm:ss.f` (not "live data" as the design's status list says: the boards next to it
  carry their own liveness and "live data" over "not live" boards read as a contradiction in the first screenshot),
  `Connection lost, showing data from HH:mm:ss: <reason>` (the wording of the stale banner in the brand brief, changed in
  step 9); `LastSuccessText` is `Last success HH:mm:ss` in local time (design decision 6: a time of day, no ticking age).
- The camera board of a stale state keeps its liveness from the last successful snapshot (design: liveness is a property
  of the snapshot; the stale indicator covers the failed poll).

### Step 8: composition root

- `Host.CreateApplicationBuilder(e.Args)` gives the configuration sources of the design for free (`appsettings.json`,
  environment variables, command line); the host is built but never started: there are no hosted services, the poller
  runs on a `Task.Run` with the app's own `CancellationTokenSource`, cancelled in `OnExit` and awaited for at most 2 s.
- `StartupUri` is removed; `MainWindow` is resolved from the container (its constructor takes the view model and sets
  `DataContext`, the only code in the code-behind) and shown from `OnStartup`.
- The console logging provider is registered as the plan says, but a `WinExe` has no console, so only the debug provider
  is visible (in the debugger's output window). Not changed; the plan asks for both.
- `HttpClient.Timeout` stays at its default (100 s): the poller's `RequestTimeout` bounds every request.

### Step 9: views (with a custom look, owner's extra requirement)

- **The logo is a parody "NOT YUNEX" wordmark of our own (text in the Segoe UI Black font, no third-party asset); it replaced
  the original Yunex Traffic logo before the repository was made public.** The mint/slate
  palette as brushes (`Resources/Colors.xaml`), everything else in `Resources/Styles.xaml`, merged in `App.xaml`.
- Layout as in the brand brief: dark header with the white logo, the title and a connection pill (WAITING grey,
  CONNECTED mint, STALE red) with the last success time under it, a mint line under the header, a red stale banner with
  the view model's `StatusText`, two white cards with rounded corners and a soft shadow, rank 1 to 3 in mint badges, a live
  dot (mint and pulsing through a storyboard in a style trigger on `Liveness`, amber when not live, grey without data)
  next to the age text in the card header, the card body dimmed to 0.55 opacity through a trigger on `IsDimmed` (the
  header with the age text stays readable), the `ComboBox` inside the camera card's title with a blue highlight, and a
  footer status bar with `StatusText`. No code-behind beyond `InitializeComponent` and `DataContext`; no external library.
- Window 1040 x 600 (minimum 900 x 440) instead of the plan's "around 900 x 560": with the global board's five columns
  the time column was clipped at 980 in the first screenshot.
- Rows are not selectable (`IsHitTestVisible=false`, `Focusable=false` on the items) so the system highlight never
  competes with the brand colors; the lists still scroll.
- The pulse animation is the only "fun" beyond the badges and the tagline; nothing in the colors carries domain meaning
  (brand brief: the domain has no speed limit, so no "speeding" color).

### Step 10: manual smoke run

- Environment: the compose stack of the main checkout (broker, processor), the generator from this worktree
  (`--Generator:Seed=42 --Generator:MaxTicks=120`, then a second run with seed 43 and 45 ticks after the layout fix), the
  client started from its build output. The session can show a window, but a screen capture returned black (no visible
  desktop), so the screenshots were taken with `PrintWindow(PW_RENDERFULLCONTENT)` on the window handle (a PowerShell
  script outside the repository), which renders the window's composed content. They are in `docs/client/`.
  Retaken on 2026-10-15 with the application icon and the refresh selector; the states are the same.
- Observed, in order: `dashboard-empty.png` (processor up without data: CONNECTED pill, grey dots, "no data yet",
  "Latest event: none"); `dashboard-live.png` about 30 s into the generator run (both boards live, mint dots, rows
  refreshing with every poll, "Last success" advancing every 3 s); `dashboard-not-live.png` 8 s after the generator
  ended (the processor boards froze, BR-LB-12: both boards show "not live, latest event 6.9 s old" with amber dots and a
  dimmed body while the pill still says CONNECTED and the status bar shows new snapshots being generated);
  `dashboard-stale.png` 14 s after `docker stop processor` (STALE pill, red banner "Connection lost, showing data from
  18:12:13: processor unreachable (ConnectionError)", the last rows kept, their liveness unchanged); `dashboard-recovered.png`
  16 s after `docker start processor` (CONNECTED again, the restarted processor has no data, so both boards read "no
  data yet"). The client process stayed responsive through all of it (the window kept repainting; it was stopped by the
  script at the end).
- **Not observed:** the camera selector in the real window. An attempt to drive it with keyboard automation (Tab to the
  `ComboBox`, arrow keys) did not change the selection, and clicking is not possible from this session; the behavior is
  covered by the view model tests (AC-25). The owner should click through the cameras once.
- Layout fixes from the screenshots: the window grew from 980 x 600 to 1040 x 660 (the global board's time column was
  clipped, and with the stale banner the ten rows needed scrollbars); the "Live data" status wording became "Connected".
- The 0.55 opacity reads as a clear but subtle dimming in the captures; the amber dot and the age text carry the message.

### Step 11: E2E

- Done, it was cheap: `tests/HighwaySpeedEnforcement.E2E.Tests/Client/HttpSnapshotClientE2ETests.cs` uses the processor's
  `PipelineStack` and `Scenario` (a fresh container, three vehicles through three cameras via the test publisher) and
  reads the snapshot through the real `HttpSnapshotClient` with its own `HttpClient`: ten boards in order, ranked global
  and camera entries, counts, watermarks, UTC offset; a second test points the client at a closed port and expects the
  port's failure type. `ProcessorContainer` got one additive property (`BaseAddress`) and the E2E project a reference to
  `Client.Infrastructure`.

### Step 12: README and wrap-up

- `README.md` at the root: the three applications, the data flow, build and test commands, how to run the whole system,
  the demo settings, the documentation map and the known limitations (collected from the three notes files and
  `AMB-06`). `CLAUDE.md`: the workflow status lines, the skeleton statement removed, a "Client (implemented)" section.
- Final run: build and format clean; 472 tests green (unit 417, component 15, E2E 40 against Docker). One of four
  full-solution runs reported a single failure (471 of 472) whose name was not captured (the output was filtered to the
  summary); the three other runs, before and after it, were clean, as were the per-project runs. The client's own tests
  drive time with `FakeTimeProvider` and awaited signals, so the suspect is a Docker-timed E2E test; it was not
  reproduced in the two further full runs with the complete output saved.

## Open questions

None. Two points for the owner's review rather than questions: the healthy status text says "Connected" instead of the
design's "live data" (step 7 note), and the camera selector was not exercised in the real window (step 10 note).

### Selectable refresh period and the application icon (owner's requests, 2026-10-14)

- The status bar has a "Refresh every" selector (1, 2, 3 (default), 5 s, plus the configured period when it is not among them). The view model passes the choice to the poller
  through `IPollIntervalControl` (Application, implemented by `SnapshotPoller`), which sets `PeriodicTimer.Period`: the next request follows one new period after the change,
  requests still never overlap (AC-26). The default stays the assignment's 3 s (BR-UI-02, extension). `MainViewModel` takes the control as an optional constructor parameter.
- The window and the executable have an icon (`Resources/AppIcon.ico`: a mint roadside speed camera post, with the camera head (lens and flash) on top, on a dark square), generated
  by `tools/make-app-icon.ps1` (System.Drawing, no package); the previous purple .NET icon came from the `dotnet` host process.
