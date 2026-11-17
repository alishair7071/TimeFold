using System.Threading;
using TimeFold.Core.Logging;

namespace TimeFold.App.Composition;

/// <summary>
/// Makes sure only one copy of TimeFold runs, and lets a second copy ask the
/// first one to show itself.
///
/// Why the second half matters as much as the first:
///
/// TimeFold lives in the tray with no window of its own. The obvious way to
/// open it is to double-click the desktop shortcut. Before this class existed,
/// doing that while TimeFold was already running showed a message saying "it is
/// already running, look in the system tray" - and Windows hides new tray icons
/// behind the little arrow, so that message sent you hunting for something you
/// could not see.
///
/// Now the second copy quietly taps the first one on the shoulder and exits.
/// The shortcut does the sensible thing every time: start TimeFold if it is not
/// running, open it if it is.
///
/// Two Windows objects do the work, both named so that separate programs can
/// find the same one:
///
///   a Mutex           - held by the first copy, so later ones know to step back
///   an EventWaitHandle - a doorbell the second copy rings
/// </summary>
public class SingleInstance : IDisposable
{
    /// <summary>
    /// These names are how two separate copies of the program find the same
    /// objects. They must never change casually: an older copy left running
    /// after an upgrade would be listening on the old names.
    /// </summary>
    private const string MutexName = "TimeFold.SingleInstance";
    private const string OpenRequestName = "TimeFold.OpenRequest";

    /// <summary>
    /// Added to both names. Empty for the real app.
    ///
    /// It exists so tests can use names of their own. Without it, running the
    /// tests on a machine where TimeFold is actually running would have the
    /// test fighting the real app over the same two system-wide objects, and
    /// failing for a reason that has nothing to do with the code.
    /// </summary>
    private readonly string _nameSuffix;

    private IAppLogger _logger;

    private Mutex _mutex;
    private EventWaitHandle _openRequest;
    private ManualResetEvent _stopListening;
    private Thread _listener;

    /// <summary>Null until TryClaim has run. True if this copy is the first.</summary>
    public bool IsFirstCopy { get; private set; }

    public SingleInstance()
    {
        _nameSuffix = "";
    }

    /// <summary>For tests, so they do not clash with a real running TimeFold.</summary>
    public SingleInstance(string nameSuffix)
    {
        _nameSuffix = nameSuffix == null ? "" : nameSuffix;
    }

    /// <summary>
    /// Takes the system-wide lock. True means this copy should carry on and
    /// start recording; false means another copy already is.
    /// </summary>
    public bool TryClaim()
    {
        bool createdNew;

        _mutex = new Mutex(true, MutexName + _nameSuffix, out createdNew);

        IsFirstCopy = createdNew;

        return createdNew;
    }

    /// <summary>
    /// Listens for a later copy asking us to show the interface.
    ///
    /// Called only by the first copy, after TryClaim succeeded.
    ///
    /// The waiting happens on its own background thread, because waiting blocks
    /// whatever thread it is on and this one must never be the screen's thread.
    /// </summary>
    public void StartListeningForOpenRequests(IAppLogger logger, Action onOpenRequested)
    {
        // The logger arrives here rather than in the constructor because it
        // does not exist yet when the lock is taken: AppHost builds it, and
        // AppHost only runs once this copy has won the lock.
        _logger = logger;

        // EventResetMode.AutoReset: the doorbell un-presses itself once we have
        // answered it. With ManualReset it would stay pressed and we would
        // answer it over and over in a tight loop.
        _openRequest = new EventWaitHandle(
            false, EventResetMode.AutoReset, OpenRequestName + _nameSuffix);

        _stopListening = new ManualResetEvent(false);

        _listener = new Thread(delegate ()
        {
            ListenUntilToldToStop(onOpenRequested);
        });

        _listener.IsBackground = true;
        _listener.Start();
    }

    private void ListenUntilToldToStop(Action onOpenRequested)
    {
        // Two things can wake this thread up: the doorbell, or us shutting down.
        // Waiting on both together is what lets the thread end cleanly instead
        // of being abandoned mid-wait.
        WaitHandle[] waitingFor = new WaitHandle[] { _openRequest, _stopListening };

        while (true)
        {
            int whichOne;

            try
            {
                whichOne = WaitHandle.WaitAny(waitingFor);
            }
            catch
            {
                // The handles were closed out from under us during shutdown.
                return;
            }

            // Index 1 is _stopListening: time to finish.
            if (whichOne != 0)
            {
                return;
            }

            try
            {
                _logger.Info("Another copy asked us to open the interface.");

                onOpenRequested();
            }
            catch (Exception error)
            {
                // A failure here must not kill the thread - that would leave
                // the shortcut silently doing nothing from then on.
                _logger.Error("Could not open the interface when asked.", error);
            }
        }
    }

    /// <summary>
    /// Rings the doorbell of the copy that is already running.
    ///
    /// Called by the second copy, just before it exits. Returns false if there
    /// was nobody to ring - which should not happen, since we only get here
    /// after failing to take the lock, but a copy could have quit in the
    /// moment between the two.
    /// </summary>
    public static bool AskTheRunningCopyToOpen()
    {
        return AskTheRunningCopyToOpen("");
    }

    /// <summary>For tests. See the note on the name suffix above.</summary>
    public static bool AskTheRunningCopyToOpen(string nameSuffix)
    {
        EventWaitHandle openRequest;

        if (!EventWaitHandle.TryOpenExisting(OpenRequestName + nameSuffix, out openRequest))
        {
            return false;
        }

        using (openRequest)
        {
            openRequest.Set();
            return true;
        }
    }

    public void Dispose()
    {
        // Stop the listening thread before closing what it is waiting on.
        if (_stopListening != null)
        {
            _stopListening.Set();
        }

        if (_listener != null)
        {
            _listener.Join(TimeSpan.FromSeconds(2));
            _listener = null;
        }

        if (_openRequest != null)
        {
            _openRequest.Dispose();
            _openRequest = null;
        }

        if (_stopListening != null)
        {
            _stopListening.Dispose();
            _stopListening = null;
        }

        if (_mutex != null)
        {
            if (IsFirstCopy)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Already released, or never held. Nothing to do.
                }
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
