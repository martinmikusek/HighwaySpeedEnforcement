# Client: component design

**Status:** Implemented (written while the owner was away, used and accepted by the owner afterwards; the decisions in section 7 may be revisited).
Binding inputs: the assignment PDF (section 3), ADR-0004 (layers, MVVM mapping), ADR-0002 (WPF, CommunityToolkit.Mvvm),
`docs/domain/` (`BR-UI-01..04`, `BR-API-01`, `AC-25..28`, `AC-32`, `AC-33`), `docs/contracts/rest-api.md`.

## 1. Purpose and scope

A WPF dashboard that polls the processor's `GET /api/traffic-snapshot` and shows the **global top 10** and the **top 10 of a camera
chosen by the user**. It marks data that is stale (the processor is unreachable) or not live (the boards are frozen or late). It
has no influence on the processor (BR-UI-03). Out of scope: charts, history, settings dialogs, authentication, localization.

## 2. Behavior

- **Polling (BR-UI-02).** A background loop requests a snapshot at a nominal period of 3 s (`PollInterval`; the user can switch it in the status bar to 1, 2, 3 or 5 s, or the configured value, and the change applies at once), the first request at
  start. Requests never overlap: the next request waits for the previous one to finish; periods that were missed while a request was
  running are skipped, not queued (`PeriodicTimer` coalesces them). Each request has `RequestTimeout` (default 10 s, longer than the period on
  purpose: a slow response is tolerated, AC-26). Only the update of UI-bound state runs on the UI thread.
- **Stale (BR-UI-03, AC-27).** If a request fails (network error, timeout, non-success status, undecodable body) the client keeps the last
  successful snapshot on screen, shows a clear *stale* indication with the time of the last success and a short reason, and keeps
  polling. The first successful poll afterwards clears it. Before the first success the screen says *waiting for data*.
- **Live (BR-UI-04, AC-28, AC-32).** For every displayed board the client computes `lag = generatedAtUtc - latestEventUtc` of the
  **same snapshot**. Lag below `LiveLagThreshold` (default 3 s) is *live*; otherwise *not live*; a `latestEventUtc` of null is *no data*. A board
  that is not live is shown dimmed with its data age ("latest event 12.4 s old"); it is never presented as live. Liveness is a
  property of the snapshot, so a failed poll does not change it (the stale indicator covers that). Lag is meaningful only when the generator's
  and the processor's clocks agree (same machine or synchronized machines); the UI shows the numbers it computed.
- **Camera selection (BR-UI-01, AC-25).** A selector with Camera 1 to 10 (default Camera 1). Changing it shows that camera's board from the
  latest snapshot immediately, without a request; the selection survives polls.
- **Presentation.** Speeds are rounded to 0.1 km/h for display only (`BR-LB-05`: ranking uses unrounded values, the server already ranks);
  numbers use the invariant culture. Global rows: rank, number plate, average speed, number of measurements, time of the latest measurement.
  Camera rows: rank, number plate, speed, time of the measurement. Times are the event times from the snapshot shown in local time (`HH:mm:ss.f`).
  An empty board with a `latestEventUtc` reads "no live entries"; a board without data reads "no data yet".

## 3. Structure (ADR-0004, four projects, kept as decided)

