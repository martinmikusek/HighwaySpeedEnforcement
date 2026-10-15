namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>
/// Section <c>Client</c> of the configuration (design section 4). A plain object bound by the host; <see cref="Validate"/>
/// reports every violated rule before the window opens.
/// </summary>
public sealed class ClientSettings
{
    public const string SectionName = "Client";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan LiveLagThreshold { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Every violated rule as one message; empty when the settings are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{nameof(BaseUrl)} must be an absolute http or https URL (was '{BaseUrl}').");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(PollInterval)} must be positive (was {PollInterval}).");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(RequestTimeout)} must be positive (was {RequestTimeout}).");
        }

        if (LiveLagThreshold <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(LiveLagThreshold)} must be positive (was {LiveLagThreshold}).");
        }

        return errors;
    }
}
