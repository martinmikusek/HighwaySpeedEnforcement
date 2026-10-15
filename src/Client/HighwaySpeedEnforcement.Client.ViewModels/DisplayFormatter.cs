using System.Globalization;

namespace HighwaySpeedEnforcement.Client.ViewModels;

/// <summary>
/// Presentation formatting in one place (design section 2): speeds to 0.1 km/h, event times as <c>HH:mm:ss.f</c> in the
/// given time zone, invariant culture. Rounding is presentation only (BR-LB-05).
/// </summary>
public sealed class DisplayFormatter
{
    private readonly TimeZoneInfo _timeZone;

    public DisplayFormatter(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public static DisplayFormatter Local { get; } = new(TimeZoneInfo.Local);

    public string Speed(double kmh)
    {
        return kmh.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public string Time(DateTimeOffset instant)
    {
        return TimeZoneInfo.ConvertTime(instant, _timeZone).ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
    }

    public string TimeOfDay(DateTimeOffset instant)
    {
        return TimeZoneInfo.ConvertTime(instant, _timeZone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public string Age(TimeSpan lag)
    {
        return lag.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
    }
}
