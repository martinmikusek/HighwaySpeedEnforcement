# Business rules

Each rule has an ID, a statement and a provenance:

- **Assignment** — stated by the PDF.
- **Interpretation (AMB-xx)** — our resolution of an ambiguity; see `../assignment-ambiguities.md`.
- **Extension (tier 2)** — added by us under the decided scope (AMB-06); not requested by the PDF.

Terms are defined in `glossary.md`. "Shall" describes required behavior. Tests and code that enforce a rule cite its ID.
All ambiguities behind the rules are decided (`../assignment-ambiguities.md`); a rule names the entry it interprets.

## Simulation (Generator)

| ID | Rule | Provenance |
|---|---|---|
| BR-SIM-01 | The road has ten cameras in one direction. Every vehicle passes Camera 1 to Camera 10 in order. Lanes, spacing and image recognition are not modeled. | Assignment |
| BR-SIM-02 | Once per second the generator performs, in this order: (1) create exactly 100 vehicles with unique plates and counter 0; (2) advance every active vehicle by one camera and emit its measurement; (3) remove vehicles that have emitted Camera 10. | Assignment, Interpretation AMB-03 |
| BR-SIM-03 | Number plates are unique within a run and never reused, including after a generator restart. | Interpretation AMB-08 |
| BR-SIM-04 | Every vehicle emits exactly 10 measurements, `CameraId` 1 to 10 in order, and never Camera 11. In steady state a tick emits 1,000 measurements, 100 per camera. | Assignment, Interpretation AMB-03 |
| BR-SIM-05 | Speeds are `float` km/h and vary plausibly between consecutive cameras around a highway speed of about 130 km/h. Distribution and limits are parameters. | Assignment |

## Telemetry

| ID | Rule | Provenance |
|---|---|---|
| BR-TEL-01 | A measurement contains `string NumberPlate`, `long TimestampTicks`, `float CurrentSpeed`, `int CameraId` (1–10). | Assignment |
| BR-TEL-02 | `TimestampTicks` is UTC `DateTime.Ticks` at measurement time, not processor receipt time. It drives expiry only, never the order of one vehicle's measurements. | Interpretation AMB-08 |
| BR-TEL-03 | **Heartbeat (optional extension, described design).** A camera may send a heartbeat (`CameraId`, `TimestampTicks`) when it sees no vehicle. A valid heartbeat advances that camera's and the global watermark like a measurement but carries no vehicle data and changes no vehicle track. A quiet but healthy camera therefore lets its board expire normally (an empty board means no traffic), and the absence of both heartbeats and measurements marks the camera offline. The message contract reserves a message type for it; implementing it is a later decision. | Extension (optional), AMB-05 |

## Ingestion (Processor)

