using TimeFold.Core.Logging;
using TimeFold.Core.Models;
using TimeFold.Core.Recording;
using TimeFold.Core.Time;
using TimeFold.Core.Watchers.Native;

namespace TimeFold.Core.Watchers;

/// <summary>
/// Watches which window you are working in, and turns that into records.
///
/// How it works:
///   Every second it asks Windows what the active window is.
///   If it is the same as last second, the current record just gets longer.
///   If it changed, the current record is finished and a new one begins.
///
/// So you get ONE record per thing you actually did, not one per second.
///
/// A watcher never saves anything and never decides what is private.
/// It only reports what it saw. RecordingService does the rest.
/// </summary>
public class WindowWatcher
{
    /// <summary>
    /// How often we ask Windows what the active window is.
    ///
    /// Decided deliberately to POLL rather than use the Windows event hook
    /// SetWinEventHook. A hook is woken only when something changes, which uses
    /// slightly less power - but if the hook is ever dropped by Windows, it
    /// fails silently. You would get a gap in your history with no error
    /// anywhere. A timer that is still ticking is a timer we can see.
    /// One cheap call per second is a price worth paying for that.
    /// </summary>
    private const int CheckEverySeconds = 1;

    /// <summary>
    /// Records shorter than this are thrown away instead of being saved.
    ///
    /// Alt-Tab, the task switcher and a click passing over a window all create
    /// a "record" lasting well under a second. They are noise: you were never
    /// really working in that window, and they make the timeline unreadable.
    /// Two seconds is long enough to mean you actually looked at something.
    /// </summary>
    private const int MinimumRecordSeconds = 2;

    /// <summary>
    /// How often an unfinished record is written to the database anyway.
    /// If the computer loses power, we lose at most this much of the current record.
    /// </summary>
    private const int SaveUnfinishedEverySeconds = 30;

    private readonly ForegroundWindowReader _reader;
    private readonly IAppLogger _logger;
    private readonly string _deviceId;

    private System.Threading.Timer _timer;

    // The record currently being built. Null when nothing is being watched.
    private ActivityRecord _currentRecord;
    private string _currentKey;
    private long _currentLastSavedAtUtcMs;

    // Stops two timer ticks from running over each other.
    private readonly object _tickLock = new object();

    /// <summary>Raised when a record is finished, or is being saved while still open.</summary>
    public event Action<ActivityRecord> RecordReady;

    public WindowWatcher(ForegroundWindowReader reader, IAppLogger logger, string deviceId)
    {
        _reader = reader;
        _logger = logger;
        _deviceId = deviceId;
    }

    public bool IsRunning
    {
        get { return _timer != null; }
    }

    public void Start()
    {
        if (_timer != null)
        {
            return;
        }

        _logger.Info("Window watcher started.");

        // This timer runs on a background thread, NOT on the screen's thread.
        // That is why recording keeps working while the window is closed.
        _timer = new System.Threading.Timer(
            OnTimerTick,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(CheckEverySeconds));
    }

    public void Stop()
    {
        if (_timer == null)
        {
            return;
        }

        _timer.Dispose();
        _timer = null;

        // Finish whatever was open, so the last record has a proper end time.
        lock (_tickLock)
        {
            CloseCurrentRecord(AppClock.NowUtcMs());
        }

        _logger.Info("Window watcher stopped.");
    }

    private void OnTimerTick(object state)
    {
        // If a tick is already running, skip this one rather than queue up.
        if (!Monitor.TryEnter(_tickLock))
        {
            return;
        }

        try
        {
            CheckActiveWindow();
        }
        catch (Exception error)
        {
            _logger.Error("Window watcher tick failed.", error);
        }
        finally
        {
            Monitor.Exit(_tickLock);
        }
    }

    private void CheckActiveWindow()
    {
        long now = AppClock.NowUtcMs();
        ForegroundWindowInfo info = _reader.Read();

        // Nothing readable, for example the screen is locked.
        if (!info.IsValid)
        {
            CloseCurrentRecord(now);
            return;
        }

        // Remove any unread counter like "(490)" BEFORE comparing. Otherwise a
        // new email would look like a new window. See WindowTitleCleaner.
        string cleanTitle = WindowTitleCleaner.Clean(info.WindowTitle);
        string key = info.ExePath + "|" + cleanTitle;

        // Still doing the same thing - just extend the current record.
        if (_currentRecord != null && key == _currentKey)
        {
            _currentRecord.EndedAtUtcMs = now;

            bool timeToSaveAgain =
                now - _currentLastSavedAtUtcMs >= SaveUnfinishedEverySeconds * 1000;

            if (timeToSaveAgain)
            {
                _currentLastSavedAtUtcMs = now;
                RaiseRecordReady(_currentRecord);
            }

            return;
        }

        // Something changed. Finish the old record, start a new one.
        CloseCurrentRecord(now);
        StartNewRecord(info, cleanTitle, key, now);
    }

    private void StartNewRecord(ForegroundWindowInfo info, string cleanTitle, string key, long now)
    {
        ActivityRecord record = new ActivityRecord();

        record.Kind = RecordKinds.Window;
        record.Source = "window-watcher";
        record.StartedAtUtcMs = now;
        record.EndedAtUtcMs = now;
        record.TimeZoneOffsetMinutes = AppClock.CurrentOffsetMinutes();
        record.AppName = info.AppName;
        record.ExePath = info.ExePath;
        record.WindowTitle = cleanTitle;

        record.Id = RecordId.Create(
            _deviceId,
            record.Kind,
            record.Source,
            record.StartedAtUtcMs,
            record.ExePath,
            record.WindowTitle);

        _currentRecord = record;
        _currentKey = key;
        _currentLastSavedAtUtcMs = now;
    }

    private void CloseCurrentRecord(long endedAtUtcMs)
    {
        if (_currentRecord == null)
        {
            return;
        }

        _currentRecord.EndedAtUtcMs = endedAtUtcMs;

        long lengthMs = endedAtUtcMs - _currentRecord.StartedAtUtcMs;
        bool longEnoughToKeep = lengthMs >= MinimumRecordSeconds * 1000;

        // A record that was already written to the database by the 30-second
        // heartbeat must still be closed properly, even if that is impossible
        // here (a heartbeat only happens after 30 seconds, which is far longer
        // than the minimum). This check keeps the rule true no matter what
        // those two numbers are changed to later.
        bool alreadySaved = _currentLastSavedAtUtcMs > _currentRecord.StartedAtUtcMs;

        if (longEnoughToKeep || alreadySaved)
        {
            RaiseRecordReady(_currentRecord);
        }

        _currentRecord = null;
        _currentKey = null;
    }

    private void RaiseRecordReady(ActivityRecord record)
    {
        Action<ActivityRecord> handler = RecordReady;

        if (handler != null)
        {
            handler(record);
        }
    }
}
