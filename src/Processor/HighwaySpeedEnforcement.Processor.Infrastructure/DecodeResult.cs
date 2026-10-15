using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>What <see cref="MessageDecoder"/> made of a payload.</summary>
public enum DecodeOutcome
{
    /// <summary><see cref="DecodeResult.Message"/> holds the decoded message.</summary>
    Decoded,

    /// <summary>Not a JSON object, a missing or mistyped property, or an oversized payload (<see cref="RejectReason.Malformed"/>).</summary>
    Malformed,

    /// <summary><c>Type</c> is neither <c>Measurement</c> nor <c>Heartbeat</c> (<see cref="RejectReason.UnknownType"/>).</summary>
    UnknownType,

    /// <summary>A heartbeat while heartbeats are disabled: counted as ignored, not an error (BR-TEL-03).</summary>
    Ignored,
}

/// <summary>Outcome of decoding one payload; <see cref="Message"/> is set exactly when <see cref="Outcome"/> is <see cref="DecodeOutcome.Decoded"/>.</summary>
public readonly record struct DecodeResult(DecodeOutcome Outcome, IncomingMessage? Message)
{
    public static DecodeResult Decoded(IncomingMessage message)
    {
        return new DecodeResult(DecodeOutcome.Decoded, message);
    }

    public static DecodeResult Malformed => new(DecodeOutcome.Malformed, null);

    public static DecodeResult UnknownType => new(DecodeOutcome.UnknownType, null);

    public static DecodeResult Ignored => new(DecodeOutcome.Ignored, null);

    /// <summary>The reject reason of an undecodable payload; throws for the other outcomes.</summary>
    public RejectReason RejectReason => Outcome switch
    {
        DecodeOutcome.Malformed => RejectReason.Malformed,
        DecodeOutcome.UnknownType => RejectReason.UnknownType,
        _ => throw new InvalidOperationException($"{Outcome} is not a rejection."),
    };
}
