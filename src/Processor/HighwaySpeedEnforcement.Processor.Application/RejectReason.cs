namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>Why a message was rejected (BR-ING-07); the reason codes of <c>docs/contracts/telemetry-messages.md</c>.</summary>
public enum RejectReason
{
    /// <summary>Not a JSON object, a missing or mistyped property, NaN or infinity, or an oversized payload.</summary>
    Malformed,

    /// <summary><c>Type</c> is neither <c>Measurement</c> nor <c>Heartbeat</c>.</summary>
    UnknownType,

    InvalidPlate,

    InvalidCamera,

    InvalidSpeed,

    InvalidTimestamp,

    /// <summary>Valid payload whose camera is not greater than the vehicle's last camera (BR-ING-02).</summary>
    OutOfSequence,
}
