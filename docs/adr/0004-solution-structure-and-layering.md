# ADR-0004: Solution structure and layering

- **Status:** Accepted (2026-10-14)
- **Date:** 2026-10-14
- **Related:** ADR-0001 (stage 2 swaps the input adapter), ADR-0002 (stack), ADR-0003 (processor internals), `docs/testing-strategy.md`

## Context

The solution has three applications. The processor holds almost all domain logic (sequence rule, vehicle tracks, ranked boards,
watermarks, partitioned pipeline), the generator has a simulation, the client has polling and presentation logic.

Forces:

- **The domain logic is the core of the system and must be independent**: of MQTT, of HTTP, of WPF, of the clock. It must be
  testable on its own, and its input must be replaceable (MQTT now, a stream in stage 2).
- The wire contracts are shared by producer and consumer and are versioned independently of the domain.
- The owner wants the layers clearly separated.
- The assignment is a prototype, so a layer must contain real code; forwarding classes are to be avoided.

## Options considered

1. **One project per application.** Simplest; domain separated only by folders, nothing stops it from reaching for infrastructure types.
2. **Core library per app plus thin hosts** (ports and adapters at a few seams). Fewer projects; the domain and the orchestration
   share one assembly.
3. **Layered per app: Domain, Application, Infrastructure, host** (chosen). Domain and orchestration are separate assemblies, so
   the independence of the domain is enforced by project references, not by discipline.

Option 3 has more projects than the logic strictly needs (15 in total). It was chosen by the owner for a clean, enforceable
separation; the cost is accepted and limited by the rules below (no mediator, no repository layer, no forwarding classes).

## Decision

Option 3. Namespaces and assemblies use the prefix `HighwaySpeedEnforcement` (the product name; the `Yunex` company part of the repository name is not repeated in code).

### Layers

| Layer | Responsibility | May reference |
|---|---|---|
| **Domain** | Business rules and model; pure, no I/O, no framework | nothing |
| **Application** | Orchestration (use cases) and the **ports** it needs; no framework | Domain; `Microsoft.Extensions.*.Abstractions` |
| **Infrastructure** | **Adapters** that implement ports: MQTT, HTTP, metrics | Application, `HighwaySpeedEnforcement.Processor.Contracts`, third-party libraries |
| **Host** (Api, console, WPF) | Entry point, composition root, presentation | all of the above |

Dependencies point inward only: Host, Infrastructure, Application, Domain. `HighwaySpeedEnforcement.Processor.Contracts` (wire types) is referenced by
Infrastructure and the hosts, never by Domain or Application.

### Projects

| Project | Layer | Contains |
|---|---|---|
| `HighwaySpeedEnforcement.Processor.Contracts` | wire contracts of the processor | MQTT `Measurement` and `Heartbeat` (the processor's input), REST snapshot DTOs (its output), source-generated JSON contexts, topic and route constants (`docs/contracts/`). Owned by the processor because it defines both schemas; the generator and the client reference the project directly (no package: contract changes are made expand-and-contract in the monorepo). |
| `HighwaySpeedEnforcement.Processor.Domain` | Domain | `Measurement`, `VehicleTrack`, sequence rule, `RankedBoard`, board entries, watermarks, window and linger rules |
| `HighwaySpeedEnforcement.Processor.Application` | Application | ingest pipeline (validation, stamping, partitioning, workers), snapshot service; ports: the ingest sink that input adapters push into (carries acknowledgement and backpressure, ADR-0001), metrics |
| `HighwaySpeedEnforcement.Processor.Infrastructure` | Infrastructure | MQTT input adapter and JSON decoding, metrics implementation |
| `HighwaySpeedEnforcement.Processor.Api` | Host | `Program`, REST endpoints and mapping to DTOs, health checks, options binding, composition root, `Dockerfile` |
| `HighwaySpeedEnforcement.Generator.Domain` | Domain | simulation (tick state machine, vehicles, plate generation, speed model) and its output port (a sink for emitted messages) |
| `HighwaySpeedEnforcement.Generator.Infrastructure` | Infrastructure | MQTT publisher adapter(s): one connection, optionally one per camera |
| `HighwaySpeedEnforcement.Generator` | Host | console host, run loop, configuration, composition root |
| `HighwaySpeedEnforcement.Client.Application` | Application | polling use case (fixed rate, no overlap), snapshot state with stale and live judgment, `ISnapshotClient` port |
| `HighwaySpeedEnforcement.Client.Infrastructure` | Infrastructure | HTTP snapshot client (`HttpClient`, `System.Text.Json`) |
| `HighwaySpeedEnforcement.Client.ViewModels` | Presentation | view models (CommunityToolkit.Mvvm), UI dispatcher port; no WPF reference |
| `HighwaySpeedEnforcement.Client.Wpf` | Host | views, `App`, composition root, dispatcher adapter (`net10.0-windows`) |
| `HighwaySpeedEnforcement.Unit.Tests` | tests | domain, application, model-based, generator and view-model tests |
| `HighwaySpeedEnforcement.Component.Tests` | tests | processor in memory through `WebApplicationFactory`; contract checks against `docs/contracts/examples` |
| `HighwaySpeedEnforcement.E2E.Tests` | tests | Testcontainers: real broker, processor container, generator process |

**MVVM in the client.** The MVVM pattern lives inside the client's layers: *Model* is `HighwaySpeedEnforcement.Client.Application` (snapshot state,
polling, ports), *ViewModel* is `HighwaySpeedEnforcement.Client.ViewModels` (no WPF reference, so view models are testable on any machine and
cannot touch WPF types), *View* is `HighwaySpeedEnforcement.Client.Wpf` (`Views/` folder with XAML, composition root). The alternative of one WPF
project with `Models/`, `ViewModels/` and `Views/` folders is simpler, but its view-model tests would run on Windows only.
`HighwaySpeedEnforcement.Client.Application`, `HighwaySpeedEnforcement.Client.ViewModels` and `HighwaySpeedEnforcement.Client.Infrastructure` are candidates for merging if they turn
out thin; that is decided in the client design.

