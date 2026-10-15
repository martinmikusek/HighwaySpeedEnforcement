# Assignment ambiguities and our decisions

The assignment PDF is ambiguous or self-contradictory in several places. This document records each one: what the text
says, the readings we considered, what we chose, and why. It is the audit trail for "why does the system behave like
this?" and the place to look before changing any domain rule.

None of these decisions has been confirmed with the assignment author. If the author clarifies, update the entry and
the affected rules in `docs/domain/`.

Status values: `Open` · `Proposed` (recommended default, awaiting owner confirmation) · `Decided`. All entries are `Decided` (owner, 2026-10-15).

| ID | Topic | Status |
|---|---|---|
| AMB-01 | Global leaderboard: live only vs. all recorded traffic | Decided: configurable linger, default live |
| AMB-02 | Per-camera leaderboards: time scope | Decided: configurable window, default live |
| AMB-03 | Exit boundary: Camera 10 vs. "counter exceeds 10" | Decided: remove after Camera 10 |
| AMB-04 | Vehicle completion and message ordering | Decided: strictly increasing cameras, any start |
| AMB-05 | Incomplete (orphaned) passages | Decided: inactivity timeout |
| AMB-06 | Scope beyond the PDF (recovery, metrics, ...) | Decided: tier 2, tier 3 only if time allows |
| AMB-07 | Duplicates and invalid input | Decided: reject and count, repeats by the sequence rule |
| AMB-08 | Minor interpretations | Decided: as listed |
| AMB-09 | Repository language | Decided: English |
| AMB-10 | Repeated passage of the same plate | Decided: out of scope; newest passage wins |

## Summary: what we decided and what it means

**In one paragraph.** The PDF can be read as "current traffic only" or "all recorded traffic". We do not hard-code either:
how long a result stays on a leaderboard is a parameter, and both readings are the two ends of it. The default is the live
reading. Time is measured by **measurement timestamps (event time)**, not by the processor's clock: a result is judged by
how old it is relative to the newest measurement seen, so a quiet period never empties a board; it freezes on the last
data and reports when that data is from. A vehicle's working state is deleted the moment it passes Camera 10.

**Parameters** (terminology: these are *leaderboard windows*, how long a result stays listed. The vehicle's average itself
is cumulative over all its cameras and is not a moving average).

| Parameter | Meaning | Default | Infinite means |
|---|---|---|---|
| `CameraBoardWindow` | A camera measurement stays on its board while it is younger than this, relative to the newest measurement at that camera | about 2 s | all-time Top 10 per camera |
| `GlobalBoardLinger` | A *completed* vehicle stays on the global board this long after its last measurement | about 3 s | completed vehicles with the best final averages stay (historical + live) |
| `InactivityTimeout` (technical) | A vehicle with no new measurement for this long is dropped as orphaned (AMB-05) | a few ticks | not allowed (would leak) |

**Steady-state numbers** (100 new vehicles per second, one camera per second):

| Quantity | Value |
|---|---|
| Messages per second | 1,000 (100 per camera) |
| Messages per vehicle | exactly 10 (Camera 1–10, never Camera 11) |
| Vehicles with working state after a tick | 900 (vehicles with 1–9 measurements) |
| Global candidates, finite linger | 700 in-flight vehicles (3–9 measurements) + 100 per second of linger (about 300 for 3 s); output is the top 10 |
| Global candidates, infinite linger | 700 in-flight + the 10 best completed ones |
| Camera candidates, finite window | 100 per second of window (about 200 for 2 s) per camera; output is the top 10 |
| Camera candidates, infinite window | 10 per camera |
| Memory | well under 1 MB in every mode; not a design constraint |

**Pruning rule (the "purge").** An entry may be discarded only if it can never again appear in a top 10, that is, if at
least 10 other entries are better, have a fixed score, and stay on the board at least as long. A completed vehicle's
average and a camera measurement are fixed; the average of a vehicle still on the road can rise and fall, so it is never
pruned for ranking reasons and never counts as one of the 10 dominating entries.

