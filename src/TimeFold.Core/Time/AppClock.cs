namespace TimeFold.Core.Time;

/// <summary>
/// All time handling in TimeFold goes through here.
///
/// The rule, in every phase: save UTC plus your timezone offset.
/// Never save local time on its own.
///
/// Why: if you record "3 PM" and later the clock changes (daylight saving) or you
/// travel, you can no longer tell what "3 PM" meant. UTC plus the offset at that
/// moment always converts back correctly.
/// </summary>
public static class AppClock
{
    /// <summary>The current time, as UTC milliseconds since 1 January 1970.</summary>
    public static long NowUtcMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// How far your local time is from UTC right now, in minutes.
    /// Example: Pakistan (UTC+5) returns 300.
    /// </summary>
    public static int CurrentOffsetMinutes()
    {
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        return (int)offset.TotalMinutes;
    }

    /// <summary>
    /// Turn a stored UTC time back into the local time it was when it happened.
    /// </summary>
    public static DateTime ToLocalTime(long utcMs, int offsetMinutes)
    {
        DateTimeOffset utc = DateTimeOffset.FromUnixTimeMilliseconds(utcMs);
        DateTimeOffset local = utc.ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        return local.DateTime;
    }

    /// <summary>
    /// The UTC millisecond range covering one local day.
    /// Used by the "show me today" query.
    /// </summary>
    public static void GetLocalDayRangeUtcMs(DateTime localDay, out long fromUtcMs, out long toUtcMs)
    {
        DateTime startOfDay = localDay.Date;
        DateTime endOfDay = startOfDay.AddDays(1);

        fromUtcMs = new DateTimeOffset(startOfDay, TimeZoneInfo.Local.GetUtcOffset(startOfDay))
            .ToUnixTimeMilliseconds();

        toUtcMs = new DateTimeOffset(endOfDay, TimeZoneInfo.Local.GetUtcOffset(endOfDay))
            .ToUnixTimeMilliseconds();
    }
}
