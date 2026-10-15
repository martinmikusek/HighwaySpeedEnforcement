using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.Client.Infrastructure;

/// <summary>Converts the REST DTOs of the processor into the client's model; the only place that knows both.</summary>
public static class SnapshotMapper
{
    /// <summary>Throws <see cref="SnapshotUnavailableException"/> when the DTO is incomplete or does not have exactly ten camera boards.</summary>
    public static TrafficSnapshot ToModel(TrafficSnapshotDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CameraBoards is null || dto.GlobalBoard is null)
        {
            throw new SnapshotUnavailableException("snapshot is incomplete");
        }

        if (dto.CameraBoards.Count != TrafficSnapshot.CameraCount)
        {
            throw new SnapshotUnavailableException($"snapshot has {dto.CameraBoards.Count} camera boards, expected {TrafficSnapshot.CameraCount}");
        }

        return new TrafficSnapshot(
            dto.GeneratedAtUtc.ToUniversalTime(),
            dto.CameraBoards.Select(ToModel).ToArray(),
            ToModel(dto.GlobalBoard));
    }

    private static CameraBoard ToModel(CameraBoardDto board)
    {
        if (board is null || board.Entries is null)
        {
            throw new SnapshotUnavailableException("camera board is incomplete");
        }

        return new CameraBoard(board.CameraId, board.LatestEventUtc?.ToUniversalTime(), board.Entries.Select(ToModel).ToArray());
    }

    private static GlobalBoard ToModel(GlobalBoardDto board)
    {
        if (board.Entries is null)
        {
            throw new SnapshotUnavailableException("global board is incomplete");
        }

        return new GlobalBoard(board.LatestEventUtc?.ToUniversalTime(), board.Entries.Select(ToModel).ToArray());
    }

    private static CameraEntry ToModel(CameraEntryDto entry)
    {
        if (entry is null || entry.NumberPlate is null)
        {
            throw new SnapshotUnavailableException("camera entry is incomplete");
        }

        return new CameraEntry(entry.Rank, entry.NumberPlate, entry.SpeedKmh, entry.MeasuredAtUtc.ToUniversalTime());
    }

    private static GlobalEntry ToModel(GlobalEntryDto entry)
    {
        if (entry is null || entry.NumberPlate is null)
        {
            throw new SnapshotUnavailableException("global entry is incomplete");
        }

        return new GlobalEntry(entry.Rank, entry.NumberPlate, entry.AverageSpeedKmh, entry.MeasurementCount, entry.MeasuredAtUtc.ToUniversalTime());
    }
}
