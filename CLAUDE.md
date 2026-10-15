# Yunex Highway Speed Enforcement Pipeline

Interview-style prototype (Senior C# assignment). Three decoupled apps: telemetry **Generator** (console) →
**Processor** (.NET service in Docker, REST API) → **Client** (WPF dashboard). The assignment explicitly values design
choices and structure over polish; keep the solution as small as the agreed behavior allows.

## Language

Talk to the owner in Czech. Everything stored in the repo (docs, code, comments, commit messages) is English.

## Source of truth, in priority order

1. `docs/assignment/CodingAssignment_HighwaySpeedEnforcementPipeline.pdf` — the original requirements.
2. `docs/domain/` — the domain model. Always consult before changing behavior:
   - @docs/domain/glossary.md — ubiquitous language; use these terms exactly.
   - @docs/domain/business-rules.md — rules with IDs (`BR-xx`) and provenance (Assignment / Interpretation / Extension).
   - `docs/domain/domain-model.md` — concepts, vehicle lifecycle, invariants.
   - `docs/domain/acceptance-scenarios.md` — behavior scenarios (`AC-xx`) that tests are derived from.
   - `docs/contracts/` — wire contracts: MQTT telemetry messages and the REST snapshot, with example JSON files.
3. `docs/adr/` — architecture decisions. Only `Accepted` ADRs bind; `Proposed` ones are discussion. Technology stack:
   ADR-0002. Testing approach: `docs/testing-strategy.md` (accepted).
4. `docs/requirements.md` — an unreviewed ChatGPT draft. Use it as input only; it is **not** authoritative and goes
   beyond the PDF (recovery, metrics, old-event rejection).

`docs/assignment-ambiguities.md` records each ambiguity in the assignment, our decision and the reason. Entries with status `Decided` are final.

## Rules for working here

- Never silently resolve an ambiguity. If behavior depends on an unresolved question, stop and add or update an entry in
  `docs/assignment-ambiguities.md`, then ask.
- Use the glossary terms exactly in code, tests, docs and conversation (once the glossary exists).
- Cite the business-rule ID (`BR-xx`) in tests and in comments where code enforces a rule.
- **NuGet packages: only well-known, maintained ones, and none without the owner's approval.** The approved set is the list in
  `Directory.Packages.props` (Microsoft packages, `CommunityToolkit.Mvvm`, `MQTTnet`, `Testcontainers`, xUnit). Do not add any other
  package, however small; prefer the base class library (P/Invoke, ANSI escapes, `System.Text.Json`) and write the few lines yourself.
  If a package seems necessary, stop and ask.
- Distinguish what the assignment requires from what we add. Do not add infrastructure "because production would need it".
- Record every non-obvious architecture/technology choice as an ADR (`docs/adr/0000-template.md`), including rejected options.
- Docs describe decisions and rules, not code structure; do not duplicate what the code shows.

## Workflow phases

1. **Requirements/domain** — resolve ambiguities, write glossary, business rules, acceptance scenarios, wire contracts.
   *(done; all ambiguities are decided)*
2. **Architecture** — ADR-0001 transport, ADR-0002 stack, ADR-0003 concurrency, ADR-0004 structure; solution skeleton builds
   and its architecture tests pass. Scope is tier 2 (AMB-06); durable recovery (tier 3) is a stretch goal only if time
   remains. *(done)*
3. **Component design** — a design and an implementation plan per component (Processor, Generator, Client).
   *(done for all three: `docs/design/<component>.md`, `docs/plans/<component>-implementation-plan.md`)*
4. **Implementation** — behavior-first, tests derived from acceptance scenarios, following the component plans.
   *(done for all three; deviation logs in `docs/design/<component>-implementation-notes.md`)*

All three applications are implemented: the **processor** (`src/Processor/*`: MQTT source, ingest pipeline, boards, REST
snapshot, health and status endpoints, Docker image), the **generator** (`src/Generator/*`) and the **client**
(`src/Client/*`). `README.md` at the root is the reviewer's entry point (how to build, test and run the whole system).
Spikes are throwaway and go outside `src/`.

## Environment and commands

Windows 11, PowerShell or Git Bash. .NET SDK 10.0.x (pinned in `global.json`) and Docker are required. The solution file is
`Yunex.HighwaySpeedEnforcement.slnx`; projects are in `src/` and `tests/` (ADR-0004). NuGet uses only nuget.org
(`nuget.config`); package versions are central (`Directory.Packages.props`).

| Task | Command |
|---|---|
| Build everything (strict: warnings are errors) | `dotnet build Yunex.HighwaySpeedEnforcement.slnx` |
| Run all tests (unit, component, E2E; E2E needs Docker) | `dotnet test --solution Yunex.HighwaySpeedEnforcement.slnx` |
| Run one test project | `dotnet test --project tests/HighwaySpeedEnforcement.Unit.Tests` |
| Run the E2E tests only (Docker; builds the processor image) | `dotnet test --project tests/HighwaySpeedEnforcement.E2E.Tests` |
| Run the load test (Docker; about 6 minutes, `HSE_LOAD_SECONDS` shortens the steady phase) | `dotnet test --project tests/HighwaySpeedEnforcement.E2E.Tests -p:TestingPlatformCommandLineArguments="--filter-trait Category=Load"` |
| List acceptance scenarios without a test | `pwsh tools/ac-coverage.ps1 -ProcessorScenarios AC-04,AC-05,...` |
| Check formatting | `dotnet format Yunex.HighwaySpeedEnforcement.slnx --verify-no-changes` |
| Start broker and processor | `docker compose up -d --build` (REST on `http://127.0.0.1:8080`, broker on `127.0.0.1:1883`) |
| Processor endpoints | `GET /api/traffic-snapshot`, `GET /api/status`, `GET /ui` (status page: throughput, ingest lag, buffer, tracked vehicles), `GET /health/live`, `GET /health/ready` |

The load test carries the traits `Category=E2E` and `Category=Load`; the E2E project excludes `Category=Load` by default
(`TestingPlatformCommandLineArguments` in its csproj), so it only runs with the explicit filter above.

Tests use xUnit v3 on the Microsoft Testing Platform (opted in through `global.json`). The architecture tests in
`HighwaySpeedEnforcement.Unit.Tests/Architecture` fail the build when a project references something its layer must not
(ADR-0004); a new production project needs an entry in their rule table.

Connect to `127.0.0.1`, not `localhost`, from the host (`localhost` is slow with Docker Desktop, see `spikes/mqtt/FINDINGS.md`).

## Generator (implemented)

Run it against the compose broker with
`dotnet run --project src/Generator/HighwaySpeedEnforcement.Generator -- --Generator:Seed=42 --Generator:MaxTicks=15`
(settings: design `docs/design/generator.md` section 6; without `--Generator:Seed` the plate state file under
`LocalApplicationData` is used, so plates stay unique across runs). E2E tests under `tests/HighwaySpeedEnforcement.E2E.Tests/Generator`
need Docker. Deviations and open questions: `docs/design/generator-implementation-notes.md`.
In a terminal the generator is interactive (keys `P`, `H`, `+`/`-`, `1`-`0`, `Q`, status bar; design section 11, `src/Generator/.../Interactive/`);
`--Generator:Console:Interactive=false` or redirected output turns it off, so tests and piped runs are unaffected.

## Client (implemented)

Run it against the compose processor with `dotnet run --project src/Client/HighwaySpeedEnforcement.Client.Wpf` (settings:
design `docs/design/client.md` section 4, `appsettings.json` section `Client`, overridable as `Client__BaseUrl` or
`--Client:BaseUrl=...`). View models are tested without WPF under `tests/HighwaySpeedEnforcement.Unit.Tests/Client`; the
window is checked by a manual smoke run (screenshots in `docs/client/`). The look (a parody "NOT YUNEX" wordmark of our own, mint palette)
lives in `src/Client/HighwaySpeedEnforcement.Client.Wpf/Resources`.
Deviations and open questions: `docs/design/client-implementation-notes.md`.

## Code style (enforced by the build)

`.editorconfig` is the source of truth; `Directory.Build.props` turns warnings and code style violations into build errors.
Run `dotnet build` and `dotnet format` before committing. Summary of the conventions:

- Allman braces: opening and closing braces on their own lines. **Always braces**, never one-line `if`, `else`, `for`, `while`.
- File-scoped namespaces, `using` directives outside the namespace, explicit accessibility modifiers.
- Private and internal fields `_camelCase`, interfaces start with `I`, constants and members PascalCase.
- Nullable reference types are on; no suppressing a warning to make the build pass without a reason in a comment.
- Spikes (`spikes/`) are exempt (own `Directory.Build.props`).

## Layering (ADR-0004)

Per app: Domain, Application, Infrastructure, host. Domain references nothing; Application references only Domain; wire
contracts (`HighwaySpeedEnforcement.Processor.Contracts`) never reach Domain or Application. Ports are defined by the inner layer and implemented in
Infrastructure. Do not add a mediator, repositories or a layer without content.

Folders: one folder per application under `src/` (`src/Processor`, `src/Generator`, `src/Client`); no shared folder. A new
project goes into its application's folder, never directly under `src/`; the folder on disk equals the solution folder.
Wire contracts belong to the application that defines them: `HighwaySpeedEnforcement.Processor.Contracts` holds both the
telemetry input and the REST output of the processor, and the generator and the client reference it directly (no package).
