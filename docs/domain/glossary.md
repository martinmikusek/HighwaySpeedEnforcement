# Glossary (ubiquitous language)

Use these terms exactly in code, tests, docs and conversation. Names in `code font` are the intended identifiers.
Rules referenced as `BR-*` are in `business-rules.md`; ambiguity decisions as `AMB-*` in `../assignment-ambiguities.md`.

## Road and traffic

| Term | Definition |
|---|---|
| **Camera** | One of ten fixed measurement points on the highway, identified by `CameraId` 1–10. Vehicles pass them in ascending order. |
| **Vehicle** | A road vehicle identified by its **NumberPlate**. A plate identifies exactly one passage and is never reused (BR-SIM-03). |
| **NumberPlate** | `string` identifier of a vehicle. Compared ordinally, case-sensitively (BR-LB-05). |
| **Passage** | A vehicle's journey from Camera 1 to Camera 10. A passage has at most 10 measurements. |
| **Tick** | One one-second cycle of the generator: spawn, advance and emit, remove (BR-SIM-02). |

## Telemetry

| Term | Definition |
|---|---|
| **Measurement** | One telemetry message: `NumberPlate`, `TimestampTicks`, `CurrentSpeed`, `CameraId` (BR-TEL-01). |
| **CurrentSpeed** | Instantaneous speed in km/h as `float`, measured by one camera. |
| **TimestampTicks** | `long` UTC `DateTime.Ticks` at measurement time (BR-TEL-02). Also called the measurement's **event time**. |
| **Accepted measurement** | A measurement that passed validation and the sequence rule and changed state (BR-ING-01, BR-ING-02). |
| **Rejected measurement** | A measurement that was not accepted. Reasons: `InvalidPayload`, `OutOfSequence`. It changes no state and is counted and logged (BR-ING-07). |

## Processing state

| Term | Definition |
|---|---|
| **Vehicle track** | Working state of one vehicle between its first and last accepted measurement: `NumberPlate`, measurement count, speed sum, last `CameraId`, last timestamp. Deleted when the vehicle completes or times out (BR-ING-04..06). |
| **In-flight vehicle** | A vehicle that has a vehicle track. |
| **Completed vehicle** | A vehicle whose Camera 10 measurement was accepted. Its vehicle track is deleted immediately (BR-ING-05). |
| **Orphaned vehicle** | An in-flight vehicle that stopped sending measurements before Camera 10 (BR-ING-06). |
| **Cumulative average** | Arithmetic mean of a vehicle's accepted `CurrentSpeed` values: speed sum divided by count. Not a moving average and not distance/time (BR-ING-04). |
| **Eligible vehicle** | An in-flight vehicle with at least 3 accepted measurements, or a completed vehicle within the global linger (BR-LB-03, BR-LB-08). |

## Leaderboards

| Term | Definition |
|---|---|
| **Leaderboard** (board) | A ranked list of at most 10 entries presented to the client. Two kinds: camera and global. |
| **Camera leaderboard** | One of 10 boards, ranking measurements made at one camera by `CurrentSpeed` (BR-LB-01). |
| **Global leaderboard** | One board ranking vehicles by cumulative average (BR-LB-02). |
| **Entry** | One row of a board: `NumberPlate`, `Score`, `Timestamp` (exposed as `MeasuredAtUtc` in the snapshot). Self-contained; it does not depend on a vehicle track (BR-LB-06). |
| **Score** | Camera board: the measurement's `CurrentSpeed`. Global board: the vehicle's cumulative average. |
| **Rank order** | Score descending, then `NumberPlate` ascending (BR-LB-05). |
| **Candidate** | Any live entry held internally, including those ranked below 10th. Only the top 10 are presented. |
| **Watermark** | The newest timestamp of valid measurements seen in a scope: per camera for a camera board, overall for the global board. Stamped on each message in arrival order, never decreases (BR-LB-11). |
| **Live entry** | An entry younger than its window or linger relative to the watermark (BR-LB-07, BR-LB-08). |
| **CameraBoardWindow** | How long a camera entry stays live. Positive; infinite allowed (BR-CFG-01). |
| **GlobalBoardLinger** | How long a completed vehicle's entry stays on the global board. Zero or more; infinite allowed (BR-CFG-01). |
| **InactivityTimeout** | Silence after which an in-flight vehicle is orphaned. Finite (BR-CFG-01). |
| **Expiry** | Removal of an entry that stopped being live. |
| **Dominating entry** | An entry that is better, has a fixed score and stays live at least as long as another entry. 10 of them make the other entry prunable (BR-LB-10). |
| **Pruning** (purge) | Discarding an entry that can never again reach the top 10. Optimization only (BR-LB-10). |
| **Freeze** | Behavior during silence: with no new measurements the watermarks do not advance, so boards keep their last content (BR-LB-12). |

## API and client

| Term | Definition |
|---|---|
| **Traffic snapshot** | The detached, coherent DTO returned by `GET /api/traffic-snapshot` (BR-API-01, BR-API-02). |
| **GeneratedAtUtc** | UTC time at which the snapshot was generated. |
| **LatestEventUtc** | Per board in the snapshot: the event time (camera `TimestampTicks`) of the newest valid message the board reflects, that is, the board's watermark. Null for a board that never received data. It is **not** the time of the snapshot (that is `GeneratedAtUtc`); their difference is the lag. |
| **Live** | A client-side judgment: a board is live when `GeneratedAtUtc − LatestEventUtc` is below the client's `LiveLagThreshold`. The snapshot itself carries no such flag (BR-UI-04). |
| **Lag** | `GeneratedAtUtc − LatestEventUtc` of a board (processor clock minus camera event time); the client uses it to decide whether the board is live, it never affects state. |
| **Heartbeat** | Optional message from a camera that sees no vehicle: "alive, nothing before this timestamp is outstanding". Advances the watermarks (BR-TEL-03). |
| **Stale** | Client state: the last poll failed, so the displayed snapshot is older than one polling cycle (BR-UI-03). Not the same as a frozen board, which is shown with its `LatestEventUtc` age. |
