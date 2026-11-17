using TimeFold.Core.Logging;
using TimeFold.Core.Models;
using TimeFold.Core.Privacy;
using TimeFold.Core.Settings;
using TimeFold.Core.Storage;
using TimeFold.Core.Watchers;

namespace TimeFold.Core.Recording;

/// <summary>
/// Connects the three parts of recording:
///
///     watcher  ->  privacy gate  ->  store
///
/// This is the only place where those three meet. The watcher does not know the
/// gate exists, and the gate does not know the store exists. That is what lets
/// us add new watchers in later phases without changing anything else.
/// </summary>
public class RecordingService
{
    private readonly WindowWatcher _watcher;
    private readonly PrivacyGate _gate;
    private readonly IActivityStore _store;
    private readonly SettingsService _settings;
    private readonly IAppLogger _logger;

    // Counters shown in the UI so you can see the gate is doing its job.
    private long _savedCount;
    private long _droppedCount;

    // ---- stopping the log from repeating itself ----------------------------
    //
    // An unfinished record is re-offered every 30 seconds. If it is a blocked
    // one - say you left a password manager open - the gate drops it every 30
    // seconds and we used to write a log line every single time. Leave it open
    // all day and that is about 2,880 identical lines burying everything that
    // actually matters.
    //
    // So we log a drop once, then stay quiet while it is the same record being
    // dropped for the same reason, and write a single summary line when it
    // finally changes. Nothing is hidden - the count is still reported.
    private string _lastDropKey;
    private long _repeatedDropCount;

    /// <summary>Raised after something is saved, so the screen can refresh.</summary>
    public event Action RecordSaved;

    public RecordingService(
        WindowWatcher watcher,
        PrivacyGate gate,
        IActivityStore store,
        SettingsService settings,
        IAppLogger logger)
    {
        _watcher = watcher;
        _gate = gate;
        _store = store;
        _settings = settings;
        _logger = logger;

        _watcher.RecordReady += OnRecordReady;
    }

    public long SavedCount
    {
        get { return _savedCount; }
    }

    public long DroppedCount
    {
        get { return _droppedCount; }
    }

    public bool IsPaused
    {
        get { return _settings.IsPaused(); }
    }

    public bool IsRecording
    {
        get { return _watcher.IsRunning; }
    }

    /// <summary>Called once when the app starts.</summary>
    public void Start()
    {
        if (_settings.IsPaused())
        {
            _logger.Info("Recording is paused, watcher not started.");
            return;
        }

        _watcher.Start();
    }

    public void Stop()
    {
        _watcher.Stop();
        FlushRepeatedDropSummary();
    }

    /// <summary>
    /// Writes one log line for a dropped record, but stays quiet if it is the
    /// same record being dropped for the same reason as last time.
    ///
    /// The log line contains the reason CODE only. Never the window title or
    /// the file path - otherwise the private data we just refused to save would
    /// end up in the log file instead, which is the same leak by another route.
    /// </summary>
    private void LogDropWithoutRepeating(ActivityRecord record, string reasonCode)
    {
        // The record id is a hash, not readable data, so it is safe to use here
        // as a way of telling "the same thing again" from "something new".
        string recordId = record == null ? "none" : record.Id;
        string dropKey = recordId + "|" + reasonCode;

        if (dropKey == _lastDropKey)
        {
            _repeatedDropCount++;
            return;
        }

        FlushRepeatedDropSummary();

        _lastDropKey = dropKey;
        _repeatedDropCount = 0;

        _logger.Info("Record dropped by privacy gate. Reason: " + reasonCode);
    }

    /// <summary>
    /// If we have been quietly skipping repeats, write one line saying how many
    /// there were, then start counting again.
    /// </summary>
    private void FlushRepeatedDropSummary()
    {
        if (_repeatedDropCount > 0)
        {
            _logger.Info(
                "The same record was dropped " + _repeatedDropCount +
                " more time(s) for the same reason.");
        }

        _lastDropKey = null;
        _repeatedDropCount = 0;
    }

    /// <summary>
    /// Turns recording on or off.
    ///
    /// Note we do two things: stop the watcher AND remember the paused setting.
    /// The gate also refuses everything while paused. Two defences for the same
    /// rule, because "pause really means pause" is a promise we cannot get wrong.
    /// </summary>
    public void SetPaused(bool paused)
    {
        _settings.SetPaused(paused);

        if (paused)
        {
            _watcher.Stop();
            _logger.Info("Recording paused by user.");
        }
        else
        {
            _watcher.Start();
            _logger.Info("Recording resumed by user.");
        }
    }

    /// <summary>
    /// Runs every time the watcher has a record ready.
    /// This runs on a background thread, not the screen thread.
    /// </summary>
    private void OnRecordReady(ActivityRecord record)
    {
        try
        {
            GateVerdict verdict = _gate.Check(record, _settings.IsPaused());

            if (!verdict.IsAllowed)
            {
                _droppedCount++;
                LogDropWithoutRepeating(record, verdict.ReasonCode);
                return;
            }

            // Something different is happening now, so if we were staying quiet
            // about a repeating drop, say how many times it happened.
            FlushRepeatedDropSummary();

            _store.Save(record);
            _savedCount++;

            Action handler = RecordSaved;
            if (handler != null)
            {
                handler();
            }
        }
        catch (Exception error)
        {
            _logger.Error("Could not save a record.", error);
        }
    }
}
