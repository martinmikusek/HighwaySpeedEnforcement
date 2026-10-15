using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed class RuntimeControlTests
{
    [Fact]
    public void Snapshot_starts_from_the_initial_options()
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = 250, HeartbeatsEnabled = true, DisabledCameras = [7, 3] });

        var snapshot = control.Snapshot();

        Assert.Equal(new RuntimeControlSnapshot(TrafficPaused: false, VehiclesPerTick: 250, HeartbeatsEnabled: true, DisabledCameras: [3, 7]), snapshot);
    }

    [Fact]
    public void TogglePause_flips_the_paused_flag_each_time()
    {
        var control = new RuntimeControl(new SimulationOptions());

        Assert.True(control.TogglePause());
        Assert.True(control.Snapshot().TrafficPaused);
        Assert.False(control.TogglePause());
        Assert.False(control.Snapshot().TrafficPaused);
    }

    [Fact]
    public void Heartbeats_can_be_set_and_toggled()
    {
        var control = new RuntimeControl(new SimulationOptions());

        Assert.True(control.SetHeartbeats(true));
        Assert.True(control.Snapshot().HeartbeatsEnabled);
        Assert.False(control.ToggleHeartbeats());
        Assert.False(control.Snapshot().HeartbeatsEnabled);
        Assert.True(control.ToggleHeartbeats());
        Assert.False(control.SetHeartbeats(false));
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(50, 60)]
    [InlineData(90, 100)]
    [InlineData(95, 100)]
    [InlineData(100, 200)]
    [InlineData(150, 200)]
    [InlineData(500, 600)]
    [InlineData(1_900, 2_000)]
    [InlineData(2_000, 2_000)]
    [InlineData(5, 10)]
    [InlineData(5_000, 5_000)]
    public void IncreaseVehicles_steps_by_10_up_to_100_then_by_100_and_stops_at_the_maximum(int start, int expected)
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = start });

        Assert.Equal(expected, control.IncreaseVehicles());
        Assert.Equal(expected, control.Snapshot().VehiclesPerTick);
    }

    [Theory]
    [InlineData(2_000, 1_900)]
    [InlineData(500, 400)]
    [InlineData(200, 100)]
    [InlineData(150, 100)]
    [InlineData(101, 100)]
    [InlineData(100, 90)]
    [InlineData(95, 90)]
    [InlineData(20, 10)]
    [InlineData(10, 10)]
    [InlineData(5, 5)]
    [InlineData(1, 1)]
    [InlineData(5_000, 2_000)]
    public void DecreaseVehicles_steps_by_100_above_100_then_by_10_and_stops_at_the_minimum(int start, int expected)
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = start });

        Assert.Equal(expected, control.DecreaseVehicles());
        Assert.Equal(expected, control.Snapshot().VehiclesPerTick);
    }

    [Fact]
    public void The_steps_walk_up_and_down_the_same_grid()
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = 80 });

        Assert.Equal([90, 100, 200, 300], new[] { control.IncreaseVehicles(), control.IncreaseVehicles(), control.IncreaseVehicles(), control.IncreaseVehicles() });
        Assert.Equal([200, 100, 90, 80], new[] { control.DecreaseVehicles(), control.DecreaseVehicles(), control.DecreaseVehicles(), control.DecreaseVehicles() });
    }

    [Fact]
    public void ToggleCamera_switches_a_camera_off_and_on_and_the_snapshot_lists_them_sorted()
    {
        var control = new RuntimeControl(new SimulationOptions());

        Assert.True(control.ToggleCamera(7));
        Assert.True(control.ToggleCamera(3));
        Assert.True(control.ToggleCamera(10));
        Assert.Equal([3, 7, 10], control.Snapshot().DisabledCameras);

        Assert.False(control.ToggleCamera(7));
        Assert.Equal([3, 10], control.Snapshot().DisabledCameras);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public void ToggleCamera_rejects_a_camera_outside_the_range(int camera)
    {
        var control = new RuntimeControl(new SimulationOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => control.ToggleCamera(camera));
        Assert.Empty(control.Snapshot().DisabledCameras);
    }

    [Fact]
    public void Constructor_rejects_invalid_initial_options()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeControl(new SimulationOptions { VehiclesPerTick = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeControl(new SimulationOptions { DisabledCameras = [11] }));
        Assert.Throws<ArgumentNullException>(() => new RuntimeControl(null!));
    }

    [Fact]
    public void Snapshot_is_detached_from_later_changes()
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = 100 });

        var before = control.Snapshot();
        control.TogglePause();
        control.IncreaseVehicles();
        control.ToggleHeartbeats();
        control.ToggleCamera(4);

        Assert.Equal(new RuntimeControlSnapshot(false, 100, false, []), before);
        Assert.Equal(new RuntimeControlSnapshot(true, 200, true, [4]), control.Snapshot());
        Assert.NotEqual(before, control.Snapshot());
    }

    [Fact]
    public void Snapshot_applies_itself_on_top_of_a_baseline_without_touching_the_fixed_options()
    {
        var baseline = new SimulationOptions { VehiclesPerTick = 100, SpawnUntilTick = 30, Speed = new SpeedOptions { BaselineKmh = 110 } };
        var snapshot = new RuntimeControlSnapshot(TrafficPaused: true, VehiclesPerTick: 400, HeartbeatsEnabled: true, DisabledCameras: [2]);

        var options = snapshot.ToSimulationOptions(baseline);

        Assert.True(options.TrafficPaused);
        Assert.Equal(400, options.VehiclesPerTick);
        Assert.True(options.HeartbeatsEnabled);
        Assert.Equal([2], options.DisabledCameras);
        Assert.Equal(30, options.SpawnUntilTick);
        Assert.Equal(110.0, options.Speed.BaselineKmh);
    }

    [Fact]
    public async Task Concurrent_operations_leave_a_consistent_final_state()
    {
        var control = new RuntimeControl(new SimulationOptions { VehiclesPerTick = 1 });
        const int threads = 8;
        const int togglesPerThread = 1_001;

        // Odd toggles per thread, an even number of threads and two threads per camera: every flag ends where it started.
        var tasks = Enumerable.Range(0, threads).Select(thread => Task.Run(() =>
        {
            for (int i = 0; i < togglesPerThread; i++)
            {
                control.TogglePause();
                control.ToggleHeartbeats();
                control.ToggleCamera(1 + (thread / 2));
                control.IncreaseVehicles();
            }
        }, TestContext.Current.CancellationToken));

        await Task.WhenAll(tasks);

        var snapshot = control.Snapshot();
        Assert.False(snapshot.TrafficPaused);
        Assert.False(snapshot.HeartbeatsEnabled);
        Assert.Empty(snapshot.DisabledCameras);
        Assert.Equal(RuntimeControl.MaxVehiclesPerTick, snapshot.VehiclesPerTick);
    }
}
