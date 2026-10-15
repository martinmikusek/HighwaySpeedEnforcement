using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Generator;

/// <summary>
/// The permutation start-up of design section 4: with a configured seed the run is reproducible and the state store is not
/// used; without one, a stored state is resumed when present, otherwise a new permutation is created and saved (BR-SIM-03).
/// </summary>
public sealed record SimulationStartup(Simulation Simulation, long Seed, bool SeedConfigured, bool StoreInUse, bool StateResumed)
{
    /// <param name="randomSeed">The seed to use when the settings carry none; drawn by the caller so that this method stays deterministic.</param>
    public static SimulationStartup Create(GeneratorSettings settings, IPlateStateStore store, long randomSeed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        bool seedConfigured = settings.Seed is not null;
        long seed = settings.Seed ?? randomSeed;
        var random = new DeterministicRandom(unchecked((ulong)seed));
        var options = settings.ToSimulationOptions();

        if (seedConfigured)
        {
            var seeded = PlatePermutation.FromSeed(PlateFormat.Size, random);

            return new SimulationStartup(new Simulation(options, seeded, random), seed, SeedConfigured: true, StoreInUse: false, StateResumed: false);
        }

        var stored = store.Load();
        PlatePermutation permutation;

        if (stored is { } state)
        {
            permutation = new PlatePermutation(PlateFormat.Size, state);
        }
        else
        {
            permutation = PlatePermutation.FromSeed(PlateFormat.Size, random);
            store.Save(permutation.State);
        }

        return new SimulationStartup(new Simulation(options, permutation, random), seed, SeedConfigured: false, StoreInUse: true, StateResumed: stored is not null);
    }
}
