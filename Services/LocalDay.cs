namespace Nook.Api.Services;

/// <summary>
/// Helpers for interpreting a calendar date (YYYY-MM-DD) as the server's local
/// day, expressed as a UTC window [StartUtc, EndUtc). Bookings are stored in
/// UTC, but "today" in the UI means the local day.
/// </summary>
public static class LocalDay
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static (DateTime StartUtc, DateTime EndUtc) ToUtcWindow(DateOnly date)
    {
        var startLocal = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local);
        return (startLocal.ToUniversalTime(), startLocal.AddDays(1).ToUniversalTime());
    }
}
