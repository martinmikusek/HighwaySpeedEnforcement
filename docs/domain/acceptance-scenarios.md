# Acceptance scenarios

Behavior-oriented scenarios derived from the business rules. They are the basis of tests; they do not prescribe a test
framework. Use `W` for `CameraBoardWindow` and `L` for `GlobalBoardLinger`. Timestamps are written in seconds for
readability.

## Generator

| ID | Scenario | Rules |
|---|---|---|
| AC-01 | **Given** a run of 15 ticks, **then** every vehicle emitted exactly `CameraId` 1 to 10 in order, and no measurement has `CameraId` 11. | BR-SIM-04 |
| AC-02 | **Given** a steady-state tick (after tick 10), **then** exactly 1,000 measurements are emitted, 100 per camera, and 100 new vehicles were created. | BR-SIM-02, BR-SIM-04 |
| AC-03 | **Given** a run with a generator restart, **then** no plate appears in both runs. | BR-SIM-03 |

## Ingestion

| ID | Scenario | Rules |
|---|---|---|
| AC-04 | **When** measurements arrive with empty plate, camera 0 or 11, negative speed, NaN or infinite speed, **then** each is rejected as `InvalidPayload` and no state changes. | BR-ING-01, BR-ING-07 |
| AC-05 | **Given** plate A accepted at camera 1, 2, 3, **when** camera 3 or camera 2 arrives again, **then** it is rejected as `OutOfSequence` and count, sum and average are unchanged. | BR-ING-02, BR-ING-04 |
| AC-06 | **When** a plate without a track arrives at camera 4, **then** it is accepted and starts a track; **when** it later arrives at camera 2, **then** that is rejected as `OutOfSequence`. | BR-ING-02 |
| AC-07 | **Given** plate A completed (camera 10 accepted, track deleted), **when** camera 10 for A arrives again, **then** it creates a one-measurement track that completes at once; the camera 10 board is unchanged (same plate, same entry) and A is not on the global board (fewer than 3 measurements). | BR-ING-02, BR-ING-05, BR-LB-03 |
| AC-08 | **Given** plate A accepted up to camera 6, **when** no measurement of A arrives and the global watermark advances by `InactivityTimeout`, **then** A's track and global entry are removed; its camera entries remain until `W` ends. | BR-ING-06 |
| AC-09 | **When** many measurements of different plates are processed concurrently, **then** results equal those of sequential processing, and measurements of the same plate are applied in arrival order. | BR-ING-03 |

## Global leaderboard

| ID | Scenario | Rules |
|---|---|---|
| AC-10 | **Given** a vehicle with a very high speed at cameras 1 and 2, **then** it is not on the global board until its third accepted measurement. | BR-LB-03 |
| AC-11 | **Given** vehicle A is on the global board with average 140 and vehicle B (completed, final 135) is within the linger, **when** A's next speed lowers its average to 130, **then** A's entry is replaced by 130 and B is ranked above A. | BR-LB-03, BR-LB-09 |
| AC-12 | **Given** 10 vehicles above vehicle X on the board, **when** one of them falls below X or expires, **then** X appears in the top 10. | BR-LB-09 |
| AC-13 | **Given** vehicle A completes with final average 150 and `L` = 3 s, **then** A stays on the global board until the global watermark is 3 s past A's Camera 10 timestamp, then leaves. | BR-LB-04, BR-LB-08 |
| AC-14 | **Given** `L` = 0, **when** A completes, **then** A is not on the global board. **Given** `L` = infinite, **then** A stays and completed vehicles outside the top 10 are pruned. | BR-LB-08, BR-LB-10 |
| AC-15 | **Given** two entries with exactly equal scores and plates `AB1` and `AA9`, **then** `AA9` is ranked first. | BR-LB-05 |

## Camera leaderboards

