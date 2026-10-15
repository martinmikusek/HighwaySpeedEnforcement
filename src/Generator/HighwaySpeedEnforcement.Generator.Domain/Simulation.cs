namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// The state machine of design section 2 (BR-SIM-01, BR-SIM-02, BR-SIM-04, AMB-03): per tick, spawn, advance every vehicle by
/// one camera and emit, then remove the vehicles that emitted camera 10. Deterministic for a given permutation and random source.
/// The options of a tick may change between ticks (design section 11); the speed model is fixed at construction.
/// </summary>
public sealed class Simulation
{
    public const int CameraCount = 10;

    private readonly SimulationOptions _options;
    private readonly PlatePermutation _permutation;
    private readonly DeterministicRandom _random;
    private readonly SpeedModel _speedModel;
    private readonly bool[] _cameraDisabled = new bool[CameraCount + 1];

    // Vehicles spawned in the same tick share a camera; the newest cohort is first, so iteration is by camera and then by age.
    private readonly LinkedList<List<Vehicle>> _cohorts = new();

    private long _tickNumber;
    private int _activeCount;

    public Simulation(SimulationOptions options, PlatePermutation permutation, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(permutation);
        ArgumentNullException.ThrowIfNull(random);
        ValidateTickOptions(options);

        if (permutation.Size != PlateFormat.Size)
        {
            throw new ArgumentException($"The permutation must cover the plate space ({PlateFormat.Size}), was {permutation.Size}.", nameof(permutation));
        }

        _options = options;
        _permutation = permutation;
        _random = random;
        _speedModel = new SpeedModel(options.Speed);
    }

    public long PlatesDrawn { get; private set; }

    public PlatePermutation Permutation => _permutation;

    /// <summary>One tick with the options given at construction.</summary>
    public TickOutput Tick(long timestampTicks)
    {
        return Tick(timestampTicks, _options);
    }

    /// <summary>
    /// One tick with the given options: vehicles per tick, pause, disabled cameras and heartbeats apply to this tick only.
    /// <see cref="SimulationOptions.Speed"/> of <paramref name="options"/> is ignored (the speed model is fixed at construction).
    /// </summary>
    public TickOutput Tick(long timestampTicks, SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateTickOptions(options);
        ApplyDisabledCameras(options);

        _tickNumber++;

        int spawned = Spawn(options);
        var emitted = new bool[CameraCount + 1];
        var measurements = new List<Measurement>(_activeCount);

        foreach (var cohort in _cohorts)
        {
            Advance(cohort, timestampTicks, measurements, emitted);
        }

        int removed = RemoveFinished();
        var heartbeats = new List<int>();

        if (options.HeartbeatsEnabled)
        {
            for (int camera = 1; camera <= CameraCount; camera++)
            {
                if (!emitted[camera] && !_cameraDisabled[camera])
                {
                    heartbeats.Add(camera);
                }
            }
        }

        return new TickOutput(_tickNumber, timestampTicks, measurements, heartbeats, spawned, removed, _activeCount);
    }

    private static void ValidateTickOptions(SimulationOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.VehiclesPerTick, 1, nameof(options));

        foreach (int camera in options.DisabledCameras)
        {
            if (camera is < 1 or > CameraCount)
            {
                throw new ArgumentException($"Disabled camera {camera} is outside 1 to {CameraCount}.", nameof(options));
            }
        }
    }

    private void ApplyDisabledCameras(SimulationOptions options)
    {
        Array.Clear(_cameraDisabled);

        foreach (int camera in options.DisabledCameras)
        {
            _cameraDisabled[camera] = true;
        }
    }

    private int Spawn(SimulationOptions options)
    {
        if (options.TrafficPaused || (options.SpawnUntilTick is { } last && _tickNumber > last))
        {
            return 0;
        }

        var cohort = new List<Vehicle>(options.VehiclesPerTick);

        for (int i = 0; i < options.VehiclesPerTick; i++)
        {
            string plate = PlateFormat.Decode(_permutation.Next());
            double desired = _speedModel.DrawDesired(_random);
            cohort.Add(new Vehicle(plate, desired));
        }

        PlatesDrawn += cohort.Count;
        _activeCount += cohort.Count;
        _cohorts.AddFirst(cohort);

        return cohort.Count;
    }

    private void Advance(List<Vehicle> cohort, long timestampTicks, List<Measurement> measurements, bool[] emitted)
    {
        foreach (var vehicle in cohort)
        {
            vehicle.Camera++;
            // The speed is drawn even for a disabled camera, so disabling one does not change the other cameras' speeds.
            vehicle.Speed = _speedModel.Next(vehicle.Speed, vehicle.DesiredSpeed, _random);

            if (!_cameraDisabled[vehicle.Camera])
            {
                measurements.Add(new Measurement(vehicle.NumberPlate, timestampTicks, vehicle.Speed, vehicle.Camera));
                emitted[vehicle.Camera] = true;
            }
        }
    }

    private int RemoveFinished()
    {
        var oldest = _cohorts.Last;

        if (oldest is null || oldest.Value[0].Camera < CameraCount)
        {
            return 0;
        }

        _cohorts.RemoveLast();
        _activeCount -= oldest.Value.Count;

        return oldest.Value.Count;
    }

    private sealed class Vehicle
    {
        public Vehicle(string numberPlate, double desiredSpeed)
        {
            NumberPlate = numberPlate;
            DesiredSpeed = desiredSpeed;
            Speed = (float)desiredSpeed;
        }

        public string NumberPlate { get; }

        public double DesiredSpeed { get; }

        public int Camera { get; set; }

        public float Speed { get; set; }
    }
}
