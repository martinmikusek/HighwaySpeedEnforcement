# ADR-0003: Concurrency model and leaderboard data structure

- **Status:** Accepted (2026-10-13)
- **Date:** 2026-10-13
- **Related rules:** BR-ING-02, BR-ING-03, BR-ING-06, BR-LB-05..12, BR-API-02, BR-CFG-01; AMB-01, AMB-02, AMB-04, AMB-05; ADR-0001

## Context

The assignment asks for concurrent ingestion with thread-safe collections or fine-grained locking, and a coherent snapshot
(BR-API-02). The processor gets about 1,000 messages per second (bursts up to 10x), already ordered by the single MQTT
stream (ADR-0001), and keeps about 900 vehicle tracks and about 3,000 leaderboard candidates (ADR-0001 and the summary in
`assignment-ambiguities.md`).

Requirements that shape the design:

- Measurements of one plate are applied in order (BR-ING-03). Different plates may be processed concurrently.
- Board content must depend only on the accepted measurements and their timestamps, not on processing speed (domain
  invariant 8). This is what makes scripted E2E tests exact and what stage 2 replay relies on (ADR-0001).
- Boards need update, removal, time expiry and a top-10 read (BR-LB-09). A bounded "keep 10" list is not enough.
- A snapshot is a detached, coherent copy; the consistency boundary must be documented (BR-API-02).
- Buffers are bounded, with an explicit overflow policy (ADR-0001).

Two pitfalls found while designing:

1. **Cross-partition time skew.** The watermark (BR-LB-11) and the orphan check (BR-ING-06) use "the newest timestamp seen
   so far". If parallel workers each advance a shared watermark as they happen to process messages, a fast worker can push
   it ahead while a slower one still handles older messages. Decisions such as "this vehicle is orphaned" then depend on
   thread timing. With synthetic timestamps spanning minutes (scripted E2E) or a replay (stage 2), this produces wrong and
   non-reproducible results.
2. **Poison timestamps.** Watermarks never decrease, so one measurement with a timestamp far in the future would expire every
   entry and never recover.

## Options considered

1. **Single writer** (confinement). One loop applies all state changes; reads use an immutable snapshot swapped in by the
   writer. Simplest and obviously correct; at this load one thread has ample headroom. But it does not exercise or
   demonstrate the concurrency the assignment asks for, and it does not scale past one core.
2. **Shared state with locks, any thread applies messages.** No ordering per plate unless every plate is also locked or
   pinned, and the skew problem appears. Rejected.
3. **Per-plate partitioned workers, per-board locks, watermarks stamped in arrival order.** Vehicle state is confined to
   one worker (no locks), shared boards use short fine-grained locks, and time is decided in one sequential stage, so
   results do not depend on thread interleaving.
4. **Immutable copy-on-write boards** (immutable sorted set swapped with compare-and-exchange). Lock-free writers and
   free snapshots, at the price of allocation per update and retry loops. No benefit at this load. Rejected.

| | 1 Single writer | 2 Shared + locks | 3 Partitioned + stamping | 4 Copy-on-write |
|---|---|---|---|---|
| Deterministic (invariant 8) | yes | no | yes (by design) | partly |
| Fits "thread-safe / fine-grained locking" | by confinement | yes | yes, plus confinement | lock-free |
| Complexity | lowest | medium | medium | high |
| Scales beyond one core | no | yes | yes | yes |
| Snapshot cost | swap | lock | short lock per board | reference read |

## Decision

Option 3.

**Pipeline (ADR-0001 stage 1; the input adapter is replaceable):**

1. **Receive and validate (sequential, one stream).** The MQTT handler decodes a message and validates it (BR-ING-01).
   Invalid messages are counted and logged and go no further.
2. **Stamp (sequential, arrival order).** For each valid message the stage advances `globalWatermark` and
   `cameraWatermark[camera]` (maximum of the timestamps seen so far) and attaches the resulting values to the message. All
   time decisions downstream use the stamped values, never a shared mutable clock.
3. **Route.** `partition = stableHash(plate) mod N` into a bounded channel per partition (N and the capacity are options in
   `appsettings.json`/environment, default N = 4; full means the handler waits and the MQTT acknowledgement is delayed, per ADR-0001). One worker owns each
   partition, so one plate is always handled by the same worker in arrival order.
4. **Apply (parallel across partitions).** A worker owns its vehicle tracks in a plain dictionary (no locking). For each
   message: drop the track if it is orphaned by the stamped watermark (BR-ING-06), apply the sequence rule (BR-ING-02),
   update count and sum, and send operations to the boards. Camera board lock and global board lock are taken one after
   the other, never nested, so there is no lock ordering problem.
5. **Boards (shared, one lock per board).** Ten camera boards and one global board, each a `RankedBoard` guarded by its own
   lock (`System.Threading.Lock`).
6. **Track cleanup.** Memory of silent vehicles is freed by a sweep inside each worker whenever the stamped global
   watermark has advanced by at least one second of event time since its last sweep. The sweep only frees memory; the
   orphan decision itself is made on every message (step 4), so behavior does not depend on sweep timing.

