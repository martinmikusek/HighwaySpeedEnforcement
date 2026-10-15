using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed class SpeedModelTests
{
    private const ulong Seed = 20261014;
    private const int CamerasPerVehicle = 10;

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void Next_results_are_multiples_of_the_resolution_and_never_negative()
    {
        var model = new SpeedModel(new SpeedOptions());
        var random = new DeterministicRandom(Seed);

        for (int i = 0; i < 10_000; i++)
        {
            double desired = model.DrawDesired(random);
            float speed = model.Next((float)desired, desired, random);

            Assert.True(speed >= 0f, $"Negative speed {speed} (seed {Seed}).");
            Assert.True(Math.Abs((speed * 10.0) - Math.Round(speed * 10.0)) < 1e-3, $"Speed {speed} is not a multiple of 0.1 (seed {Seed}).");
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void Next_with_a_huge_negative_step_is_clamped_to_zero()
    {
        var model = new SpeedModel(new SpeedOptions { StepStdDev = 0 });

        Assert.Equal(0f, model.Next(0f, 0.0, new DeterministicRandom(Seed)));
        Assert.Equal(0f, model.Next(10f, -1_000.0, new DeterministicRandom(Seed)));
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void DrawDesired_stays_within_the_clamp_and_has_the_configured_mean_and_spread()
    {
        const int Draws = 10_000;
        var options = new SpeedOptions();
        var model = new SpeedModel(options);
        var random = new DeterministicRandom(Seed);
        double sum = 0;
        double sumOfSquares = 0;

        for (int i = 0; i < Draws; i++)
        {
            double desired = model.DrawDesired(random);

            Assert.InRange(desired, SpeedOptions.MinDesiredKmh, SpeedOptions.MaxDesiredKmh);
            sum += desired;
            sumOfSquares += desired * desired;
        }

        double mean = sum / Draws;
        double stdDev = Math.Sqrt((sumOfSquares / Draws) - (mean * mean));

        Assert.True(Math.Abs(mean - (options.BaselineKmh - 5)) <= 1.0, $"Mean {mean} is not within ±1 of {options.BaselineKmh - 5} (seed {Seed}).");
        Assert.True(Math.Abs(stdDev - 15.0) <= 1.5, $"Standard deviation {stdDev} is not within ±1.5 of 15 (seed {Seed}).");
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void Next_without_noise_is_exactly_the_pull()
    {
        var model = new SpeedModel(new SpeedOptions { StepStdDev = 0 });

        Assert.Equal(112.0f, model.Next(100f, 140.0, new DeterministicRandom(Seed)));
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    public void Next_over_many_vehicles_changes_speed_in_small_steps_around_the_desired_speed()
    {
        const int Vehicles = 10_000;
        var model = new SpeedModel(new SpeedOptions());
        var random = new DeterministicRandom(Seed);
        double differenceSum = 0;
        double differenceSumOfSquares = 0;
        long differenceCount = 0;
        double largestDifference = 0;
        double largestStray = 0;

        for (int vehicle = 0; vehicle < Vehicles; vehicle++)
        {
            double desired = model.DrawDesired(random);
            float previous = (float)desired;

            for (int camera = 1; camera <= CamerasPerVehicle; camera++)
            {
                float speed = model.Next(previous, desired, random);

                Assert.True(speed >= 0f, $"Negative speed {speed} (seed {Seed}).");
                double difference = speed - previous;
                differenceSum += difference;
                differenceSumOfSquares += difference * difference;
                differenceCount++;
                largestDifference = Math.Max(largestDifference, Math.Abs(difference));
                largestStray = Math.Max(largestStray, Math.Abs(speed - desired));
                previous = speed;
            }
        }

        double mean = differenceSum / differenceCount;
        double stdDev = Math.Sqrt((differenceSumOfSquares / differenceCount) - (mean * mean));

        Assert.True(stdDev is >= 2.5 and <= 4.5, $"Standard deviation of consecutive differences {stdDev} is outside [2.5, 4.5] (seed {Seed}).");
        Assert.True(largestDifference < 20.0, $"Largest consecutive difference {largestDifference} is not below 20 (seed {Seed}).");
        Assert.True(largestStray <= 25.0, $"A speed strayed {largestStray} km/h from its desired speed, more than 25 (seed {Seed}).");
    }

    [Fact]
    [Trait("BR", "BR-SIM-05")]
    [Trait("BR", "BR-LB-05")]
    public void Next_first_camera_speeds_of_many_vehicles_contain_ties()
    {
        var model = new SpeedModel(new SpeedOptions());
        var random = new DeterministicRandom(Seed);
        var speeds = new List<float>();

        for (int vehicle = 0; vehicle < 1_000; vehicle++)
        {
            double desired = model.DrawDesired(random);
            speeds.Add(model.Next((float)desired, desired, random));
        }

        Assert.True(speeds.Distinct().Count() < speeds.Count, $"No equal pair among 1,000 first-camera speeds (seed {Seed}).");
    }
}
