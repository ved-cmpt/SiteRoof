namespace SiteRoof.Api.Common;

/// <summary>
/// "Today" on site. All SiteRoof projects are in the Edmonton area, so overdue checks use
/// Mountain time. The SQL views use the same zone (<c>AT TIME ZONE 'Mountain Standard Time'</c>).
/// </summary>
public sealed class SiteClock(TimeProvider time)
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public DateTimeOffset Now => time.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "America/Edmonton", "Mountain Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        }
        return TimeZoneInfo.Utc;
    }
}
