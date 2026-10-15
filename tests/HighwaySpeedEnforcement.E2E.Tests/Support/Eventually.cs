namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>Polls a condition that an external process (broker, container) will make true; there is no event to await for it.</summary>
public static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static Task TrueAsync(Func<bool> condition, string what, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        return TrueAsync(() => Task.FromResult(condition()), what, cancellationToken, timeout);
    }

    public static async Task TrueAsync(Func<Task<bool>> condition, string what, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DefaultTimeout);

        while (!await condition())
        {
            if (deadline.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Timed out waiting until {what}.");
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }
}
