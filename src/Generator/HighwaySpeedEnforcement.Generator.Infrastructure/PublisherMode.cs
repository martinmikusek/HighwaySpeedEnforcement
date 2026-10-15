namespace HighwaySpeedEnforcement.Generator.Infrastructure;

/// <summary>How the MQTT publisher connects (design section 5).</summary>
public enum PublisherMode
{
    /// <summary>One MQTT client per enabled camera, each publishing its camera's messages in order.</summary>
    PerCamera,

    /// <summary>One MQTT client for all cameras, messages pipelined.</summary>
    SingleConnection,
}
