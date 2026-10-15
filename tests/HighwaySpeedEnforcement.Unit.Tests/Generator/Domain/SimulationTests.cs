using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed class SimulationTests
{
    private const ulong Seed = 20261014;
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private const long StartTimestamp = 638_000_000_000_000_000;

    [Fact]
    [Trait("AC", "AC-02")]
    [Trait("BR", "BR-SIM-02")]
    [Trait("BR", "BR-SIM-04")]
    public void Tick_warm_up_and_steady_state_emit_the_expected_counts()
    {
        var simulation = Create(new SimulationOptions());

        for (int k = 1; k <= 15; k++)
        {
            var output = simulation.Tick(Timestamp(k));

            Assert.Equal(k, output.TickNumber);
            Assert.Equal(100 * Math.Min(k, 10), output.Measurements.Count);
            Assert.Equal(100, output.Spawned);
            Assert.Equal(k >= 10 ? 100 : 0, output.Removed);
            Assert.Equal(100 * Math.Min(k, 10) - output.Removed, output.Active);

            if (k >= 10)
            {
                Assert.Equal(1_000, output.Measurements.Count);
                var perCamera = output.Measurements.GroupBy(m => m.CameraId).ToDictionary(g => g.Key, g => g.Count());
                Assert.Equal(Enumerable.Range(1, 10), perCamera.Keys.Order());
                Assert.All(perCamera.Values, count => Assert.Equal(100, count));
            }
        }
    }

    [Fact]
    [Trait("AC", "AC-01")]
    [Trait("BR", "BR-SIM-04")]
    public void Tick_over_fifteen_ticks_every_vehicle_passes_cameras_one_to_ten_in_order()
    {
        var simulation = Create(new SimulationOptions());
        var camerasByPlate = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var measurement in Run(simulation, 15))
        {
            Assert.InRange(measurement.CameraId, 1, 10);
            camerasByPlate.GetOrAdd(measurement.NumberPlate).Add(measurement.CameraId);
        }

        Assert.Equal(1_500, camerasByPlate.Count);

        foreach (var (plate, cameras) in camerasByPlate)
        {
            int expectedCount = cameras.Count;
            Assert.Equal(Enumerable.Range(1, expectedCount), cameras);
        }

        Assert.Equal(600, camerasByPlate.Values.Count(cameras => cameras.Count == 10));
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public void Tick_over_fifty_ticks_never_repeats_a_plate()
    {
        var simulation = Create(new SimulationOptions());
        var plates = new HashSet<string>(StringComparer.Ordinal);

        for (int k = 1; k <= 50; k++)
        {
            var output = simulation.Tick(Timestamp(k));

            foreach (var measurement in output.Measurements.Where(m => m.CameraId == 1))
            {
                Assert.True(plates.Add(measurement.NumberPlate), $"Plate {measurement.NumberPlate} repeats in tick {k} (seed {Seed}).");
            }
        }

        Assert.Equal(5_000, plates.Count);
        Assert.Equal(5_000, simulation.PlatesDrawn);
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public void Tick_stamps_every_measurement_with_the_given_timestamp()
    {
        var simulation = Create(new SimulationOptions());

        for (int k = 1; k <= 12; k++)
        {
            var output = simulation.Tick(Timestamp(k));

            Assert.Equal(Timestamp(k), output.TimestampTicks);
            Assert.All(output.Measurements, measurement => Assert.Equal(Timestamp(k), measurement.TimestampTicks));
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void Tick_speeds_of_a_vehicle_follow_the_speed_model()
    {
        var simulation = Create(new SimulationOptions());
        var speedsByPlate = new Dictionary<string, List<float>>(StringComparer.Ordinal);

        foreach (var measurement in Run(simulation, 12))
        {
            speedsByPlate.GetOrAdd(measurement.NumberPlate).Add(measurement.CurrentSpeed);
        }

        foreach (var (plate, speeds) in speedsByPlate.Where(pair => pair.Value.Count == 10))
        {
            for (int i = 1; i < speeds.Count; i++)
            {
                Assert.True(Math.Abs(speeds[i] - speeds[i - 1]) < 20f, $"Plate {plate}: step {speeds[i - 1]} to {speeds[i]} is too large (seed {Seed}).");
            }

            Assert.All(speeds, speed => Assert.InRange(speed, 0f, 250f));
        }
    }

    [Fact]
    public void Tick_same_seed_gives_identical_output_and_a_different_seed_differs()
    {
        var first = Run(Create(new SimulationOptions()), 12).ToArray();
        var second = Run(Create(new SimulationOptions()), 12).ToArray();
        var other = Run(Create(new SimulationOptions(), Seed + 1), 12).ToArray();

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    [Trait("BR", "BR-SIM-02")]
    public void Tick_respects_vehicles_per_tick(int vehiclesPerTick)
    {
        var simulation = Create(new SimulationOptions { VehiclesPerTick = vehiclesPerTick });

        for (int k = 1; k <= 12; k++)
        {
            var output = simulation.Tick(Timestamp(k));

            Assert.Equal(vehiclesPerTick, output.Spawned);
            Assert.Equal(vehiclesPerTick * Math.Min(k, 10), output.Measurements.Count);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-TEL-03")]
    public void Tick_after_spawn_until_tick_drains_the_fleet_and_idle_cameras_send_heartbeats(bool heartbeatsEnabled)
    {
        var simulation = Create(new SimulationOptions { SpawnUntilTick = 3, HeartbeatsEnabled = heartbeatsEnabled });
        var spawnedPlates = new HashSet<string>(StringComparer.Ordinal);

        for (int k = 1; k <= 16; k++)
        {
            var output = simulation.Tick(Timestamp(k));
            int cohortsOnRoad = Math.Max(0, Math.Min(3, k) - Math.Max(0, k - 10));

            Assert.Equal(k <= 3 ? 100 : 0, output.Spawned);
            Assert.Equal(100 * cohortsOnRoad, output.Measurements.Count);
            Assert.Equal(k is >= 10 and <= 12 ? 100 : 0, output.Removed);
            Assert.Equal(100 * cohortsOnRoad - output.Removed, output.Active);

            foreach (var measurement in output.Measurements.Where(m => m.CameraId == 1))
            {
                Assert.True(spawnedPlates.Add(measurement.NumberPlate));
            }

            var busyCameras = output.Measurements.Select(m => m.CameraId).Distinct().Order().ToArray();
            int firstBusy = Math.Max(1, k - 2);
            int lastBusy = Math.Min(10, k);
            var expectedBusy = k <= 12 ? Enumerable.Range(firstBusy, lastBusy - firstBusy + 1).ToArray() : [];
            Assert.Equal(expectedBusy, busyCameras);

            var expectedHeartbeats = heartbeatsEnabled ? Enumerable.Range(1, 10).Except(expectedBusy).ToArray() : [];
            Assert.Equal(expectedHeartbeats, output.HeartbeatCameras);

            if (k > 12)
            {
                Assert.Empty(output.Measurements);
                Assert.Equal(0, output.Active);
            }
        }

        Assert.Equal(300, spawnedPlates.Count);
    }

    [Fact]
    [Trait("AC", "AC-34")]
    [Trait("BR", "BR-SIM-04")]
    public void Tick_with_a_disabled_camera_skips_its_measurements_and_leaves_the_others_unchanged()
    {
        var withOutage = Run(Create(new SimulationOptions { DisabledCameras = [7] }), 15).ToArray();
        var reference = Run(Create(new SimulationOptions()), 15).ToArray();

        Assert.DoesNotContain(withOutage, measurement => measurement.CameraId == 7);
        Assert.Equal(reference.Where(measurement => measurement.CameraId != 7), withOutage);

        var camerasByPlate = withOutage.GroupBy(m => m.NumberPlate).ToDictionary(g => g.Key, g => g.Select(m => m.CameraId).ToArray());
        var completed = camerasByPlate.Values.Where(cameras => cameras[^1] == 10).ToArray();
        Assert.Equal(600, completed.Length);
        Assert.All(completed, cameras => Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10 }, cameras));
    }

    [Fact]
    [Trait("AC", "AC-34")]
    public void Tick_with_a_disabled_camera_and_heartbeats_never_sends_its_heartbeat()
    {
        var simulation = Create(new SimulationOptions { DisabledCameras = [7], HeartbeatsEnabled = true, SpawnUntilTick = 1 });

        for (int k = 1; k <= 12; k++)
        {
            var output = simulation.Tick(Timestamp(k));

            Assert.DoesNotContain(7, output.HeartbeatCameras);
            Assert.Equal(Enumerable.Range(1, 10).Where(camera => camera != 7 && camera != k), output.HeartbeatCameras);
        }
    }

    [Fact]
    public void Tick_removed_and_active_counts_add_up_to_the_spawned_total()
    {
        var simulation = Create(new SimulationOptions { VehiclesPerTick = 7 });
        long spawned = 0;
        long removed = 0;

        for (int k = 1; k <= 40; k++)
        {
            var output = simulation.Tick(Timestamp(k));
            spawned += output.Spawned;
            removed += output.Removed;

            Assert.Equal(spawned - removed, output.Active);
        }

        Assert.Equal(280, spawned);
        Assert.Equal(217, removed);
    }

    [Fact]
    public void Constructor_rejects_invalid_options_and_a_foreign_permutation()
    {
        var random = new DeterministicRandom(Seed);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation(new SimulationOptions { VehiclesPerTick = 0 }, PlatePermutation.FromSeed(PlateFormat.Size, random), random));
        Assert.Throws<ArgumentException>(() => new Simulation(new SimulationOptions { DisabledCameras = [11] }, PlatePermutation.FromSeed(PlateFormat.Size, random), random));
        Assert.Throws<ArgumentException>(() => new Simulation(new SimulationOptions(), PlatePermutation.FromSeed(1_000, random), random));
    }

    private static Simulation Create(SimulationOptions options, ulong seed = Seed)
    {
        var random = new DeterministicRandom(seed);
        var permutation = PlatePermutation.FromSeed(PlateFormat.Size, random);

        return new Simulation(options, permutation, random);
    }

    private static IEnumerable<Measurement> Run(Simulation simulation, int ticks)
    {
        for (int k = 1; k <= ticks; k++)
        {
            foreach (var measurement in simulation.Tick(Timestamp(k)).Measurements)
            {
                yield return measurement;
            }
        }
    }

    private static long Timestamp(int tick)
    {
        return StartTimestamp + (tick * TicksPerSecond);
    }
}

internal static class DictionaryExtensions
{
    public static List<TValue> GetOrAdd<TKey, TValue>(this Dictionary<TKey, List<TValue>> dictionary, TKey key)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = [];
            dictionary[key] = list;
        }

        return list;
    }
}
