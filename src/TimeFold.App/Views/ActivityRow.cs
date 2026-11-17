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
    /// <summary>
    /// The record's id and program file, carried so that a right-click on a row
    /// can delete or block exactly the thing you are looking at. Neither is
    /// shown on screen.
    /// </summary>
    public string RecordId { get; set; }
    public string ExeFileName { get; set; }

    /// <summary>
    /// The hour this happened in, as a heading: "2 PM", or "17 November, 2 PM"
    /// when the list is showing more than one day. The list groups on this.
    /// </summary>
    public string HourGroup { get; set; }

    public string TimeRange { get; set; }
    public string Duration { get; set; }
    public string AppName { get; set; }
    public string WindowTitle { get; set; }

    public static ActivityRow FromRecord(ActivityRecord record)
    {
        return FromRecord(record, false);
    }

    /// <summary>
    /// <paramref name="includeTheDate"/> is true for search results, which can
    /// come from any day. Without it two results an hour apart but a month
    /// apart would sit under headings that look identical.
    /// </summary>
    public static ActivityRow FromRecord(ActivityRecord record, bool includeTheDate)
    {
        ActivityRow row = new ActivityRow();

        row.RecordId = record.Id;
        row.HourGroup = BuildHourGroup(record, includeTheDate);
        row.ExeFileName = string.IsNullOrEmpty(record.ExePath)
            ? null
            : System.IO.Path.GetFileName(record.ExePath);

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

    /// <summary>
    /// The heading a row sits under.
    ///
    /// Built from the record's own timezone offset, not from today's. A record
    /// made in Pakistan and read in London must still say the hour it was for
    /// you at the time - that is the whole reason the offset is stored.
    /// </summary>
    private static string BuildHourGroup(ActivityRecord record, bool includeTheDate)
    {
        DateTime local = AppClock.ToLocalTime(
            record.StartedAtUtcMs,
            record.TimeZoneOffsetMinutes);

        DateTime hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);

        if (includeTheDate)
        {
            return hour.ToString("ddd d MMM, h tt");
        }

        return hour.ToString("h tt");
    }
}
