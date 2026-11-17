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

                // We log the reason code only. Never the title or the path -
                // otherwise the private data we just refused to save ends up
                // in the log file instead.
                _logger.Info("Record dropped by privacy gate. Reason: " + verdict.ReasonCode);
                return;
            }

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
