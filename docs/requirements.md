# Highway Speed Enforcement Pipeline

## Requirements and Working Interpretation

**Status:** NOT AUTHORITATIVE. An unreviewed first draft, kept for history. The decisions are in `docs/assignment-ambiguities.md`, `docs/domain/`, `docs/contracts/` and `docs/adr/`; they differ from this draft in places (recovery, old-event rejection and other items beyond the PDF).  
**Date:** 2026-10-13  
**Source:** `CodingAssignment_HighwaySpeedEnforcementPipeline.pdf`

## 1. Purpose and authority

Build a small prototype consisting of a telemetry generator, a containerized processor, and a WPF dashboard. Prioritize correct behavior, clear design decisions, and a manageable implementation scope.

This document captures our interpretation of the assignment. It is not an approved clarification from the assignment author. The original PDF remains the reference for the supplied requirements.

The labels below distinguish requirement provenance:

- **Assignment:** explicitly requested by the source document.
- **Interpretation:** our resolution of an ambiguity in the source; subject to confirmation.
- **Extension:** behavior we additionally want to demonstrate.
- **Open:** a decision still to be made.

“Shall” describes expected behavior under this working specification. It does not imply that every extension was requested by the assignment author. Architecture and technology choices shall be recorded separately in ADRs.

## 2. Scope

The solution shall contain three independently running applications:

1. A console telemetry generator.
2. A .NET processor running in Docker, ingesting telemetry and exposing a REST API.
3. A WPF desktop client displaying the results.

The assignment permits .NET 8, 9, or 10 for the processor. The exact target framework and generator-to-processor transport remain open.

The initial implementation shall target one processor instance. Horizontal scaling, broker high availability, and production deployment infrastructure are outside the initial scope.

## 3. Traffic simulation

### SIM-01: Road model — Assignment

The road has ten sequential camera locations in one direction of travel. Every vehicle passes Camera 1 through Camera 10 in that order. Lane allocation, camera spacing, and physical image recognition are not modeled.

### SIM-02: Simulation cycle — Assignment with interpretation

Once per second, the generator shall execute the following steps in order:

1. Create exactly 100 vehicles with unique number plates and an initial camera counter of zero.
2. Advance each active vehicle by one camera.
3. Emit one telemetry message for each advanced vehicle.
4. Remove vehicles that have emitted their Camera 10 message.

Each vehicle shall emit exactly ten messages. At steady state, each cycle emits 1,000 messages: 100 per camera.

**Interpretation A-01 — Exit boundary:** The original instructions remove vehicles only after their camera counter exceeds 10, despite restricting messages to Camera 1–10. We remove vehicles after emission at Camera 10, preventing a Camera 11 message.

The discrete tick sequence is authoritative; no additional physical timing model is required.

### SIM-03: Speed behavior — Assignment

Speeds shall be expressed as `float` values in km/h and vary plausibly between consecutive cameras, around highway speeds such as 130 km/h. Exact distributions and variation limits shall be implementation parameters, not a realistic traffic physics model.

## 4. Telemetry contract

### TEL-01: Required fields — Assignment

| Field | Type | Meaning |
|---|---|---|
| `NumberPlate` | `string` | Vehicle identifier |
| `TimestampTicks` | `long` | Measurement timestamp |
| `CurrentSpeed` | `float` | Measured speed in km/h |
| `CameraId` | `int` | Camera identifier, 1 through 10 |

**Interpretation A-02 — Timestamp:** `TimestampTicks` represents UTC .NET ticks, equivalent in convention to `DateTime.UtcNow.Ticks`. It is the measurement time, not processor receipt time.

**Interpretation A-03 — Identity:** A number plate identifies one passage and is not reused within the retained processing history, including generator restarts. Consequently, `(NumberPlate, CameraId)` identifies a measurement. Supporting repeated real-world passages of the same plate would require an additional passage identifier and is outside scope.

### TEL-02: Input handling — Extension

The processor shall reject malformed messages and invalid values, including empty number plates, camera identifiers outside 1–10, invalid timestamps, and negative or non-finite speeds.

Repeated delivery of the same measurement shall not increase the measurement count or speed sum. Handling conflicting payloads with the same measurement identity remains an open decision.

## 5. Vehicle processing state

### STATE-01: Working state

For an incomplete passage, retain enough state to determine:

