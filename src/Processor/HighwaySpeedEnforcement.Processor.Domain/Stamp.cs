namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>The watermarks stamped on a message in arrival order: the global one and the one of the message's camera (BR-LB-11).</summary>
public readonly record struct Stamp(long GlobalTicks, long CameraTicks);

/// <summary>A valid measurement together with its <see cref="Stamp"/>; all time decisions use the stamp, never the clock.</summary>
public readonly record struct StampedMeasurement(Measurement Measurement, Stamp Stamp);
