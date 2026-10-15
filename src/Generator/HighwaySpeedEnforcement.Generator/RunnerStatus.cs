namespace HighwaySpeedEnforcement.Generator;

/// <summary>The numbers of the last completed tick, replaced atomically.</summary>
public sealed record RunnerStatusValues(long Tick, long MessagesSent, TimeSpan LastTickDuration, long? Seed);

/// <summary>
/// What the status bar needs from the runner (design section 11): written by the runner after every tick, read by anyone.
/// <see cref="Changed"/> fires after each update, on the runner's thread.
/// </summary>
public sealed class RunnerStatus
{
    private RunnerStatusValues _values = new(0, 0, TimeSpan.Zero, null);

    public event Action? Changed;

    public RunnerStatusValues Current => Volatile.Read(ref _values);

    /// <summary>The configured seed, or null for a random one (the state file run).</summary>
    public void SetSeed(long? seed)
    {
        Volatile.Write(ref _values, Current with { Seed = seed });
        Changed?.Invoke();
    }

    public void RecordTick(long tick, long messagesSent, TimeSpan lastTickDuration)
    {
        Volatile.Write(ref _values, Current with { Tick = tick, MessagesSent = messagesSent, LastTickDuration = lastTickDuration });
        Changed?.Invoke();
    }
}
