namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>The persisted state of a <see cref="PlatePermutation"/>: the affine coefficients and the next index to draw (BR-SIM-03).</summary>
public readonly record struct PlateState(long A, long B, long NextIndex);
