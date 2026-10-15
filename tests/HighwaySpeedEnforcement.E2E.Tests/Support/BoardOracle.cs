namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>One valid measurement as a scenario publishes it.</summary>
public sealed record Telemetry(string Plate, int Camera, long Ticks, float Speed);

/// <summary>An expected camera board row.</summary>
public sealed record ExpectedCameraEntry(int Rank, string Plate, float Speed, long Ticks);

/// <summary>An expected global board row.</summary>
public sealed record ExpectedGlobalEntry(int Rank, string Plate, double Average, int Count, long Ticks);

/// <summary>
/// An independent, deliberately naive model of the business rules: it keeps every entry in plain lists and recomputes
/// the live top 10 on demand. Watermarks are taken in arrival order from valid measurements (BR-LB-11); the sequence rule
/// (BR-ING-02), the orphan timeout (BR-ING-06), the 3-measurement gate (BR-LB-03), completion (BR-ING-05, BR-LB-04) and
/// the expiry arithmetic (BR-LB-07, BR-LB-08) are written out literally, not shared with the processor.
/// </summary>
public sealed class BoardOracle
{
    private readonly ProcessorRun _settings;
    private readonly long[] _cameraWatermark = new long[11];
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly List<CameraEntry> _cameraEntries = [];
    private readonly Dictionary<string, GlobalEntry> _globalEntries = new(StringComparer.Ordinal);
    private long _globalWatermark;

    public BoardOracle(ProcessorRun settings)
    {
        _settings = settings;
    }

    public int Rejected { get; private set; }

    public long? CameraWatermark(int camera)
    {
        return _cameraWatermark[camera] > 0 ? _cameraWatermark[camera] : null;
    }

    public long? GlobalWatermark => _globalWatermark > 0 ? _globalWatermark : null;

    public void Apply(IEnumerable<Telemetry> measurements)
    {
        foreach (var measurement in measurements)
        {
            Apply(measurement);
        }
    }

    public void Apply(Telemetry m)
    {
        _cameraWatermark[m.Camera] = Math.Max(_cameraWatermark[m.Camera], m.Ticks);
        _globalWatermark = Math.Max(_globalWatermark, m.Ticks);

        if (_tracks.TryGetValue(m.Plate, out var track) && _globalWatermark - track.LastTicks >= _settings.InactivityTimeout.Ticks)
        {
            _tracks.Remove(m.Plate);
            track = null;
        }

        if (track is not null && m.Camera <= track.LastCamera)
        {
            Rejected++;
            return;
        }

        track ??= _tracks[m.Plate] = new Track();
        track.Count++;
        track.Sum += m.Speed;
        track.LastCamera = m.Camera;
        track.LastTicks = Math.Max(track.LastTicks, m.Ticks);

        _cameraEntries.Add(new CameraEntry(m.Plate, m.Camera, m.Speed, m.Ticks, Plus(m.Ticks, _settings.CameraBoardWindow)));

        var completed = m.Camera == 10;

        if (completed)
        {
            _tracks.Remove(m.Plate);
        }

        if (track.Count >= 3)
        {
            var lifetime = completed ? _settings.GlobalBoardLinger : _settings.InactivityTimeout;
            _globalEntries[m.Plate] = new GlobalEntry(m.Plate, track.Sum / track.Count, track.Count, track.LastTicks, Plus(track.LastTicks, lifetime));
        }
    }

    public IReadOnlyList<ExpectedCameraEntry> CameraTop(int camera)
    {
        return _cameraEntries
            .Where(entry => entry.Camera == camera && entry.ExpiresAt > _cameraWatermark[camera])
            .OrderByDescending(entry => entry.Speed)
            .ThenBy(entry => entry.Plate, StringComparer.Ordinal)
            .Take(10)
            .Select((entry, index) => new ExpectedCameraEntry(index + 1, entry.Plate, entry.Speed, entry.Ticks))
            .ToArray();
    }

    public IReadOnlyList<ExpectedGlobalEntry> GlobalTop()
    {
        return _globalEntries.Values
            .Where(entry => entry.ExpiresAt > _globalWatermark)
            .OrderByDescending(entry => entry.Average)
            .ThenBy(entry => entry.Plate, StringComparer.Ordinal)
            .Take(10)
            .Select((entry, index) => new ExpectedGlobalEntry(index + 1, entry.Plate, entry.Average, entry.Count, entry.Ticks))
            .ToArray();
    }

    private static long Plus(long ticks, TimeSpan lifetime)
    {
        if (lifetime == Timeout.InfiniteTimeSpan)
        {
            return long.MaxValue;
        }

        return ticks > long.MaxValue - lifetime.Ticks ? long.MaxValue : ticks + lifetime.Ticks;
    }

    private sealed class Track
    {
        public int Count { get; set; }

        public double Sum { get; set; }

        public int LastCamera { get; set; }

        public long LastTicks { get; set; }
    }

    private sealed record CameraEntry(string Plate, int Camera, float Speed, long Ticks, long ExpiresAt);

    private sealed record GlobalEntry(string Plate, double Average, int Count, long Ticks, long ExpiresAt);
}
