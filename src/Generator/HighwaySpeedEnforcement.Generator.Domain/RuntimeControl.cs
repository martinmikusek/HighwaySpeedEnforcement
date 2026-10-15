namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// The operator's live settings of design section 11: traffic paused, vehicles per tick, heartbeats and the cameras that are
/// off. Written by the keyboard handler, read by the runner once per tick through <see cref="Snapshot"/>. Thread-safe; every
/// operation returns the new value of what it changed.
/// </summary>
public sealed class RuntimeControl
{
    public const int MinVehiclesPerTick = 10;
    public const int MaxVehiclesPerTick = 2_000;

    private const int StepBoundary = 100;
    private const int SmallStep = 10;
    private const int LargeStep = 100;

    private readonly Lock _lock = new();
    private readonly bool[] _cameraDisabled = new bool[Simulation.CameraCount + 1];
    private bool _trafficPaused;
    private int _vehiclesPerTick;
    private bool _heartbeatsEnabled;

    /// <summary>Starts from the configured options; the initial <see cref="SimulationOptions.VehiclesPerTick"/> is taken as is, only changes are clamped.</summary>
    public RuntimeControl(SimulationOptions initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentOutOfRangeException.ThrowIfLessThan(initial.VehiclesPerTick, 1);

        _trafficPaused = initial.TrafficPaused;
        _vehiclesPerTick = initial.VehiclesPerTick;
        _heartbeatsEnabled = initial.HeartbeatsEnabled;

        foreach (int camera in initial.DisabledCameras)
        {
            ValidateCamera(camera);
            _cameraDisabled[camera] = true;
        }
    }

    public bool TogglePause()
    {
        lock (_lock)
        {
            _trafficPaused = !_trafficPaused;
            return _trafficPaused;
        }
    }

    public bool SetHeartbeats(bool enabled)
    {
        lock (_lock)
        {
            _heartbeatsEnabled = enabled;
            return _heartbeatsEnabled;
        }
    }

    public bool ToggleHeartbeats()
    {
        lock (_lock)
        {
            _heartbeatsEnabled = !_heartbeatsEnabled;
            return _heartbeatsEnabled;
        }
    }

    /// <summary>The next step up: by 10 up to 100, by 100 above it, capped at <see cref="MaxVehiclesPerTick"/>.</summary>
    public int IncreaseVehicles()
    {
        lock (_lock)
        {
            _vehiclesPerTick = NextUp(_vehiclesPerTick);
            return _vehiclesPerTick;
        }
    }

    /// <summary>The next step down: by 100 above 100, by 10 up to it, never below <see cref="MinVehiclesPerTick"/> (a smaller configured value is kept).</summary>
    public int DecreaseVehicles()
    {
        lock (_lock)
        {
            _vehiclesPerTick = NextDown(_vehiclesPerTick);
            return _vehiclesPerTick;
        }
    }

    /// <summary>10 steps below the baseline of 100 and 100 steps above it, snapped to the grid of the step (95 goes up to 100, 150 to 200).</summary>
    public static int NextUp(int value)
    {
        int next = value < StepBoundary ? ((value / SmallStep) + 1) * SmallStep : ((value / LargeStep) + 1) * LargeStep;

        return Math.Clamp(next, MinVehiclesPerTick, Math.Max(MaxVehiclesPerTick, value));
    }

    /// <summary>The counterpart of <see cref="NextUp"/>: 200 goes down to 100, 100 to 90, 95 to 90, 10 stays 10.</summary>
    public static int NextDown(int value)
    {
        if (value > MaxVehiclesPerTick)
        {
            return MaxVehiclesPerTick;
        }

        if (value <= MinVehiclesPerTick)
        {
            return value;
        }

        int next = value > StepBoundary ? ((value - 1) / LargeStep) * LargeStep : ((value - 1) / SmallStep) * SmallStep;

        return Math.Min(value, Math.Max(next, MinVehiclesPerTick));
    }

    /// <summary>Switches the camera off or on; returns true when the camera is now disabled.</summary>
    public bool ToggleCamera(int camera)
    {
        ValidateCamera(camera);

        lock (_lock)
        {
            _cameraDisabled[camera] = !_cameraDisabled[camera];
            return _cameraDisabled[camera];
        }
    }

    public RuntimeControlSnapshot Snapshot()
    {
        lock (_lock)
        {
            var disabled = new List<int>();

            for (int camera = 1; camera <= Simulation.CameraCount; camera++)
            {
                if (_cameraDisabled[camera])
                {
                    disabled.Add(camera);
                }
            }

            return new RuntimeControlSnapshot(_trafficPaused, _vehiclesPerTick, _heartbeatsEnabled, disabled.ToArray());
        }
    }

    private static void ValidateCamera(int camera)
    {
        if (camera is < 1 or > Simulation.CameraCount)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), camera, $"Camera must be within 1 to {Simulation.CameraCount}.");
        }
    }
}
