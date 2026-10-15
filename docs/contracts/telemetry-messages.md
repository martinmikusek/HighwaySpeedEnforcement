# Telemetry messages (Generator to Processor)

**Status:** Accepted (implemented; the examples are approved files of the contract tests). Examples: `examples/measurement.json`, `examples/heartbeat.json` (used as approved files in contract tests).
Related: ADR-0001 (transport), BR-TEL-01..03, BR-ING-01, BR-ING-02, BR-ING-07, AMB-04.

## Transport binding

| Item | Value |
|---|---|
| Protocol | MQTT 3.1.1 (ADR-0001) |
| Topic | `highway/telemetry/v1` (one topic for all cameras and both message types) |
| QoS, retain | QoS 1, retain false |
| Payload | One message per MQTT packet, UTF-8 JSON object, no batching |
| Processor client id | `speed-processor`, persistent session (clean session off) |
| Generator client ids | `speed-camera-<cameraId>-<runId>` (one client per camera, the default `PerCamera` mode) or `speed-generator-<runId>` (`SingleConnection` mode) |

**One topic, any number of publishers.** In a real deployment every camera is its own MQTT client publishing to this topic; the
simulation uses one connection for all cameras. MQTT keeps the order of the messages of one publisher connection, which is
what the contract needs: a camera's heartbeat follows that camera's earlier measurements. Order between different cameras is
not guaranteed and not required (a vehicle's consecutive cameras are about a second apart, and a reordered pair is rejected
by the sequence rule, BR-ING-02). Per-camera subtopics (`highway/telemetry/v1/camera/<id>`, one connection per camera) would
preserve the same guarantee and add per-camera access rules and last-will messages; they are an equally valid layout, not
needed for the prototype.

## Message types

### Measurement (the assignment's payload)

The assignment's four fields keep their exact names. `Type` is added to tell message types apart; it is mandatory on every
message (the assignment requires the four fields to be present, not that no others exist).

| Property | JSON type | .NET type | Meaning | Rule |
|---|---|---|---|---|
| `Type` | string | `string` | Constant `"Measurement"` | exactly `Measurement` |
| `NumberPlate` | string | `string` | Vehicle identifier | 1 to 16 characters, not blank; compared ordinally (BR-LB-05) |
| `TimestampTicks` | number (integer) | `long` | UTC `DateTime.Ticks` of the measurement (BR-TEL-02) | valid tick value, not further ahead of the processor clock than `FutureTimestampTolerance` |
| `CurrentSpeed` | number | `float` | Speed in km/h at the moment of passing | finite, at least 0 |
| `CameraId` | number (integer) | `int` | Camera 1 to 10 | 1 to 10 |

Example: `examples/measurement.json`

```json
{"Type":"Measurement","NumberPlate":"1AB 2345","TimestampTicks":639275833894000000,"CurrentSpeed":131.2,"CameraId":5}
```

### Heartbeat (optional extension, BR-TEL-03)

Sent by a camera that sees no vehicle. It carries no vehicle data.

| Property | JSON type | .NET type | Meaning | Rule |
|---|---|---|---|---|
| `Type` | string | `string` | Constant `"Heartbeat"` | exactly `Heartbeat` |
| `CameraId` | number (integer) | `int` | Camera that is alive | 1 to 10 |
| `TimestampTicks` | number (integer) | `long` | UTC ticks: this camera has delivered everything up to this time | as for a measurement |

Example: `examples/heartbeat.json`

```json
{"Type":"Heartbeat","CameraId":5,"TimestampTicks":639275833894000000}
```

**Message type resolution.** `Type` selects the message type: `Measurement` or `Heartbeat`. A missing `Type` is `Malformed`;
any other value is `UnknownType`. There is no default type. A processor that does not implement heartbeats ignores them and
counts them as `Ignored`; they are not errors.

## Encoding rules

- UTF-8 JSON object; property names are case-sensitive PascalCase exactly as above.
- Unknown additional properties are ignored; a missing required property or a value of the wrong JSON type is `Malformed`.
- `TimestampTicks` is a 64-bit integer. Its magnitude (about 6.4e17) exceeds the safe integer range of JavaScript, so every
  consumer must use 64-bit integers. The REST API therefore uses ISO 8601 text instead of ticks.
- `CurrentSpeed` is written with the shortest text that round-trips a 32-bit float (for example `131.2`). JSON cannot
  represent NaN or infinity, so such values can only appear as invalid input and are `Malformed`.
- A message is at most 1,024 bytes; longer payloads are `Malformed`.

## Validation and reason codes

Rejected messages are counted by reason and logged (BR-ING-07). The reason is the metric tag `reason`; the plate is never a tag.

| Reason | Cause | Rule |
|---|---|---|
| `Malformed` | not a JSON object, missing `Type` or another required property, mistyped property, NaN or infinity, too large | BR-ING-01 |
| `UnknownType` | `Type` is neither `Measurement` nor `Heartbeat` | BR-ING-01 |
| `InvalidPlate` | null, empty, blank or longer than 16 characters | BR-ING-01 |
| `InvalidCamera` | `CameraId` outside 1 to 10 | BR-ING-01 |
| `InvalidSpeed` | negative or not finite | BR-ING-01 |
| `InvalidTimestamp` | not a valid UTC tick value, or ahead of the processor clock by more than `FutureTimestampTolerance` | BR-ING-01, ADR-0003 |
| `OutOfSequence` | valid payload, but `CameraId` is not greater than the vehicle's last camera | BR-ING-02 |

The first five payload reasons mean the message is *invalid* and does not move watermarks; `OutOfSequence` is a valid message and does
(BR-LB-11).

## Ordering and time

- A publisher sends the messages of one connection in emission order.
- For one camera, timestamps do not decrease. A `Heartbeat` with timestamp `T` promises that this camera will not later send a
  measurement older than `T`.
- `TimestampTicks` is used for expiry and watermarks, never to order one vehicle's measurements (BR-TEL-02).

## Delivery

At-least-once (QoS 1): the same message can arrive again. Repeats are rejected by the sequence rule without changing state
(ADR-0001, BR-ING-02). Nothing in the payload identifies a delivery attempt.

## Versioning

- Additive changes (new optional properties, new message types) keep the topic. Consumers ignore unknown properties and
  count unknown types as `UnknownType`.
- A breaking change gets a new topic (`highway/telemetry/v2`); both can run side by side.

## Decisions and alternatives

| Decision | Alternatives considered | Why |
|---|---|---|
| JSON | Protobuf, MessagePack | Messages stay readable with any MQTT client during development and review; about 100 bytes at 1,000 messages/s is irrelevant for bandwidth. A binary format can be added later as a new topic version. |
| PascalCase names | camelCase | The assignment defines the field names; keeping them verbatim avoids a translation layer and a reviewer can match the payload to the PDF. |
| Mandatory `Type` on every message | No `Type` on measurements, with "missing means measurement" | An implicit default would turn a damaged or incomplete message into a measurement without anyone noticing; explicit is safer. The extra property does not conflict with the assignment, which requires its four fields to be present. |
| One topic, many publishers | Topic per camera | Simplest for the prototype (one generator connection) and enough for per-camera ordering; per-camera topics stay possible. |
| Plate length limit 16 | No limit | Cheap defense against oversized values; the longest Czech-like plate is 8 characters. |
