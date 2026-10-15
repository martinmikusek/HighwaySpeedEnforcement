namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>No valid snapshot could be obtained (network error, non-success status, undecodable body, timeout); the message is short enough for the status bar.</summary>
public sealed class SnapshotUnavailableException : Exception
{
    public SnapshotUnavailableException()
    {
    }

    public SnapshotUnavailableException(string message)
        : base(message)
    {
    }

    public SnapshotUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
