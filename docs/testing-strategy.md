# Testing strategy

**Status:** Accepted (2026-10-13). Decisions are listed below; no open questions.

Terms and IDs: `docs/domain/glossary.md`, `business-rules.md` (`BR-xx`), `acceptance-scenarios.md` (`AC-xx`).

## Principles

1. **Tests come from the domain documents.** Every acceptance scenario `AC-xx` is covered by at least one automated test,
   and every test names the scenario or rule it checks (`[Trait("AC","AC-10")]`, `[Trait("BR","BR-LB-03")]`).
2. **Deterministic by construction.** Domain logic uses event time only, wall-clock time comes from an injected
   `TimeProvider` (fake in tests), and the generator takes a seeded random source. No sleeps to "wait long enough".
3. **Pyramid, not ice-cream cone.** Many fast unit and model-based tests, fewer component tests, a handful of strong E2E tests.
4. **Test behavior, not structure.** Public contracts and observable outcomes; internals stay free to change.
5. **E2E tests run the real artifacts:** the real broker and the real processor container, the real generator process.

## Levels

| Level | Scope | Main tools | Proves |
|---|---|---|---|
| Unit: domain | Sequence rule, vehicle track, cumulative average, ranked set (rank order, tie-break, update, remove, expiry, pruning), watermark, orphan timeout | xUnit, `FakeTimeProvider` where needed | BR-ING-*, BR-LB-*; AC-04..21 |
| Model-based / property | Leaderboard compared with a naive oracle that recomputes the top 10 from all live entries after every random operation sequence; pruning never changes the top 10; concurrent producers give the same result as sequential | FsCheck (or a seeded random generator in plain xUnit) | BR-LB-09, BR-LB-10, AC-09, AC-20 |
| Unit: generator | Tick state machine: 100 new vehicles, 10 messages per vehicle, no Camera 11, unique plates, speed bounds | xUnit, seeded random, fake sink | BR-SIM-*; AC-01..03 |
| Unit: client | View models: polling loop (no overlap, fixed rate), stale state, last success time, camera selection | xUnit, `FakeTimeProvider`, fake snapshot client | BR-UI-*; AC-25..28 |
| Component: processor | Processor in memory through `WebApplicationFactory`, measurements fed via the input abstraction (no broker), REST called as a client would | xUnit, `WebApplicationFactory` | BR-API-*; AC-22..24, AC-29, snapshot contract |
| Contract | Snapshot JSON shape and MQTT payload schema are checked against stored examples | xUnit, approved JSON files | Compatibility between apps |
| E2E: generator | Real generator process publishes to a real broker; a test subscriber verifies every vehicle's 10 messages, order, plates, 1,000 per tick in steady state, no Camera 11 | Testcontainers (Mosquitto), xUnit | AC-01..03 |
| E2E: pipeline, scripted | Real broker and processor container; a **test publisher replaces the generator** and publishes exactly the measurements a scenario needs, with synthetic timestamps. All window, linger and timeout rules use event time, so a scenario that spans minutes of event time runs in milliseconds. REST is polled and compared with an independent oracle. Boards freeze at the last timestamp (BR-LB-12), so the final snapshot is exact. | Testcontainers, xUnit | AC-10..24 end to end |
| E2E: pipeline, full smoke | Real generator (real clock), broker, processor container and REST, one run of about 15 s. Asserts invariants (at most 10 entries, rank order, about 900 tracked vehicles after warm-up, no rejections), not exact content | Testcontainers, xUnit | The three apps work together |
| E2E: resilience (tier 2) | Broker restart: generator and processor reconnect; processor restart: boards refill within the window; client sees stale then fresh data | Testcontainers, xUnit | BR-UI-03, ADR-0001 behavior |
| Load / soak | 1,000 messages/s for several minutes, and a 10x burst: ingest lag stays below the threshold, tracked vehicles stay near 900 (no leak), no unexpected rejections | Test harness with metrics, optional BenchmarkDotNet for the ranked set | CON-01, BR-OPS-02 |

## Traceability

Each `AC-xx` maps to at least one level above; the mapping is kept in the test names and traits, and a small script lists
`AC` ids without a test (so gaps are visible, not discovered at review time). `BR-xx` rules are covered through their
scenarios; rules without a scenario get their own test.

## Infrastructure

- `docker compose` provides the broker with the tuned configuration; E2E tests start their own containers through Testcontainers.
- `dotnet test` runs unit, model-based and component tests without Docker. E2E and load tests are marked
  (`[Trait("Category","E2E")]`) and need Docker; WPF tests need Windows.
- Assertion and mocking libraries: xUnit assertions plus Shouldly; NSubstitute only where a hand-written fake is worse.
  (Check licensing of any assertion library before adding it.)

## Out of scope

Security testing, TLS and authentication, multi-instance behavior, long-term durability (tier 3), cross-machine clock skew,
pixel-level UI checks, **automated UI tests of the real WPF window** (view-model tests cover the behavior; a manual smoke
check covers the binding).

## Decisions

- **UI testing:** view-model tests are enough; no FlaUI-style automation.
- **E2E speed:** no accelerated generator ticks. Window, linger and timeout rules run on event time, so scripted E2E
  scenarios use synthetic timestamps and finish immediately; real-time runs exist only for generator output and one full smoke test.
- **CI:** GitHub Actions is feasible and cheap (one workflow file): a build-and-unit job on a Windows runner (needed for
  WPF) and an optional Linux job for component and E2E tests with Testcontainers. Whether E2E runs on every push or on
  demand depends on measured duration. It is a late, optional task.
- **Load tests:** wanted. Proposed pass criteria (adjust after the first measurements): at 1,000 messages/s for 5 minutes,
  ingest lag p99 below 1 s, tracked vehicles stable around 900, zero rejected measurements, no memory growth after warm-up;
  a 10x burst (10,000 messages within 1 s) is absorbed and lag returns below 1 s within 10 s.
- **Model-based tests:** a seeded random generator in plain xUnit, with the failing seed printed so any failure can be
  reproduced. FsCheck (generation of random inputs plus automatic shrinking to a minimal failing case) can replace the
  loop later without changing the shape of the tests.
- **Traceability:** test traits plus a script that lists acceptance scenarios without a test. No generated matrix document.
- **Coverage and mutation testing:** not pursued. The rule is that every `AC-xx` has a test.

## Open questions

None at the moment.