| Project | MVVM role | Contents |
|---|---|---|
| `HighwaySpeedEnforcement.Client.Application` | Model | `TrafficSnapshot` and its board and entry records (the client's own model, not the wire DTOs), `BoardStatus` and `LivenessEvaluator`, `ClientSettings` with validation, port `ISnapshotClient`, `DashboardState` (last snapshot, statuses, stale flag, last success, last error), `SnapshotPoller` (the loop; owns the state; raises `StateChanged`) |
| `HighwaySpeedEnforcement.Client.Infrastructure` | adapter | `HttpSnapshotClient : ISnapshotClient` (`HttpClient`, `System.Text.Json` with the source-generated REST context of `Processor.Contracts`), mapper from the wire DTOs to the Application model |
| `HighwaySpeedEnforcement.Client.ViewModels` | ViewModel | `MainViewModel`, `BoardViewModel`, `EntryViewModel`, camera options, `IUiDispatcher` port; CommunityToolkit.Mvvm; no WPF reference |
| `HighwaySpeedEnforcement.Client.Wpf` | View | `Views/MainWindow.xaml`, `App` (composition root with the generic host: configuration, logging, DI), `DispatcherUiDispatcher : IUiDispatcher`, `appsettings.json` (section `Client`) |

Dependencies as in `DependencyRuleTests` (unchanged): Application has no project reference; Infrastructure references Application and `Processor.Contracts`;
ViewModels references Application; Wpf references the other three. The merge of Application, ViewModels and Infrastructure that ADR-0004
leaves open is **not** done: the layers exist, are enforced by the architecture tests and cost nothing.

### Data flow

`SnapshotPoller` (background task) calls `ISnapshotClient`, builds the new `DashboardState` (applying `LivenessEvaluator` to the ten camera
boards and the global board) and raises `StateChanged` on its own thread. `MainViewModel` subscribes and, through `IUiDispatcher`, copies the
state into its bound properties on the UI thread. Selecting a camera re-renders from the last state held by the view model.

### UI thread rule

The loop and the HTTP call never run on the UI thread; view-model properties and collections change only inside `IUiDispatcher.Post`. The
WPF adapter uses `Dispatcher.BeginInvoke`; tests use an inline dispatcher and a recording one that proves the marshaling.

## 4. Configuration

Section `Client` of `appsettings.json` (overridable by environment variables `Client__BaseUrl` and command line):

| Setting | Default |
|---|---|
| `BaseUrl` | `http://127.0.0.1:8080` |
| `PollInterval` | `00:00:03` |
| `RequestTimeout` | `00:00:10` |
| `LiveLagThreshold` | `00:00:03` |

Validation at start: absolute http(s) `BaseUrl`, positive durations. Invalid settings show a message box and exit with code 1.

## 5. Observability

`ILogger` to the debug and console providers: poll failures (Warning, with the reason, once per change of state, not every 3 s while down),
recovery (Information). No metrics.

## 6. Test map

| Area | Level | Scenarios |
|---|---|---|
| `LivenessEvaluator` | unit | live, not live, no data, boundary at the threshold (AC-28, AC-32, BR-UI-04) |
| `ClientSettings` | unit | defaults, validation messages |
| `SnapshotPoller` | unit, `FakeTimeProvider` and a fake `ISnapshotClient` | first request at start; one request per period; slow request blocks the next and missed periods are not queued (AC-26, BR-UI-02); failure keeps the last snapshot, sets stale with the last success time, polling continues, recovery clears it (AC-27, BR-UI-03); timeout counts as failure; cancellation stops the loop |
| `HttpSnapshotClient` and mapper | unit, fake `HttpMessageHandler` | the approved file `docs/contracts/examples/traffic-snapshot.json` maps to the model (ten boards, null `latestEventUtc`, empty board, UTC times); 503, 500, malformed JSON, network error, cancellation become the port's failure type; the request goes to `BaseUrl` + `/api/traffic-snapshot` |
| view models | unit | initial state ("waiting"); a state fills the global board and the selected camera board with the right texts (AC-25); selecting a camera re-renders without a request; stale and recovered display (AC-27); live, not live and no data marking per board (AC-28, AC-32, AC-33); rounding; updates happen only inside the dispatcher |
| composition and window | none automated | no UI automation (testing strategy); one manual smoke run against the real stack with a screenshot |
| client against the real processor | E2E (Docker) | optional: `HttpSnapshotClient` reads a real snapshot of the processor container fed by a test publisher |

## 7. Decisions recorded here (owner may revisit)

1. Four projects stay (no merge); 2. `RequestTimeout` 10 s so that a response slower than one period is tolerated rather than aborted;
3. liveness is computed from the snapshot's own `generatedAtUtc`, never from the client's wall clock; 4. numbers use the invariant culture;
5. one long-lived `HttpClient`; 6. no ticking "age" timer: the last success time is shown as a time of day and refreshed with each poll result.
