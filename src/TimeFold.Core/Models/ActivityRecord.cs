namespace TimeFold.Core.Models;

/// <summary>
/// One thing that happened on the computer.
///
/// In Phase 1 this is always "you used this app, with this window title,
/// from this time to this time". Later phases add websites, screenshots
/// and more, using this same shape.
///
/// Read COMPONENTS.md section "How watchers report" for why these fields exist.
/// </summary>
public class ActivityRecord
{
    /// <summary>
    /// A code built from the data itself, not a random number.
    /// If the same thing is seen twice we get the same Id, so we save one row
    /// instead of two. See Recording/RecordId.cs.
    /// </summary>
    public string Id { get; set; }

    /// <summary>What kind of thing this is. See RecordKinds.</summary>
    public string Kind { get; set; }

    /// <summary>Which watcher reported it, for example "window-watcher".</summary>
    public string Source { get; set; }

    /// <summary>When it started, in UTC milliseconds. ALWAYS UTC.</summary>
    public long StartedAtUtcMs { get; set; }

    /// <summary>When it ended, in UTC milliseconds. Null while it is still going on.</summary>
    public long? EndedAtUtcMs { get; set; }

    /// <summary>
    /// Your timezone offset in minutes at the moment this happened.
    /// We save this because UTC alone is not enough: without it, times recorded
    /// before a daylight-saving change or in another country display wrongly.
    /// </summary>
    public int TimeZoneOffsetMinutes { get; set; }

    /// <summary>Friendly application name, for example "Google Chrome".</summary>
    public string AppName { get; set; }

    /// <summary>Full path of the program file, for example "C:\...\chrome.exe".</summary>
    public string ExePath { get; set; }

    /// <summary>The window title at the time, for example "TimeFold - Visual Studio Code".</summary>
    public string WindowTitle { get; set; }

    /// <summary>
    /// Anything extra, stored as JSON text.
    /// Later phases put website URLs, screenshot ids and so on here, so we do not
    /// have to change the database table every time we add a small field.
    /// </summary>
    public string DetailsJson { get; set; }

    /// <summary>
    /// When we actually wrote this to the database.
    /// Different from StartedAtUtcMs when we import older data (Phase 4 browser history).
    /// </summary>
    public long SavedAtUtcMs { get; set; }

    /// <summary>Last time this row was changed. Used by Phase 9 backup.</summary>
    public long UpdatedAtUtcMs { get; set; }

    /// <summary>Goes up by one on every change. Used by Phase 9 backup.</summary>
    public long RowVersion { get; set; }

    /// <summary>How long this lasted. Zero if it has not ended yet.</summary>
    public TimeSpan Duration
    {
        get
        {
            if (EndedAtUtcMs == null)
            {
                return TimeSpan.Zero;
            }

            long milliseconds = EndedAtUtcMs.Value - StartedAtUtcMs;
            if (milliseconds < 0)
            {
                milliseconds = 0;
            }

            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }
}