**`RankedBoard`.** Holds, for one board: a dictionary by plate; a sorted set in rank order (score descending, plate
ordinal ascending, BR-LB-05); a sorted set by `ExpiresAt`; the board's watermark. Every entry has
`ExpiresAt = timestamp + window` (camera entry), `+ linger` (completed vehicle) or `+ InactivityTimeout` (in-flight
vehicle); infinite means no expiry. Operations, all O(log n):

- `Upsert(entry)`: ignored if `ExpiresAt` is not after the board's watermark (the entry is already expired); otherwise it
  replaces the plate's previous entry in all three structures.
- `Remove(plate)`.
- `ExpireUpTo(watermark)`: raises the board's watermark (never lowers it) and removes every entry whose `ExpiresAt` is not
  after it.
- `Top(10)`: the first ten entries of the rank-ordered set, plus the watermark, read under the same lock.

Entries with infinite lifetime and fixed score are limited to the best 10 (the worst is evicted on insert). This is the
pruning rule for infinite window or linger and keeps state bounded; it never changes the top 10 (BR-LB-10). Pruning for
finite lifetimes is not implemented; state stays near 1,000 entries per board.

**Order independence.** Operations of different plates commute: the final contents of a board are the last entry per plate
(plates are ordered by their worker) whose `ExpiresAt` is after the board's final watermark, which is the maximum of the
stamped values. A test shuffles worker interleavings and checks that boards are identical.

**Snapshot.** The REST handler reads each board's top 10 and `LatestEventUtc` under that board's lock and assembles the DTO. Each
board is consistent in itself (its `LatestEventUtc` and entries belong together). The boards are **not** read at one instant: during
ingestion, camera 5 may already contain a measurement whose effect on the global board is not yet visible. The difference is
bounded by the processing time of a few messages. This is the documented consistency boundary (BR-API-02).

**Watermark: what, when, why.**

- *What.* The "current time of the data": the newest measurement timestamp seen so far (per camera, and overall).
  Everything time-based (window, linger, `InactivityTimeout`) compares an entry's timestamp with it. Wall-clock time is
  not used, because a delayed batch, a replay, a test with synthetic timestamps or a quiet period would then expire or keep
  the wrong entries.
- *When and how.* In the stamping stage (step 2), once per message, in arrival order, only for valid messages:
  `globalWatermark = max(globalWatermark, ts)` and `cameraWatermark[c] = max(cameraWatermark[c], ts)`; the new values travel
  with the message. A late message with an older timestamp leaves the watermark unchanged.
- *Example* (camera 5 window 2 s): message A, camera 5, ts 10 -> stamp (camera 5: 10, global: 10). Message B, camera 3, ts 11
  -> stamp (camera 3: 11, global: 11). Message C, camera 5, ts 12 -> stamp (camera 5: 12, global: 12); applying C expires
  camera 5 entries with `ExpiresAt <= 12`, so A (10 + 2) leaves. If message D, camera 5, ts 11.9 arrives afterwards, the
  camera 5 watermark stays 12.
- *Which messages count.* Not messages with an invalid payload: their timestamp cannot be trusted (garbled data, a camera
  with a wrong clock). Yes for valid messages that the sequence rule later rejects (duplicates, gaps, late arrivals): their
  timestamp is genuine evidence that time has reached that point, and the stamping stage runs before a worker can decide
  acceptance (acceptance needs the vehicle's track, which lives in the worker). A duplicate carries an old timestamp and
  changes nothing; a message after a gap carries a real newer timestamp and rightly moves the clock.
- *Real-world causes of rejected messages.* Invalid: corrupted payload, OCR failure (empty plate), sensor glitch
  (negative speed), camera with a wrong clock. Valid but out of sequence: redelivery after a reconnect (QoS 1), a lost
  message followed by later ones, a very late message after the vehicle timed out.

**Time and validation.**

- `InactivityTimeout`, window and linger are evaluated against stamped event-time watermarks only; wall-clock time is used
  for the ingest lag metric and `GeneratedAtUtc` only (via `TimeProvider`).
- Validation also rejects timestamps further than a configurable tolerance ahead of the processor's clock (poison
  timestamps, pitfall 2). Tests use timestamps in the past relative to the processor's clock.

## Consequences

- Vehicle state needs no locks; shared state is limited to eleven small boards with short critical sections.
- Parsing and validation are sequential, not parallel. At this load it costs microseconds per message (to be confirmed in
  the load tests); parallelism is in the apply stage, where per-plate partitioning keeps correctness cheap.
- A full partition channel stalls the single receive stage (head-of-line blocking). It is visible in the buffer depth metric
  and in ingest lag, and it is the intended backpressure path.
- Stage 2 (partitioned stream, several consumers) cannot take the maximum over partitions: it must use the minimum of the
  partitions' maxima as the watermark, as event-time stream processors do, otherwise pitfall 1 returns across consumers.
- Rule changes this ADR implies (applied to the domain documents once the ADR is accepted):
  - BR-LB-11, BR-ING-06: the watermark is the newest timestamp of **valid** measurements in arrival order (a measurement
    rejected by the sequence rule still counts as evidence that time has passed), and decisions use the stamped value.
  - BR-ING-01: a timestamp too far ahead of the processor's clock is invalid.
  - New acceptance scenarios: order independence of boards; a far-future timestamp is rejected and does not expire the boards.
- Open: default partition count and channel capacity (measured in load tests), the future-timestamp tolerance, the stable
  hash function.
