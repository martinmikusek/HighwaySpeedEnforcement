using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;
using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Api;

/// <summary>Maps the application snapshot to the REST contract (<c>docs/contracts/rest-api.md</c>, BR-API-01).</summary>
public static class SnapshotMapper
{
    public static TrafficSnapshotDto ToDto(TrafficSnapshot snapshot)
    {
        var cameras = new CameraBoardDto[snapshot.CameraBoards.Count];

        for (int i = 0; i < cameras.Length; i++)
        {
            var board = snapshot.CameraBoards[i];
            cameras[i] = new CameraBoardDto(
                HighwayLayout.FirstCamera + i,
                ToUtc(board.LatestEventTicks),
                board.Entries.Select(ToCameraEntry).ToArray());
        }

        var global = new GlobalBoardDto(
            ToUtc(snapshot.GlobalBoard.LatestEventTicks),
            snapshot.GlobalBoard.Entries.Select(ToGlobalEntry).ToArray());

        return new TrafficSnapshotDto(snapshot.GeneratedAt, cameras, global);
    }

    private static CameraEntryDto ToCameraEntry(RankedEntry ranked)
    {
        // A camera score is the measured float speed stored as double; the cast restores the exact measured value.
        return new CameraEntryDto(ranked.Rank, ranked.Entry.Plate, (float)ranked.Entry.Score, ToUtc(ranked.Entry.TimestampTicks));
    }

    private static GlobalEntryDto ToGlobalEntry(RankedEntry ranked)
    {
        return new GlobalEntryDto(ranked.Rank, ranked.Entry.Plate, ranked.Entry.Score, ranked.Entry.MeasurementCount, ToUtc(ranked.Entry.TimestampTicks));
    }

    private static DateTimeOffset ToUtc(long ticks)
    {
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static DateTimeOffset? ToUtc(long? ticks)
    {
        return ticks is { } value ? ToUtc(value) : null;
    }
}