- Vehicle identity.
- Sum of accepted speeds and number of distinct accepted measurements.
- Which cameras have already been processed.
- Whether the passage is complete.
- Activity information required for timeout cleanup.

The cumulative average shall be the arithmetic mean of accepted camera speed measurements. It is not a distance/time calculation.

### STATE-02: Completion — Interpretation

A passage is complete after all ten distinct camera measurements have been processed. Receipt of Camera 10 alone shall not trigger cleanup if earlier measurements are still missing.

**Interpretation A-04 — Processing order:** Physical generation order is sequential, but concurrent delivery or processing may expose messages out of order. Completion and duplicate handling shall not assume that Camera 10 is necessarily processed last.

## 6. Leaderboards

### LB-01: Per-camera leaderboards — Assignment with interpretation

Maintain ten separate leaderboards, each containing at most ten vehicles ranked by the instantaneous speed recorded at that camera. A record shall contain at least the number plate and measured speed; camera identity may be represented by its enclosing leaderboard.

**Interpretation A-05 — Historical camera records:** Camera leaderboards represent historical Top 10 results without a rolling time window. Vehicle exit or deletion of working state does not remove a qualifying camera record. Better-ranked results may displace it.

### LB-02: Global leaderboard — Assignment with interpretation

Maintain one global leaderboard containing at most ten vehicles ranked by cumulative average speed. An incomplete passage becomes eligible after at least three distinct camera measurements have been accepted.

For an incomplete passage, each subsequent measurement replaces its previous average, even when the average decreases. Do not retain the highest intermediate average as that vehicle's score.

For a completed passage, its average becomes final.

**Interpretation A-06 — Historical and live candidates:** The source uses both “Live Active Traffic Only” and “all recorded traffic” without an explicit definition reconciling them. Our selected interpretation combines final averages of completed passages with current averages of eligible incomplete passages. Completion does not by itself remove a vehicle from the global leaderboard. This assumption requires confirmation if strict compliance with the author's intent is necessary.

### LB-03: Ordering — Assignment with interpretation

Rank entries by:

1. Speed or average speed, descending.
2. Number plate, ascending, for an exact score tie.

**Interpretation A-07 — Plate comparison:** Use ordinal, case-sensitive string comparison for deterministic behavior across machines and cultures. Display rounding shall not determine ranking. Internal accumulation precision remains an implementation decision.

### LB-04: Candidate correctness

The global leaderboard shall remain correct when an active candidate's average decreases or an incomplete passage expires. Retain sufficient candidate information to restore the correct next entry, including completed results temporarily displaced by active candidates.

The ten-entry output limit does not imply that all internal candidate state can be limited to ten entries. Internal state shall nevertheless remain bounded by active passages and the limited historical candidate information needed for correct ranking.

## 7. Cleanup and retention

### CLEAN-01: Completed passages — Interpretation

After processing a completed passage, update the relevant leaderboard state before removing its working state. Preserve self-contained qualifying leaderboard records.

**Interpretation A-08 — “Tracking data”:** A retained leaderboard result does not require retention of the entire passage history or mutable vehicle object. Compact duplicate-detection metadata may remain for the supported redelivery period.

### CLEAN-02: Incomplete passage timeout — Proposed extension

An incomplete passage may expire after a configurable period of inactivity. Expiration shall remove its working state and its provisional global candidacy, but shall preserve valid camera records.

The timeout value, clock basis, and interaction with recovery/backlog processing remain open. Cleanup shall not silently invalidate the recovery guarantee defined below.

### CLEAN-03: Old-event rejection — Open, not enabled by this specification

We have discussed a configurable `MaxEventAge`, but have not selected its value or agreed to enable it. Ten seconds is not automatically a safe threshold: an older unprocessed event may complete a passage or establish a historical record.

If introduced, the policy shall explicitly define accepted data loss, incomplete-passage behavior, and its effect on recovery. Broker retention and application-level event rejection are separate decisions.

## 8. Concurrency and snapshots

### CON-01: Concurrent ingestion — Assignment

The processor shall support concurrent message parsing and safe updates of vehicle state and leaderboards. Use thread-safe structures, non-blocking techniques, or fine-grained locking as appropriate.

Correctness applies to compound operations, not merely to individual collection calls. Duplicate detection, average updates, ranking, and cleanup shall not race in ways that corrupt results.

### CON-02: Resource bounds — Extension

Any ingestion buffer shall have an explicit capacity and overflow/backpressure policy. Do not silently drop measurements or create unbounded background work.