| ID | Scenario | Rules |
|---|---|---|
| AC-16 | **Given** 100 measurements at camera 5 in one tick, **then** the camera 5 board shows the 10 highest speeds, ties by plate. | BR-LB-01, BR-LB-05 |
| AC-17 | **Given** `W` = 2 s and a measurement at camera 5 at time t, **when** a measurement at camera 5 with time t + 2 s is accepted, **then** the first is expired, and the next best live measurement takes its place in the top 10. | BR-LB-07, BR-LB-09 |
| AC-18 | **Given** boards with data and the input stops, **then** nothing expires and the boards keep their content; `LatestEventUtc` keeps the time of the last measurement. | BR-LB-12, BR-API-01 |
| AC-19 | **Given** the Camera 10 board, **then** it contains measurements from the last `W` seconds even though each vehicle's track was deleted at Camera 10. | BR-LB-06, BR-LB-07 |
| AC-20 | **Given** `W` = infinite, **then** each camera board holds exactly the best 10 measurements ever and pruning does not change the presented top 10. | BR-LB-10 |
| AC-21 | **Given** a valid measurement with an older timestamp than the watermark, **then** the watermark does not decrease. A valid measurement rejected by the sequence rule still advances it; a measurement with an invalid payload does not. | BR-LB-11 |

## API

| ID | Scenario | Rules |
|---|---|---|
| AC-22 | **When** `GET /api/traffic-snapshot` is called before any data arrived, **then** it returns 10 empty camera boards, an empty global board, `GeneratedAtUtc` in UTC, and null `LatestEventUtc` values. | BR-API-01 |
| AC-23 | **While** ingestion is running at 1,000 measurements per second, **then** repeated snapshot requests return valid JSON, boards in rank order with at most 10 entries, and an earlier snapshot is not changed by later ingestion. | BR-API-02 |
| AC-24 | **Given** the processor is running but receives no measurements, **then** liveness and readiness are both healthy. | BR-API-03 |

## Client

| ID | Scenario | Rules |
|---|---|---|
| AC-25 | **When** the user selects "Camera 5", **then** the panel shows camera 5's top 10 from the latest snapshot without a new request. | BR-UI-01 |
| AC-26 | **Given** a slow response (longer than 3 s), **then** no second request is started until the first finishes, and missed periods do not queue up. | BR-UI-02 |
| AC-27 | **Given** the processor becomes unreachable, **then** the UI stays responsive, keeps the last successful snapshot, shows it as stale with the last success time, and recovers when the processor returns. | BR-UI-03 |
| AC-28 | **Given** frozen boards, **then** the client shows the `LatestEventUtc` age of each displayed board and marks boards whose lag is at or above `LiveLagThreshold` as not live. | BR-UI-04 |

## Operations

| ID | Scenario | Rules |
|---|---|---|
| AC-29 | **When** measurements are rejected, **then** counters per reason increase and one structured log record is written per rejection (not per normal measurement). | BR-ING-07, BR-OPS-01, BR-OPS-02 |
| AC-30 | **Given** a set of valid measurements of many plates, **when** they are processed with different partition counts and random delays between workers, **then** the final boards are identical. | BR-LB-13, BR-ING-03 |
| AC-31 | **Given** boards with data, **when** a measurement with a timestamp further than `FutureTimestampTolerance` ahead of the processor's clock arrives, **then** it is rejected as `InvalidPayload`, the watermarks are unchanged and no entry expires. | BR-ING-01, BR-LB-11 |
| AC-32 | **Given** the processor is a few seconds behind (measurements are applied correctly but late), **then** the boards are correct for the data processed so far, `GeneratedAtUtc − LatestEventUtc` shows the lag, and the client marks the boards as not live until it falls below `LiveLagThreshold`. | BR-API-01, BR-UI-04 |
| AC-33 | **Optional (heartbeat).** **Given** heartbeats from camera 5 with advancing timestamps and no measurements, **then** camera 5's entries expire after the window, its board is empty and its `LatestEventUtc` keeps advancing (the client shows it live); **when** heartbeats stop, the board freezes and the client marks it not live. | BR-TEL-03, BR-LB-12, BR-UI-04 |
| AC-34 | **Given** camera 7 sends nothing (dead) or camera 1 is dead, **when** vehicles pass the remaining cameras, **then** measurements of the other cameras are accepted, those cameras' boards fill normally, vehicles complete at camera 10, and averages are computed from the accepted measurements only. | BR-ING-02, BR-LB-03 |
