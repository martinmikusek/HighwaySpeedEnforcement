# REST API (Processor to Client)

**Status:** Accepted (implemented; the examples are approved files of the contract tests). Example: `examples/traffic-snapshot.json` (used as an approved file in contract tests).
Related: BR-API-01..03, BR-UI-01..04, ADR-0003 (snapshot consistency).

## GET /api/traffic-snapshot

The endpoint and the three top-level contents (ten camera boards, the global board, a UTC generation time) are required by
the assignment. Everything else below is our design.

**Request.** No query parameters.

**Response 200**, `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store`. Property names are camelCase.
All times are ISO 8601 UTC text ending in `Z`.

### TrafficSnapshot

| Property | Type | Meaning |
|---|---|---|
| `generatedAtUtc` | datetime | When the processor assembled this snapshot (processor clock) |
| `cameraBoards` | array of CameraBoard | Always exactly 10 items, ordered by `cameraId` 1 to 10, empty boards included |
| `globalBoard` | GlobalBoard | The global leaderboard |

### CameraBoard

| Property | Type | Meaning |
|---|---|---|
| `cameraId` | integer | 1 to 10 |
| `latestEventUtc` | datetime or null | Event time of the newest message this board reflects (the board's watermark); null if it never received data |
| `entries` | array of CameraEntry | At most 10, in rank order |

### CameraEntry

| Property | Type | Meaning |
|---|---|---|
| `rank` | integer | 1-based position, consecutive |
| `numberPlate` | string | Vehicle |
| `speedKmh` | number | Measured speed at this camera, unrounded |
| `measuredAtUtc` | datetime | Event time of the measurement |

### GlobalBoard

| Property | Type | Meaning |
|---|---|---|
| `latestEventUtc` | datetime or null | As above, for the global watermark |
| `entries` | array of GlobalEntry | At most 10, in rank order |

### GlobalEntry

| Property | Type | Meaning |
|---|---|---|
| `rank` | integer | 1-based position, consecutive |
| `numberPlate` | string | Vehicle |
| `averageSpeedKmh` | number | Cumulative average over the accepted measurements, unrounded |
| `measurementCount` | integer | How many measurements the average is based on (3 to 10) |
| `measuredAtUtc` | datetime | Event time of the vehicle's latest accepted measurement |

### Rules

- Entries are in rank order (BR-LB-05): speed descending, then plate ascending (ordinal). Ranks start at 1 and are consecutive.
- Speeds and averages are not rounded; presentation rounds them. Ranking uses the unrounded values.
- A board with no live entries has `entries: []`. `latestEventUtc` can still be set (the board has seen data, then everything expired).
- The snapshot is a detached copy; each board is internally consistent, the boards are not read at one instant (BR-API-02).
- The snapshot carries facts only. Whether a board counts as *live* is the client's decision: `generatedAtUtc − latestEventUtc` below its own `LiveLagThreshold` (BR-UI-04).

Example: `examples/traffic-snapshot.json`. In it camera 7 shows a frozen board (old `latestEventUtc`) and camera 10
never received data (`latestEventUtc: null`).

### Other responses

| Status | When | Body |
|---|---|---|
| 503 | The processor is not ready to serve snapshots (starting up) | Problem details (RFC 9457) |
| 500 | Unexpected failure | Problem details; the client treats it like any failed poll (BR-UI-03) |

## Health

`GET /health/live` returns 200 while the process runs. `GET /health/ready` returns 200 when the processor can serve snapshots,
503 otherwise (BR-API-03). An idle input stream does not make it unready. Exact readiness conditions are decided in the
processor design.

## Versioning

- The route is fixed by the assignment. Additive changes (new properties) are allowed; the client ignores unknown properties.
- A breaking change goes to a new route (`/api/v2/traffic-snapshot`); the current one stays.
- An OpenAPI description generated from the DTOs is optional and not part of tier 2.

## Decisions and alternatives

| Decision | Alternatives considered | Why |
|---|---|---|
| camelCase, ISO 8601 text | PascalCase, ticks | Web convention and the ASP.NET Core default; text avoids 64-bit precision problems in other consumers. |
| Units in property names (`speedKmh`) | Plain `speed` | The assignment stresses km/h; the name keeps it visible. |
| Separate entry shapes for camera and global boards | One shape with a generic `score` | Typed fields are clearer in the client and in the examples; the two scores mean different things. |
| `measurementCount` on global entries (no `isCompleted`) | Only the average; also a completion flag | The count shows how many cameras the average is based on, which matters because of camera gaps and the 3-measurement gate. A completion flag was dropped: it is not needed for display and is weak under camera outages. |
| Explicit `rank` | Position implied by array order | The client does not need to compute it and tests can assert it. |
| Fixed array of 10 camera boards | Dictionary keyed by camera | Stable order, matches "a collection of the 10 camera leaderboards". |
