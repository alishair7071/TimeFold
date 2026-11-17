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
    private readonly PresenceReader _presence;
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

    public WindowWatcher(
        ForegroundWindowReader reader,
        PresenceReader presence,
        IAppLogger logger,
        string deviceId)
    {
        _reader = reader;
        _presence = presence;
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

        // Are you actually here? A window stays in the foreground whether you
        // are at the desk or not, so this has to be asked first. Without it,
        // going to lunch with Chrome open would be recorded as two hours of
        // using Chrome.
        PresenceState presence = _presence.Read();

        if (presence != PresenceState.Active)
        {
            TrackAbsence(presence, now);
            return;
        }

        // You are back. Finish any away entry before looking at the window.
        CloseCurrentRecord(now);

        ForegroundWindowInfo info = _reader.Read();

        // Nothing readable, for example a secure screen we may not look at.
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

    /// <summary>
    /// Records that you were away, as its own kind of entry.
    ///
    /// The time is not thrown away, for two reasons. "Away from 1pm to 2pm" is
    /// itself worth remembering - it is part of answering what you were doing
    /// that day. And a gap with no explanation looks like TimeFold stopped
    /// working, which is exactly the doubt we do not want you to have.
    /// </summary>
    private void TrackAbsence(PresenceState presence, long now)
    {
        string kind = presence == PresenceState.Locked ? RecordKinds.Lock : RecordKinds.Idle;
        string key = "absence|" + kind;

        // Still away - just make the existing entry longer.
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

        // Just became away, or changed from idle to locked.
        CloseCurrentRecord(now);

        ActivityRecord record = new ActivityRecord();

        record.Kind = kind;
        record.Source = "presence-watcher";
        record.StartedAtUtcMs = now;
        record.EndedAtUtcMs = now;
        record.TimeZoneOffsetMinutes = AppClock.CurrentOffsetMinutes();

        // No app, no path, no title. We are deliberately not recording what was
        // on screen while you were away - least of all while it was locked.
        record.AppName = presence == PresenceState.Locked ? "Screen locked" : "Away";
        record.ExePath = AbsenceExePath;
        record.WindowTitle = record.AppName;

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

    /// <summary>
    /// A stand-in path for away entries.
    ///
    /// The privacy gate drops any record with no program path, because not
    /// knowing which program a window belongs to means we cannot check it
    /// against your block rules. An away entry genuinely has no program, so it
    /// needs something here - and it must be something no block rule could
    /// accidentally match.
    /// </summary>
    private const string AbsenceExePath = "timefold:absence";

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
