using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Unit.Tests.Client;

/// <summary>Snapshots for the client tests; boards are empty unless a test adds entries.</summary>
internal static class ClientTestData
{
    public static readonly DateTimeOffset GeneratedAt = new(2026, 10, 14, 14, 3, 10, TimeSpan.Zero);

    /// <summary>Ten empty camera boards and an empty global board; <paramref name="latestEvent"/> gives the watermark per camera (0 is the global board).</summary>
    public static TrafficSnapshot Snapshot(DateTimeOffset generatedAt, Func<int, DateTimeOffset?>? latestEvent = null)
    {
        latestEvent ??= _ => generatedAt;
        var cameraBoards = Enumerable.Range(1, TrafficSnapshot.CameraCount)
            .Select(camera => new CameraBoard(camera, latestEvent(camera), []))
            .ToArray();

        return new TrafficSnapshot(generatedAt, cameraBoards, new GlobalBoard(latestEvent(0), []));
    }

    public static TrafficSnapshot WithCameraEntries(this TrafficSnapshot snapshot, int camera, params CameraEntry[] entries)
    {
        var boards = snapshot.CameraBoards.ToArray();
        boards[camera - 1] = boards[camera - 1] with { Entries = entries };
        return snapshot with { CameraBoards = boards };
    }

    public static TrafficSnapshot WithGlobalEntries(this TrafficSnapshot snapshot, params GlobalEntry[] entries)
    {
        return snapshot with { GlobalBoard = snapshot.GlobalBoard with { Entries = entries } };
    }
}
