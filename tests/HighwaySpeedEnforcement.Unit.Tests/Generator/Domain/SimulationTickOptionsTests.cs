using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

/// <summary>The per-tick options of design section 11: what the operator changes between ticks.</summary>
public sealed class SimulationTickOptionsTests
{
    private const ulong Seed = 20261014;
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private const long StartTimestamp = 638_000_000_000_000_000;

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public void Tick_with_unchanged_options_gives_the_same_output_as_the_constructor_options()
    {
        var options = new SimulationOptions { VehiclesPerTick = 50, HeartbeatsEnabled = true, DisabledCameras = [4], SpawnUntilTick = 8 };
        var plain = Create(options);
        var perTick = Create(options);

        for (int k = 1; k <= 14; k++)
        {
            var expected = plain.Tick(Timestamp(k));
            var actual = perTick.Tick(Timestamp(k), options);

            Assert.Equal(expected, actual with { Measurements = expected.Measurements, HeartbeatCameras = expected.HeartbeatCameras });
            Assert.Equal(expected.Measurements, actual.Measurements);
            Assert.Equal(expected.HeartbeatCameras, actual.HeartbeatCameras);
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-04")]
    public void Tick_paused_spawns_nothing_and_lets_the_road_drain_then_resumes()
    {
        var simulation = Create(new SimulationOptions());
        var running = new SimulationOptions();
        var paused = running with { TrafficPaused = true };

        for (int k = 1; k <= 3; k++)
        {
            Assert.Equal(100, simulation.Tick(Timestamp(k), running).Spawned);
        }

        for (int k = 4; k <= 15; k++)
        {
            var output = simulation.Tick(Timestamp(k), paused);
            int cohortsOnRoad = Math.Max(0, 3 - Math.Max(0, k - 10));

            Assert.Equal(0, output.Spawned);
            Assert.Equal(100 * cohortsOnRoad, output.Measurements.Count);
            Assert.DoesNotContain(output.Measurements, m => m.CameraId == 1);
        }

        Assert.Equal(0, simulation.Tick(Timestamp(16), paused).Active);

        var resumed = simulation.Tick(Timestamp(17), running);
        Assert.Equal(100, resumed.Spawned);
        Assert.Equal(100, resumed.Measurements.Count);
        Assert.All(resumed.Measurements, m => Assert.Equal(1, m.CameraId));
    }

    [Fact]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-TEL-03")]
    public void Tick_paused_with_heartbeats_lists_the_idle_cameras()
    {
        var simulation = Create(new SimulationOptions());
        simulation.Tick(Timestamp(1), new SimulationOptions());

        var paused = new SimulationOptions { TrafficPaused = true, HeartbeatsEnabled = true };
        var output = simulation.Tick(Timestamp(2), paused);

        Assert.Equal([2], output.Measurements.Select(m => m.CameraId).Distinct());
        Assert.Equal([1, 3, 4, 5, 6, 7, 8, 9, 10], output.HeartbeatCameras);
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public void Tick_plates_stay_unique_across_pause_and_resume()
    {
        var simulation = Create(new SimulationOptions());
        var plates = new HashSet<string>(StringComparer.Ordinal);
        var running = new SimulationOptions();
        var paused = running with { TrafficPaused = true };

        for (int k = 1; k <= 30; k++)
        {
            var output = simulation.Tick(Timestamp(k), k is >= 5 and <= 12 or >= 20 and <= 22 ? paused : running);

            foreach (var measurement in output.Measurements.Where(m => m.CameraId == 1))
            {
                Assert.True(plates.Add(measurement.NumberPlate), $"Plate {measurement.NumberPlate} repeats in tick {k} (seed {Seed}).");
            }
        }

        Assert.Equal(1_900, plates.Count);
        Assert.Equal(1_900, simulation.PlatesDrawn);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Tick_heartbeats_toggle_takes_effect_in_the_same_tick()
    {
        var simulation = Create(new SimulationOptions());
        var off = new SimulationOptions { SpawnUntilTick = 1 };
        var on = off with { HeartbeatsEnabled = true };

        Assert.Empty(simulation.Tick(Timestamp(1), off).HeartbeatCameras);
        Assert.Equal([1, 3, 4, 5, 6, 7, 8, 9, 10], simulation.Tick(Timestamp(2), on).HeartbeatCameras);
        Assert.Empty(simulation.Tick(Timestamp(3), off).HeartbeatCameras);
        Assert.Equal([1, 2, 3, 5, 6, 7, 8, 9, 10], simulation.Tick(Timestamp(4), on).HeartbeatCameras);
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public void Tick_vehicles_per_tick_change_applies_to_the_next_cohort_only()
    {
        var simulation = Create(new SimulationOptions());

        Assert.Equal(100, simulation.Tick(Timestamp(1), new SimulationOptions { VehiclesPerTick = 100 }).Spawned);
        var doubled = simulation.Tick(Timestamp(2), new SimulationOptions { VehiclesPerTick = 200 });
        Assert.Equal(200, doubled.Spawned);
        Assert.Equal(300, doubled.Measurements.Count);
        Assert.Equal(200, doubled.Measurements.Count(m => m.CameraId == 1));
        Assert.Equal(100, doubled.Measurements.Count(m => m.CameraId == 2));

        var halved = simulation.Tick(Timestamp(3), new SimulationOptions { VehiclesPerTick = 1 });
        Assert.Equal(1, halved.Spawned);
        Assert.Equal(301, halved.Measurements.Count);
        Assert.Equal(301, halved.Active);
    }

    [Fact]
    [Trait("AC", "AC-34")]
    [Trait("BR", "BR-SIM-04")]
    public void Tick_camera_toggled_mid_run_skips_only_that_camera_and_leaves_the_other_speeds_unchanged()
    {
        var withOutage = Create(new SimulationOptions());
        var reference = Create(new SimulationOptions());
        var all = new SimulationOptions();
        var without7 = new SimulationOptions { DisabledCameras = [7] };
        var outageMeasurements = new List<Measurement>();
        var referenceMeasurements = new List<Measurement>();

        for (int k = 1; k <= 20; k++)
        {
            bool cameraOff = k is >= 8 and <= 14;
            var output = withOutage.Tick(Timestamp(k), cameraOff ? without7 : all);
            outageMeasurements.AddRange(output.Measurements);
            referenceMeasurements.AddRange(reference.Tick(Timestamp(k)).Measurements);

            if (cameraOff)
            {
                Assert.DoesNotContain(output.Measurements, m => m.CameraId == 7);
            }
            else if (k >= 7)
            {
                Assert.Equal(100, output.Measurements.Count(m => m.CameraId == 7));
            }
        }

        var expected = referenceMeasurements.Where(m => !(m.CameraId == 7 && TickOf(m.TimestampTicks) is >= 8 and <= 14));
        Assert.Equal(expected, outageMeasurements);

        var camerasByPlate = outageMeasurements.GroupBy(m => m.NumberPlate).ToDictionary(g => g.Key, g => g.Select(m => m.CameraId).ToArray());
        var completed = camerasByPlate.Values.Where(cameras => cameras[^1] == 10).ToArray();
        Assert.Equal(1_100, completed.Length);
        Assert.Equal(700, completed.Count(cameras => cameras.Length == 9 && !cameras.Contains(7)));
        Assert.Equal(400, completed.Count(cameras => cameras.Length == 10));
    }

    [Fact]
    public void Tick_rejects_invalid_per_tick_options()
    {
        var simulation = Create(new SimulationOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Tick(Timestamp(1), new SimulationOptions { VehiclesPerTick = 0 }));
        Assert.Throws<ArgumentException>(() => simulation.Tick(Timestamp(1), new SimulationOptions { DisabledCameras = [0] }));
        Assert.Throws<ArgumentNullException>(() => simulation.Tick(Timestamp(1), null!));
    }

    private static Simulation Create(SimulationOptions options)
    {
        var random = new DeterministicRandom(Seed);
        var permutation = PlatePermutation.FromSeed(PlateFormat.Size, random);

        return new Simulation(options, permutation, random);
    }

    private static long Timestamp(int tick)
    {
        return StartTimestamp + (tick * TicksPerSecond);
    }

    private static long TickOf(long timestampTicks)
    {
        return (timestampTicks - StartTimestamp) / TicksPerSecond;
    }
}
