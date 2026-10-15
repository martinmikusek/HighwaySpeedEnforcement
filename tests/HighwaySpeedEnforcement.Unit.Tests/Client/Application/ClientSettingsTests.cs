using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.Application;

public sealed class ClientSettingsTests
{
    [Fact]
    public void Defaults_match_the_design_and_are_valid()
    {
        var settings = new ClientSettings();

        Assert.Empty(settings.Validate());
        Assert.Equal("http://127.0.0.1:8080", settings.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.LiveLagThreshold);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("/api")]
    [InlineData("ftp://127.0.0.1:8080")]
    [InlineData("not a url")]
    public void Validate_rejects_a_base_url_that_is_not_absolute_http(string baseUrl)
    {
        var settings = new ClientSettings { BaseUrl = baseUrl };

        string error = Assert.Single(settings.Validate());
        Assert.StartsWith("BaseUrl", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://processor.example.com/")]
    public void Validate_accepts_absolute_http_and_https_base_urls(string baseUrl)
    {
        var settings = new ClientSettings { BaseUrl = baseUrl };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_poll_interval(int seconds)
    {
        var settings = new ClientSettings { PollInterval = TimeSpan.FromSeconds(seconds) };

        string error = Assert.Single(settings.Validate());
        Assert.StartsWith("PollInterval", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_request_timeout(int seconds)
    {
        var settings = new ClientSettings { RequestTimeout = TimeSpan.FromSeconds(seconds) };

        string error = Assert.Single(settings.Validate());
        Assert.StartsWith("RequestTimeout", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_live_lag_threshold(int seconds)
    {
        var settings = new ClientSettings { LiveLagThreshold = TimeSpan.FromSeconds(seconds) };

        string error = Assert.Single(settings.Validate());
        Assert.StartsWith("LiveLagThreshold", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_reports_several_errors_together()
    {
        var settings = new ClientSettings
        {
            BaseUrl = "nope",
            PollInterval = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(-5),
            LiveLagThreshold = TimeSpan.Zero,
        };

        var errors = settings.Validate();

        Assert.Equal(4, errors.Count);
        Assert.All(
            new[] { "BaseUrl", "PollInterval", "RequestTimeout", "LiveLagThreshold" },
            name => Assert.Contains(errors, error => error.StartsWith(name, StringComparison.Ordinal)));
    }
}