### CON-03: Snapshot consistency — Interpretation

Return a coherent, detached snapshot of the leaderboard state. Do not expose mutable internal collections that can change during serialization. Document the chosen consistency boundary.

## 9. REST API

### API-01: Traffic snapshot — Assignment

Expose `GET /api/traffic-snapshot`. The JSON DTO shall contain:

- All ten camera leaderboards, including empty ones.
- The global average-speed leaderboard.
- A UTC timestamp indicating when the snapshot was generated.

Concrete JSON property names and DTO types shall be defined during implementation planning.

## 10. WPF client

### UI-01: Dashboard — Assignment

Display the global Top 10 and a selectable camera leaderboard. Include number plates and the relevant speed values.

### UI-02: Polling — Assignment with interpretation

Fetch snapshots on a background polling loop with a target interval of three seconds. Use the UI thread only to update UI-bound state.

**Interpretation A-09 — Slow requests:** Three seconds is the nominal polling period, not a hard real-time guarantee. Requests shall not overlap or accumulate if the backend is slow. The scheduling policy for missed intervals shall be documented.

### UI-03: Failure display — Extension

On a failed request, preserve the last successful snapshot, indicate that data is stale, and show the time of the last successful update. A client outage shall not interrupt server-side processing.

## 11. Recovery — Explicit extension to the assignment

### REC-01: Processor restart

Working passage state and leaderboard state shall survive a processor process/container restart. The processor shall resume from a recoverable input position and process pending messages within the supported retention and recovery window.

Transport buffering alone is insufficient: previously processed measurements must remain reflected in restored computation state.

### REC-02: State and acknowledgement

The input acknowledgement/checkpoint policy shall not advance beyond safely recoverable state. If a crash causes a previously applied measurement to be delivered again, processing shall not count it twice.

Do not claim end-to-end exactly-once delivery. Define and verify the actual duplicate-handling and recovery behavior.

### REC-03: Scope limits

Initial resilience targets a single processor restarting while its external dependencies remain available. Storage failure, broker failure, machine loss, and multi-instance failover guarantees require separate decisions and are not implicitly covered.

The state store and transport are not selected by this document. Redis, MQTT, Kafka, and Event Hubs are options discussed so far, not mandated dependencies.

## 12. Operational visibility — Extension

Provide structured logs for lifecycle events, connection changes, rejected messages, and processing failures. Avoid logging every normal measurement at Information level.

Provide basic metrics for received, processed, rejected, and duplicate messages; buffer depth; processing duration; and tracked incomplete passages. Do not use number plates as metric labels.

Expose liveness and readiness separately. Track the last received measurement without assuming that an idle stream necessarily indicates a fault.

Metrics scraping and visualization infrastructure is optional. Distributed tracing and centralized log infrastructure are outside the initial scope.

## 13. Acceptance scenarios

Verify at least the following behaviors:

1. Every generated passage emits exactly Camera 1–10, with no Camera 11 event.
2. Each steady-state simulation cycle emits 1,000 measurements.
3. A vehicle cannot enter the global leaderboard before its third distinct measurement.
4. A duplicate measurement does not change its count, sum, or average.
5. A decreasing average updates ranking and correctly restores the next candidate.
6. A completed historical candidate can reappear after a provisional candidate drops or expires.
7. Equal scores are ordered deterministically by number plate.
8. Completion cleanup preserves qualifying historical records.
9. Out-of-order processing does not cause premature cleanup.
10. API snapshots remain valid while ingestion is active.
11. WPF remains responsive and indicates stale data during backend unavailability.
12. Restart after state update but before acknowledgement does not double-count a measurement.
13. Restart during an incomplete passage preserves its previously processed measurements.

These are behavior-oriented scenarios, not a requirement to build an extensive test framework.

## 14. Decisions to record in ADRs

- Confirmation or revision of global leaderboard semantics (A-06).
- Transport, delivery semantics, and input recovery position.
- State storage and atomicity boundary.
- Concurrency, candidate tracking, and snapshot strategy.
- Timeout, redelivery, retention, and optional old-event rejection policies.
- Behavior for conflicting duplicate payloads.
- Initial operational tooling and explicit resilience limits.

Keep proposed decisions distinct from accepted ones. Implement the smallest design that satisfies the agreed behavior; do not add infrastructure solely because it may be useful in a future production system.