- Infinite window or linger: every entry lives forever, so exactly the top 10 of the fixed-score entries remain. This is
  what the PDF's Purge sentence asks for completed vehicles.
- Finite window or linger: an entry is not discardable at the moment it is created, because the entries above it can
  expire or (for vehicles on the road) fall first. It becomes discardable only when 10 *newer*, better, fixed-score
  entries exist. This keeps state small even for long windows.
- The rule is an optimization. Correctness does not depend on it; without it state is still well under 1 MB. Whether to
  implement it is an ADR decision.

**Impact by component.**

| Component | What the decisions imply |
|---|---|
| Generator | Emits exactly 10 messages per vehicle, then forgets it (AMB-03). Speed distribution can be simple; saturation is a configuration matter, not a generator problem (AMB-01, AMB-02). Plates are unique across the run (AMB-08). Transport must deliver one vehicle's messages in order (AMB-04). |
| Processor, state | In memory only. Working state per vehicle: count, speed sum, last camera, last timestamp; deleted on Camera 10. Leaderboard entries are self-contained records (plate, score, measurement timestamp). No database, no recovery of state after restart (AMB-06). |
| Processor, leaderboards | Ranked sets with update (a vehicle's average may fall), remove, and expiry driven by arriving timestamps. Global and camera boards can reuse one component. Reading the top 10 does not depend on candidate count. |
| Processor, time | No wall-clock timers and no clock abstraction needed for expiry: everything derives from measurement timestamps, so tests are deterministic. |
| Processor, concurrency | Per-plate ordering is obtained by routing a vehicle's messages to the same worker (AMB-04), so vehicle state needs no locking. Only the shared leaderboards need synchronization. |
| API | Each board carries an `asOf` timestamp (its newest measurement) in addition to the snapshot's generation time, so the client can tell frozen data from live data. |
| Client | Every poll shows different data while traffic runs. After traffic stops, boards freeze and the client shows the `asOf` age. Stale-data indicator on failed polls (AMB-06). |
| Architecture | The transport only needs an ordered stream. Redis would be justified by durability, which this scope does not need: live state refills within seconds after a restart. For infinite settings, durability would matter and a store such as Redis (sorted sets) becomes worth discussing (AMB-06). |
| Tests | Scenarios must cover: average falls and the next candidate takes the place; an entry expires and the next candidate takes the place; completed vehicle stays for the linger and then leaves; orphaned vehicle times out; boards freeze on silence; infinite settings keep only top 10 of completed results; duplicate and out-of-sequence messages are rejected without changing state. |

**Where we could be wrong.** None of this is confirmed with the assignment author. The two decisions that depend most on
intent are AMB-01 (the PDF contradicts itself) and AMB-02 (the PDF says "strictly bounded Top 10"). Both are covered
by a parameter, so a different intent means a different configuration, not a different design.

---

## AMB-01 Global leaderboard: live only vs. all recorded traffic — Decided: configurable linger, default live

**Source.** Heading: "Global System Leaderboard **(Live Active Traffic Only)**". The Purge: "This leaderboard represents
**all recorded traffic**. The moment a vehicle passes Camera 10 and it does not score on any leaderboard, its tracking
data will be fully purged."

**Readings.**

- **A1 — Strictly live.** A vehicle leaves the board the moment its Camera 10 measurement is accepted. Flaw: the Camera 10
  measurement can never influence the board and no vehicle ever shows its final 10-measurement average.
- **A2 — Live with linger.** As A1, but a completed vehicle stays for `GlobalBoardLinger` after its last measurement.
- **B — Historical + live.** Completed vehicles keep their final average permanently and compete with live ones.
  This is `GlobalBoardLinger = infinite`.

**Decision.** One mechanism, `GlobalBoardLinger`. Default is a finite value (A2); `0` gives A1; infinite gives B.

**Why.**

- Neither extreme fits the whole text, so the choice is left to configuration instead of being baked into the design.
- The live default keeps the dashboard dynamic with any generator; B saturates with record averages within minutes.
- A2 shows the Camera 10 measurement and final averages, which A1 cannot.
- It respects the Purge sentence: the vehicle's *tracking data* (working state) is deleted immediately at Camera 10. The
  leaderboard entry is a separate, self-contained record that is allowed to outlive it.

**Consequences.**

- A vehicle's average can decrease, so the ranking structure must support update and removal of an arbitrary entry; a
  bounded "keep only top 10" list is not sufficient. This is the intended engineering difficulty of the task.
- `GlobalBoardLinger` must exceed the interval between a vehicle's consecutive measurements (one tick plus jitter); it
  applies to completed vehicles only, in-flight ones are governed by `InactivityTimeout` (AMB-05).
- With an infinite linger, completed vehicles outside the top 10 are purged (pruning rule above).

---

## AMB-02 Per-camera leaderboards: time scope — Decided: configurable window, default live

**Source.** "Each tracks a strictly bounded Top 10 list of the fastest vehicles based on the instantaneous CurrentSpeed
recorded at the moment they passed through that specific camera." No window or "live" qualifier. The Purge sentence implies
leaderboard records can outlive the vehicle's tracking data.

**Readings.**

- **All-time.** Top 10 ever recorded per camera; matches the literal "strictly bounded Top 10" (insert-only, keep the best 10).
- **Live.** Top 10 among recent measurements at that camera.

**Decision.** One mechanism, `CameraBoardWindow`, measured in event time relative to the newest measurement at that camera.
Default is a finite value (live); infinite gives all-time.

**Why.**

- All-time boards saturate with record speeds within minutes and then rarely change.
- Event time instead of wall-clock time means a quiet period does not empty a board: it keeps the last data and `asOf`
  tells how old it is. The board is never empty merely because the poll fell between two batches.
- "Live while the vehicle is active" was rejected: the vehicle's working state is deleted at Camera 10, so the Camera 10
  board would always be empty.

**Consequences.**

- With a finite window, internal state per camera is not limited to 10 entries: after a measurement expires, the next
  best candidate must be restored. Only the output is bounded to 10. With an infinite window state is bounded to 10.
- A window much shorter than a tick (1 s) would show only part of a batch while it is being processed; the default of
  about two ticks avoids this at the cost of about 200 candidates per camera.
- Changing the window at runtime (see AMB-06): shrinking evicts immediately; enlarging cannot bring back entries that
  were already evicted.

---

## AMB-03 Exit boundary — Decided: remove after Camera 10

**Source.** Loop steps: advance counter and emit; then drop vehicles whose counter "has exceeded 10". CameraId is limited
to 1–10 and each vehicle "emits 10 total messages".

**Problem.** After the advance step a vehicle's counter is at most 10, so the drop condition is never true in the same
tick. Literally, the vehicle is advanced to 11 on the next tick and would emit a Camera 11 message.

**Decision.** Remove a vehicle right after its Camera 10 message is emitted. Each vehicle emits exactly 10 messages;
steady state is 100 messages per camera per second, 1,000 per second in total.

**Why.** It is the only reading consistent with CameraId 1–10 and with the stated lifespan of 10 messages.

---

## AMB-04 Vehicle completion and message ordering — Decided: strictly increasing cameras, any start camera

**Source.** "The moment a vehicle passes Camera 10 ... purged", combined with "hundreds of concurrent messages".

**Problem.** Generation order is sequential, but concurrent delivery/processing can reorder one vehicle's messages, so
"Camera 10 processed" may not mean "the vehicle is finished".

**Options.** (1) Guarantee per-plate ordering in the pipeline; Camera 10 accepted means complete. (2) Tolerate
reordering: complete only after all ten distinct cameras were seen, and keep tombstones so late messages do not
resurrect a purged vehicle.

**Decision.** Option 1, with a rule that the cameras of one vehicle form a strictly increasing sequence: a plate without
working state is accepted at any camera and starts a track; a plate with working state is accepted only at a camera
greater than its last one (gaps are tolerated). Anything else (repeated, late, reordered) is rejected, counted and logged,
and changes no state.

**Why.** Simpler per-vehicle state, no tombstones, and no single point of failure: a dead or lossy camera (camera 1
included) does not block the cameras after it. An earlier, stricter version (start at Camera 1, then exactly the next
camera) made one dead camera reject every later camera's measurements while their boards looked "idle but live"; it was
dropped. A ghost track created by a late repeated message is harmless: it has fewer than 3 measurements so it never enters
the global board, its camera entry replaces the same plate's entry or is already expired, and it is removed after
`InactivityTimeout`. Per-plate ordering is naturally obtained by partitioning work by plate, which also removes lock
contention on a vehicle. How it is achieved is an architecture decision (ADR).

---

## AMB-05 Incomplete (orphaned) passages — Decided: inactivity timeout

**Problem.** The PDF does not cover a vehicle that never reaches Camera 10 (lost message, generator killed). Its state
would leak and it would stay on the global leaderboard indefinitely.

**Decision.** A vehicle whose latest measurement is older than `InactivityTimeout` (event time, measured against the
newest measurement overall) is removed: its working state and its global-board entry. Its per-camera records are kept until
their own window ends. The timeout must exceed one tick plus jitter and must be finite. A later message for a removed vehicle
is rejected by the sequence rule (AMB-04).

**Consequence.** If all traffic stops, nothing expires (no newer measurement advances the clock), so the boards freeze
rather than empty, consistently with AMB-02.

**Silence and liveness (decided).** State and presentation are separated:

- *State* is driven by data only. Without new data nothing expires and boards freeze (BR-LB-12); wall-clock time never
  changes state. This also keeps the system correct when the processor lags: a backlog is applied in order and gives the
  right (late) result instead of dropping or wrongly orphaning data.
- *Presentation* says whether the picture is current. Each board carries `LatestEventUtc` (BR-API-01); the **client**
  decides what is live: `GeneratedAtUtc − LatestEventUtc < LiveLagThreshold`, a client option (BR-UI-04). The snapshot holds
  facts only, because "how fresh is fresh enough" is a presentation choice, and the processor already reports the same
  quantity as the ingest lag metric (BR-OPS-02). A delayed or frozen board is never shown as live. Clock synchronization is
  needed only for this comparison; if clocks disagree the label is wrong but the boards are not.

**Rejected: idle policy** (extrapolating the watermark by wall-clock time after some silence). It mixes two clocks, makes
state depend on wall-clock time, and would wipe data after an outage.

**Described, optional: camera heartbeats (BR-TEL-03).** In a real deployment a camera that sees no vehicle sends a heartbeat
with a timestamp. The processor treats it as punctuation ("nothing before this time is outstanding") that advances the
camera's and the global watermark. Effects: with healthy cameras and no traffic the boards expire normally and become empty,
which is what a control-room operator expects from sliding leaderboards; a dead camera stops sending both heartbeats and
measurements, so its board freezes and the client marks it not live (offline). Each camera is therefore in one of three
states: traffic, idle (heartbeats only), offline (silence). With MQTT, the last-will message and a retained status topic are
native ways to signal an offline camera. Heartbeats also keep the lag meaningful in quiet periods: without them an idle
camera cannot be told from a dead one. The message contract reserves a message type for it; the simulation does not need it.

---

## AMB-06 Scope beyond the PDF — Decided: tier 2, tier 3 only if time allows

An unreviewed ChatGPT draft (`docs/requirements.md`) adds durable recovery across processor restarts, metrics, probes and
old-event rejection. The PDF says "prototype" and "We respect your time". Recovery in particular forces a durable
broker/state store and dominates the architecture.

Candidate tiers:

1. **Core** — exactly what the PDF asks.
2. **Core + hygiene** — input validation, duplicate rejection, orphan timeout, structured logs, health endpoint, stale-data
   indicator in the WPF client.
3. **Core + hygiene + durable recovery** — restart-safe state and input position.

**Decision.** Tier 2 is the minimum we deliver. Tier 3 is a stretch goal, attempted only if time remains after tier 2 is
complete; tier 2 design must not rule it out (state and input position stay behind replaceable boundaries).

Rationale: with finite windows all state is self-healing: after a processor restart the boards refill within seconds, so
durable state would protect almost nothing. Durability, and with it a store such as Redis (sorted sets), becomes
justified only for infinite settings, because all-time results cannot be recomputed after a restart. The reason for Redis
is durability and shared state, not "all-time" as such.

Optional demo extension: an endpoint to change the window parameters at runtime. Parameters are otherwise read from
configuration at startup. The endpoint would be unauthenticated and is for demonstration only; semantics of shrinking and
enlarging are described in AMB-02. Not part of tier 2 unless the owner asks for it.

---

## AMB-07 Duplicates and invalid input — Decided: reject and count, repeats by the sequence rule

Invalid messages (empty plate, CameraId outside 1–10, non-finite or negative speed, invalid timestamp) are rejected and
counted. A repeated or stale measurement (not the next camera in sequence, AMB-04) is rejected and counted as a
duplicate; it does not change the count, sum or average. We do not distinguish identical from conflicting repeats.

---

## AMB-08 Minor interpretations — Decided: as listed

- `TimestampTicks` = UTC `DateTime.Ticks` at measurement time. It is used only for expiry (relative age), never for
  ordering of one vehicle's measurements.
- Plate comparison for tie-breaking is ordinal and case-sensitive; ranking uses the unrounded speed, rounding is
  presentation only.
- Cumulative average = arithmetic mean of accepted camera speeds (not distance/time).
- A number plate identifies one passage and is never reused.
- Plates look like Czech plates (a simplified format of digits, letters and a four-digit number, for example `1AB 2345`;
  the regional letter codes are modeled, exact rules decided in the generator design). The generator draws them not sequentially but as an **affine permutation**
  `i -> (a*i + b) mod N` over the finite plate space of size `N` (`a` coprime to `N`): the first `N` plates are unique
  without a set of used plates and look random. A seeded `(a, b)` makes runs reproducible. Uniqueness is guaranteed within a
  run; across generator restarts it is only practically guaranteed (a restart within about 10 s with colliding plates has a
  small probability) unless the counter offset is persisted. Decided in the generator design.
- "Exactly 3-second" polling = fixed-rate, non-overlapping loop; nominal, not hard real-time.

---

## AMB-09 Repository language — Decided: English

Conversation with the owner is in Czech; all repository artifacts (docs, code, comments, commit messages) are in English.

---

## AMB-10 Repeated passage of the same plate — Decided: out of scope; newest passage wins

**The question.** In the real world a vehicle can pass the road again (leave at an exit, drive around, enter again), so a
plate identifies a vehicle, not a passage. The assignment's generator never does this, and the domain model (AMB-08) says a
plate identifies exactly one passage.

**What the design already does.** The sequence rule (AMB-04) copes with a second passage without any change: the track is
deleted at Camera 10 or by the inactivity timeout (AMB-05), so the next measurement of that plate starts a new track. The
only constraint is that a return within `InactivityTimeout` of an abandoned passage is rejected as out of sequence until the
timeout passes; the timeout (seconds) is far below any real detour (minutes). On the boards a plate has at most one entry,
so the **newest passage replaces the older one**, even with a lower score. That is the chosen presentation: one row per
vehicle, showing its latest passage.

**What would be needed to do it properly.** A passage would need its own identity (for example a passage id assigned by
the processor when a track starts), and the boards would either be keyed by passage, showing a vehicle several times, or
keep the "newest passage wins" rule explicitly. With an infinite window or linger, pruning (BR-LB-10) assumes that an
entry that never expires is also never replaced; a repeated passage breaks that assumption, so a replaced older passage
could have a pruned entry that should have reappeared. Exact results with infinite settings and repeated passages would
require keeping every entry (unbounded memory) or keying entries by passage.

**Decision (owner, 2026-10-14).** Out of scope for this assignment: the generator never reuses a plate (BR-SIM-03), the
processor needs no passage identity, and the newest passage replacing the older one is the documented behavior should a
plate reappear. Infinite window and linger remain diagnostic options whose pruning may hide an older passage. Recorded so
that a production version knows this has to be solved.
