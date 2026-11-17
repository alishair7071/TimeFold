using TimeFold.Core.Models;
using TimeFold.Core.Time;

namespace TimeFold.App.Views;

/// <summary>
/// One line as it appears on screen.
///
/// This is separate from ActivityRecord on purpose. ActivityRecord is how data
/// is stored (UTC numbers, raw fields). ActivityRow is how it is displayed
/// (local times, readable durations). Keeping them apart means we can change
/// how things look without touching how things are saved.
/// </summary>
public class ActivityRow
{
    public string TimeRange { get; set; }
    public string Duration { get; set; }
    public string AppName { get; set; }
    public string WindowTitle { get; set; }

    public static ActivityRow FromRecord(ActivityRecord record)
    {
        ActivityRow row = new ActivityRow();

        DateTime startLocal = AppClock.ToLocalTime(
            record.StartedAtUtcMs,
            record.TimeZoneOffsetMinutes);

        if (record.EndedAtUtcMs == null)
        {
            row.TimeRange = startLocal.ToString("HH:mm:ss") + " - now";
        }
        else
        {
            DateTime endLocal = AppClock.ToLocalTime(
                record.EndedAtUtcMs.Value,
                record.TimeZoneOffsetMinutes);

            row.TimeRange = startLocal.ToString("HH:mm:ss") + " - " + endLocal.ToString("HH:mm:ss");
        }

        row.Duration = FormatDuration(record.Duration);
        row.AppName = string.IsNullOrEmpty(record.AppName) ? "(unknown)" : record.AppName;
        row.WindowTitle = record.WindowTitle;

        return row;
    }

    /// <summary>Turns a duration into something short and readable: 45s, 3m 20s, 1h 05m.</summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return (int)duration.TotalSeconds + "s";
        }

        if (duration.TotalMinutes < 60)
        {
            return duration.Minutes + "m " + duration.Seconds.ToString("00") + "s";
        }

        return (int)duration.TotalHours + "h " + duration.Minutes.ToString("00") + "m";
    }
}
