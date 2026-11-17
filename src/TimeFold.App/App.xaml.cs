using System.Diagnostics;
using System.Threading;
using System.Windows;
using TimeFold.App.Composition;
using TimeFold.App.Tray;
using TimeFold.Core.SystemIntegration;

// This project uses both WPF (for the application shell) and Windows Forms
// (for the tray icon). Both have a class called Application and a class called
// MessageBox, so C# cannot tell which one we mean.
// These two lines say: in this file, always mean the WPF one.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TimeFold.App;

/// <summary>
/// The entry point of the program.
///
/// TimeFold has no windows of its own any more. It is a tray icon that records,
/// plus a web interface it opens in your browser when you ask for it.
///
/// Why a browser rather than a window: the interface is now ordinary HTML and
/// CSS, which is far easier to make look like something from this decade, and
/// is completely separate from Windows. The recorder is the Windows part; the
/// interface is a web page.
///
/// The server does not start with the app. It starts the first time you choose
/// Open. If you never open the interface, TimeFold never opens a port.
/// </summary>
public partial class App : Application
{
    private SingleInstance _singleInstance;
    private AppHost _host;
    private TrayIconController _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Is a copy already running? If so, ask it to show itself and get
        // out of the way. This is what makes double-clicking the desktop
        // shortcut always do the right thing, whether TimeFold is running or
        // not - instead of telling you to go and find a hidden tray icon.
        if (!TryClaimSingleInstance())
        {
            if (!SingleInstance.AskTheRunningCopyToOpen())
            {
                // Could not reach it. Rare: the other copy quit in the moment
                // between us failing to take the lock and ringing its doorbell.
                MessageBox.Show(
                    "TimeFold is already running. Look for its icon in the system tray, " +
                    "near the clock.",
                    "TimeFold",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        try
        {
            // Build and start everything. See AppHost.Start().
            _host = new AppHost();
            _host.Start();

            // The tray icon is the app's home.
            _tray = new TrayIconController(_host, OpenTheInterface, QuitApplication);
            _tray.Show();

            // From now on, a later copy ringing the doorbell opens the
            // interface instead of starting a second recorder.
            _singleInstance.StartListeningForOpenRequests(_host.Logger, OpenFromAnotherCopy);

            OpenTheInterfaceUnlessWindowsStartedUs(e.Args);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                "TimeFold could not start.\n\n" + error.Message,
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    /// <summary>
    /// Starts the web server if it is not already running, then opens it in
    /// your browser.
    ///
    /// The address carries the secret key, which is why TimeFold opens the
    /// browser itself rather than telling you an address to type. Typing one
    /// would not work: without the key the server answers nothing.
    /// </summary>
    private void OpenTheInterface()
    {
        try
        {
            string address = _host.Web.Start();

            // UseShellExecute tells Windows to open this the way it would if
            // you had clicked a link - which means your default browser.
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = address;
            startInfo.UseShellExecute = true;

            Process.Start(startInfo);
        }
        catch (Exception error)
        {
            _host.Logger.Error("Could not open the web interface.", error);

            MessageBox.Show(
                "TimeFold could not open its interface.\n\n" + error.Message,
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Called from the listening thread when another copy rings the doorbell.
    ///
    /// Dispatcher.Invoke hands the work to the screen's thread. Starting a
    /// browser and showing a message box are both things that must happen
    /// there, and this arrives on a background thread.
    /// </summary>
    private void OpenFromAnotherCopy()
    {
        Dispatcher.Invoke(new Action(OpenTheInterface));
    }

    /// <summary>
    /// Opens the interface, unless Windows started us at login.
    ///
    /// Starting TimeFold yourself - from the desktop shortcut or the Start
    /// menu - means you want to look at it, so it opens. Windows starting it
    /// automatically does not, and a browser window appearing uninvited every
    /// morning would be maddening, so that case passes --silent.
    /// </summary>
    private void OpenTheInterfaceUnlessWindowsStartedUs(string[] arguments)
    {
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, AutoStartService.SilentArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                _host.Logger.Info("Started by Windows at login - staying in the tray.");
                return;
            }
        }

        OpenTheInterface();
    }

    private void QuitApplication()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
        {
            _tray.Dispose();
        }

        if (_host != null)
        {
            _host.Stop();
        }

        if (_singleInstance != null)
        {
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Takes a system-wide named lock. If another copy of TimeFold already holds
    /// it, this one must not start - two recorders would write duplicate records.
    /// </summary>
    private bool TryClaimSingleInstance()
    {
        _singleInstance = new SingleInstance();

        return _singleInstance.TryClaim();
    }
}
