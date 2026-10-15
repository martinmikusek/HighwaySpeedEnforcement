# ADR-0002: Technology stack

- **Status:** Accepted (2026-10-13)
- **Date:** 2026-10-13
- **Related:** ADR-0001 (transport), `docs/testing-strategy.md`, AMB-06

## Context

The assignment requires .NET (8, 9 or 10) for the processor, Docker for the processor, a WPF client, and a console
generator. ADR-0001 fixed the transport (MQTT). This ADR fixes the remaining technology choices so that component design
and implementation start from one list. Choices that belong to component design (project split, concurrency model) are
not made here.

Constraints: .NET SDK 10.0.401 and Docker 29 are installed on the development machine; the .NET 10 SDK and ASP.NET runtime
images are available (`mcr.microsoft.com/dotnet/sdk:10.0`, `.../aspnet:10.0`, checked 2026-10-13); the reviewer should be
able to run everything with `dotnet` and `docker compose`.

## Decision

| Area | Choice | Note |
|---|---|---|
| Runtime and language | C# on **.NET 10** (LTS) for all projects; WPF project targets `net10.0-windows` | One runtime version across all apps. Nullable reference types on, warnings as errors. |
| SDK pinning | `global.json` pinned to the installed 10.0 SDK, roll-forward to the latest feature band | Reproducible builds. |
| Repository layout | Monorepo: `src/`, `tests/`, `docs/`, `spikes/` (throwaway), `docker-compose.yml` at the root | Spikes never referenced from `src/`. |
| Build conventions | `Directory.Build.props` (shared settings), central package management (`Directory.Packages.props`), `.editorconfig` | Package versions chosen at the time they are added, then pinned centrally. |
| Processor host | ASP.NET Core minimal API + hosted services (Generic Host) | REST endpoint, health checks, MQTT subscriber as a hosted service. |
| Processor internals | `System.Threading.Channels` for the ingest buffer and per-plate worker queues; no actor framework | Concurrency design in a later ADR. |
| MQTT | **MQTTnet** 5.x, MQTT 3.1.1, QoS 1 (per ADR-0001); Mosquitto in the compose file with the tuned limits from the spike | MQTT 5 not used in stage 1. |
| Message encoding | JSON (UTF-8) via source-generated `System.Text.Json` | Specified in `docs/contracts/`. |
| Logging | `Microsoft.Extensions.Logging`, structured, JSON console output in the container | No additional logging framework unless a need appears. |
| Metrics and health | `System.Diagnostics.Metrics` for counters and gauges; ASP.NET Core health checks for liveness and readiness | How metrics are read without a scraping stack is decided in the processor design. |
| Configuration | Options pattern, `appsettings.json` plus environment variables and command-line arguments | Window parameters (BR-CFG-01) are options. |
| Time | `TimeProvider` injected wherever wall-clock time is used; domain logic uses event time only | Deterministic tests. |
| Generator | .NET Generic Host console app; MQTTnet publisher; injectable seeded random source | Seeded runs make E2E results reproducible. |
| Client | **WPF with MVVM** using **CommunityToolkit.Mvvm** (source-generated observable properties and commands); `Microsoft.Extensions.DependencyInjection`/`Hosting`; typed `HttpClient` via `IHttpClientFactory`; `System.Text.Json` | No logic in code-behind. Polling loop on a `PeriodicTimer`; UI updates marshalled to the UI thread only for binding updates. |
| Container | Multi-stage `Dockerfile` for the processor (SDK image to build, ASP.NET image to run); `docker-compose.yml` with broker and processor | Generator and client run on the host. |
| Tests | xUnit; see `docs/testing-strategy.md` | Libraries listed there. |
| CI | Optional GitHub Actions (build, unit and integration tests; E2E needs Docker; WPF build needs a Windows runner) | Not required by the assignment. |

## Alternatives considered

- **Other MVVM frameworks (Prism, ReactiveUI).** More structure than a single-window dashboard needs. CommunityToolkit.Mvvm is
  small and keeps view models plain classes.
- **Serilog or other logging frameworks.** Not rejected on principle; the built-in logger is enough for the required
  structured logs. Reconsider if sinks or enrichers are needed.
- **Framework 8 or 9.** Allowed by the assignment but no benefit; .NET 10 is the current LTS and installed.
- **Minimal API vs controllers.** One endpoint; minimal API is enough.

## Consequences

- One runtime and one toolchain for all apps; WPF forces the client and its tests onto Windows.
- Few third-party dependencies: MQTTnet, CommunityToolkit.Mvvm, test libraries.
- Open: message encoding, how metrics are exposed, whether CI is set up.
