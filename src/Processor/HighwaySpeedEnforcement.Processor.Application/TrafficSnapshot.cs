using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// A detached, coherent-per-board copy of all boards (BR-API-01, BR-API-02). <paramref name="CameraBoards"/> is in camera
/// order (index 0 is Camera 1). Not the wire type; the host maps it to the REST DTO.
/// </summary>
public sealed record TrafficSnapshot(DateTimeOffset GeneratedAt, IReadOnlyList<BoardView> CameraBoards, BoardView GlobalBoard);
