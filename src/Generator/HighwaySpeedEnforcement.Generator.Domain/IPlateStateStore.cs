namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>Port for the persisted plate permutation state, so that plates stay unique across restarts (BR-SIM-03).</summary>
public interface IPlateStateStore
{
    /// <summary>The stored state, or null when there is none or it cannot be used.</summary>
    PlateState? Load();

    void Save(PlateState state);
}