### Rules

1. **Dependency rule** as in the table; enforced by project references, plus an executable check: a unit test asserts that
   Domain and Application assemblies reference none of MQTTnet, ASP.NET Core, WPF or `HighwaySpeedEnforcement.Processor.Contracts`, and that Domain references
   no other `HighwaySpeedEnforcement` assembly.
2. **Ports are defined by the inner layer that needs them** and implemented in Infrastructure. Four are required: the
   processor's input (ingest sink), the clock (`TimeProvider`, from the base library), the generator's output sink, and the
   client's snapshot source and UI dispatcher.
3. **Contracts stay at the edge.** Domain has its own small immutable types. Infrastructure maps wire messages to domain
   types (processor input) and the host maps domain results to REST DTOs, each in one place. A wire change (a `v2` topic)
   does not reach the domain.
4. **No mediator, no repositories, no use-case-per-class.** Application contains the ingest pipeline and a few services;
   boards are in-memory structures, not stores.
5. **A layer without content is not created.** The generator has no Application project because its run loop is a few lines
   in the host.
6. **Tests live in their own projects**, never inside production assemblies (test code and test libraries must not ship).
   They are split by test level, as in the testing strategy: `HighwaySpeedEnforcement.Unit.Tests`, `HighwaySpeedEnforcement.Component.Tests`, `HighwaySpeedEnforcement.E2E.Tests` (suffix
   `.Tests`, folders inside `HighwaySpeedEnforcement.Unit.Tests` mirror the production projects). Tests reference production projects, never the
   reverse; internals are shared with `InternalsVisibleTo` only where unavoidable. A test project per production project was
   rejected: it would add a dozen more projects.

### Repository layout and conventions

```
src/Processor/  HighwaySpeedEnforcement.Processor.Contracts  HighwaySpeedEnforcement.Processor.Domain  HighwaySpeedEnforcement.Processor.Application  HighwaySpeedEnforcement.Processor.Infrastructure  HighwaySpeedEnforcement.Processor.Api
src/Generator/  HighwaySpeedEnforcement.Generator.Domain  HighwaySpeedEnforcement.Generator.Infrastructure  HighwaySpeedEnforcement.Generator
src/Client/     HighwaySpeedEnforcement.Client.Application  HighwaySpeedEnforcement.Client.Infrastructure  HighwaySpeedEnforcement.Client.ViewModels  HighwaySpeedEnforcement.Client.Wpf
tests/          HighwaySpeedEnforcement.Unit.Tests  HighwaySpeedEnforcement.Component.Tests  HighwaySpeedEnforcement.E2E.Tests
deploy/         broker configuration (moved from the spike) and the processor compose service
docs/  spikes/  (as now)
Yunex.HighwaySpeedEnforcement.slnx   Directory.Build.props   Directory.Packages.props   global.json
.editorconfig   .gitattributes   docker-compose.yml
```

- Solution file in the `.slnx` format (the default of the .NET 10 SDK), with solution folders per application.
- **One folder per application under `src/`** (`Processor`, `Generator`, `Client`); the folder on disk equals the solution
  folder. A new project goes into the folder of its application, never directly under `src/`. There is no shared folder:
  a contract belongs to the application that defines it and lives in its folder as `<App>.Contracts`, referenced directly by
  the consumers. (Owner's decision, 2026-10-14; a flat `src/` with 12 projects was unreadable, and a generic `Shared` project
  hides who owns a contract.)
- `Directory.Build.props`: target framework, nullable reference types, implicit usings, **warnings as errors**, code style
  enforced in the build. Spikes have their own relaxed `Directory.Build.props`.
- `.editorconfig`: standard .NET conventions with Allman braces and mandatory braces for every `if`, loop and `else`
  (no one-line statements); naming rules (`_camelCase` private fields, `I` prefix); formatting and style violations fail the
  build (verified with a deliberately violating project: IDE0011, IDE0055, IDE0161, IDE0044 failed the build, clean code passed).
- `.gitattributes` keeps LF in the repository and in working copies.
- `Directory.Packages.props`: central package versions. `global.json`: pins the SDK.
- The processor `Dockerfile` lives next to the host and is built with the repository root as context.

## Consequences

- Domain independence is a build-time fact, not a convention; stage 2 replaces the processor's input adapter without touching
  Domain or Application.
- 15 projects: more build time and more references to maintain than a leaner layout. Accepted.
- View models and polling logic are testable on any machine; only `HighwaySpeedEnforcement.Client.Wpf` needs Windows.
- Mapping at the edges costs a small amount of duplicated record types and one mapper per host.
- Not decided here: class-level design of each component (separate component designs), CI.