| ID | Rule | Provenance |
|---|---|---|
| BR-ING-01 | A measurement is invalid and rejected if the plate is null or empty, `CameraId` is outside 1–10, `CurrentSpeed` is negative or not finite, `TimestampTicks` is not a valid UTC tick value, or the timestamp is further than `FutureTimestampTolerance` ahead of the processor's clock (one far-future timestamp would otherwise expire every board, because watermarks never decrease). | Extension (tier 2), AMB-07, ADR-0003 |
| BR-ING-02 | Sequence rule: cameras of one vehicle must form a strictly increasing sequence. A plate without a vehicle track is accepted at **any** camera (the vehicle's earlier cameras may be dead or their messages lost) and starts a track; a plate with a vehicle track is accepted only if `CameraId > lastCameraId` (gaps are tolerated). `CameraId ≤ lastCameraId` is rejected as `OutOfSequence`; this rejects repeated, late and reordered messages. A missing camera never blocks later cameras. | Interpretation AMB-04, AMB-07 |
| BR-ING-03 | Measurements of the same plate are applied one at a time, in arrival order. Measurements of different plates may be processed concurrently. | Assignment (thread safety), Interpretation AMB-04 |
| BR-ING-04 | Accepting a measurement increments the vehicle's count by 1 and adds `CurrentSpeed` to its sum. The cumulative average is sum divided by count. | Assignment, Interpretation AMB-08 |
| BR-ING-05 | Accepting the Camera 10 measurement completes the vehicle: after leaderboards are updated, its vehicle track is deleted. | Assignment (Purge), Interpretation AMB-03 |
| BR-ING-06 | A vehicle is orphaned when `globalWatermark − lastTimestamp ≥ InactivityTimeout`. Orphaning is driven by *other* vehicles' messages advancing the watermark, not by the silent vehicle: (a) its global entry expires with `ExpiresAt = lastTimestamp + InactivityTimeout` once the watermark passes it (BR-LB-13); (b) its track is freed by a periodic per-worker sweep; (c) if a message of that plate does arrive, the watermark stamped on it decides whether the track is still valid, so the outcome is the same whether or not the sweep has run (BR-LB-11). Its camera entries remain until their own window ends. If no valid message arrives at all, the watermark does not advance and nothing is orphaned (freeze, BR-LB-12). | Interpretation AMB-05, ADR-0003 |
| BR-ING-07 | Every rejected measurement is counted by reason and logged (not at Information level for every normal measurement); it changes no state. | Extension (tier 2), AMB-07 |

## Leaderboards

| ID | Rule | Provenance |
|---|---|---|
| BR-LB-01 | There are 10 camera leaderboards, one per camera, each presenting at most 10 entries ranked by `CurrentSpeed` of measurements made at that camera. Each accepted measurement creates a camera entry (plate, speed, timestamp). | Assignment |
| BR-LB-02 | There is one global leaderboard presenting at most 10 entries ranked by cumulative average. A plate has at most one entry on it. | Assignment |
| BR-LB-03 | An in-flight vehicle has a global entry only while it has at least 3 accepted measurements. Each further measurement replaces the entry's score with the new average, even if the average decreased. | Assignment (Gate, Equation) |
| BR-LB-04 | A completed vehicle's global entry carries its final average and the Camera 10 timestamp. It is not changed afterwards. | Interpretation AMB-01 |
| BR-LB-05 | Rank order is score descending, then `NumberPlate` ascending using ordinal, case-sensitive comparison. Ranking uses unrounded values; rounding is presentation only. | Assignment (Tie-Breaking), Interpretation AMB-08 |
| BR-LB-06 | Entries are self-contained records. Removing a vehicle track does not remove its entries; entries leave only by expiry or pruning. | Interpretation AMB-01, AMB-02 |
| BR-LB-07 | A camera entry is live while `cameraWatermark − entry.Timestamp < CameraBoardWindow`. Expired entries are removed. | Interpretation AMB-02 |
| BR-LB-08 | A completed vehicle's global entry is live while `globalWatermark − entry.Timestamp < GlobalBoardLinger`. With linger 0 it is never live; with infinite linger it never expires. In-flight entries are not subject to the linger. | Interpretation AMB-01 |
| BR-LB-09 | The presented top 10 always equals the 10 best live candidates. When an entry expires, is removed, or its score falls, the next best live candidate takes its place. | Assignment (Equation, Purge), Interpretation AMB-01 |
| BR-LB-10 | Optional optimization: an entry may be pruned once at least 10 other entries are better, have a fixed score (camera entries, completed global entries; never in-flight ones) and stay live at least as long. Pruning must never change the presented top 10. With infinite window or linger this leaves exactly the top 10 of the fixed-score entries. Pruning assumes that an entry that never expires is also never replaced; a plate that reappears (AMB-10) breaks that assumption and is out of scope. | Interpretation AMB-01, AMB-02, AMB-10 |
| BR-LB-11 | Watermarks are computed in arrival order, before any vehicle state is touched, from **valid** measurements only (BR-ING-01): the camera watermark is the newest timestamp of valid measurements at that camera, the global watermark the newest of any valid measurement. A valid measurement that the sequence rule later rejects still counts; one with an invalid payload does not. Every message carries the watermark values stamped on it and all time decisions use those values. Watermarks never decrease, even if a measurement arrives with an older timestamp. | Interpretation AMB-02, AMB-05, ADR-0003 |
| BR-LB-12 | If no valid measurement arrives, watermarks do not advance, nothing expires and the boards keep their content (freeze). The state is never changed by the passage of wall-clock time; the presentation marks such boards as not live (BR-API-01). | Interpretation AMB-02, AMB-05 |
| BR-LB-13 | Order independence: the final content of every leaderboard depends only on the valid measurements and their timestamps, not on how workers interleave measurements of different plates. Measurements of one plate are applied in arrival order (BR-ING-03). An entry whose `ExpiresAt` is not after the board's watermark is not inserted. | Interpretation, ADR-0003 |

## Configuration

| ID | Rule | Provenance |
|---|---|---|
| BR-CFG-01 | Parameters: `CameraBoardWindow` (greater than 0, may be infinite, default about 2 s); `GlobalBoardLinger` (0 or more, may be infinite, default about 3 s); `InactivityTimeout` (finite, greater than one tick plus jitter, default a few ticks). Values are read at startup. | Interpretation AMB-01, AMB-02, AMB-05 |
| BR-CFG-02 | A run-time endpoint for changing the parameters is optional and not part of tier 2. | Extension, AMB-06 |
| BR-CFG-03 | Operational options in `appsettings.json` or environment variables: `PartitionCount` (default 4), `IngestChannelCapacity`, `FutureTimestampTolerance` and the ingest lag warning threshold. | Extension (tier 2), ADR-0003 |

## REST API

| ID | Rule | Provenance |
|---|---|---|
| BR-API-01 | `GET /api/traffic-snapshot` returns JSON containing: all ten camera leaderboards (empty ones included), the global leaderboard, and `GeneratedAtUtc`. Each board also carries `LatestEventUtc` (its watermark, null when it never received data). Each entry carries rank, `NumberPlate`, score and `MeasuredAtUtc` (the event time of its measurement); a global entry also carries `MeasurementCount`. The snapshot carries facts only; whether a board is *live* is decided by the client (BR-UI-04). Exact property names are fixed in `docs/contracts/rest-api.md`. | Assignment, Extension (`LatestEventUtc`, `MeasuredAtUtc`, `MeasurementCount`) |
| BR-API-02 | A snapshot is a detached, coherent copy: it is not affected by ingestion that occurs during or after serialization. The consistency boundary is **per board**: each board's entries and `LatestEventUtc` are read under one lock and belong together. The boards are not read at a single instant, so during ingestion one board may already reflect a measurement that another board does not yet reflect (a difference of a few messages). | Interpretation (CON-03 of the draft), ADR-0003 |
| BR-API-03 | Readiness reports whether the processor can serve snapshots; liveness reports that the process runs. An idle input stream does not by itself make the processor unready. | Extension (tier 2) |

## Client (WPF)

| ID | Rule | Provenance |
|---|---|---|
| BR-UI-01 | The dashboard shows the global top 10 and the top 10 of a camera selected by the user, each with number plates and the relevant speeds. | Assignment |
| BR-UI-02 | Snapshots are fetched by a background loop with a nominal period of 3 seconds. Requests never overlap and missed periods do not accumulate. The UI thread is used only to update UI-bound state. The nominal period stays 3 seconds by default; the user can switch it in the UI (1, 2, 3, 5 seconds) while the client runs. | Assignment, Interpretation AMB-08, Extension (selectable period) |
| BR-UI-03 | If a request fails, the client keeps showing the last successful snapshot, indicates that data is stale, shows the time of the last success, and continues polling. A client outage never affects the processor. | Extension (tier 2) |
| BR-UI-04 | The client shows each displayed board's `LatestEventUtc` and decides what is *live*: a board is live when `GeneratedAtUtc − LatestEventUtc` is below the client option `LiveLagThreshold` (default about 3 s). A board that is not live is marked clearly (for example dimmed, with the age of its data), so delayed or frozen data is never presented as live. | Extension (tier 2) |

## Operations

| ID | Rule | Provenance |
|---|---|---|
| BR-OPS-01 | The processor writes structured logs for lifecycle events, connection changes, rejected messages and processing failures. | Extension (tier 2) |
| BR-OPS-02 | The processor counts received, accepted and rejected (by reason) measurements, and reports the number of tracked in-flight vehicles, the ingest buffer depth and the **ingest lag** (wall-clock time minus the timestamp of the newest processed measurement; it grows between bursts and is meaningful only when generator and processor clocks agree) and the **latency** of the messages (for each applied message the wall-clock time of its processing minus its own timestamp, never negative; average and maximum over the last ten seconds in the status, a histogram in the metrics; it does not saw-tooth with the generator tick). They can be read without a metrics stack. Plates are never used as metric labels. | Extension (tier 2), ADR-0001 |

## Out of scope

Durable recovery across restarts, broker or state-store high availability, multi-instance processing and old-event
rejection (`MaxEventAge`) are not part of tier 2 (AMB-06). Tier 3 may add recovery if time remains.
