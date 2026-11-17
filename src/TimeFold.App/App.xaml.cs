using System.Diagnostics;
using System.Threading;
using System.Windows;
using TimeFold.App.Composition;
using TimeFold.App.Tray;

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
    // Used to make sure only one copy of TimeFold runs at a time.
    private Mutex _singleInstanceMutex;

    private AppHost _host;
    private TrayIconController _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryClaimSingleInstance())
        {
            MessageBox.Show(
                "TimeFold is already running. Look for its icon in the system tray, " +
                "near the clock.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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

            OpenTheInterfaceIfThisIsTheFirstRun();
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
    /// The very first time TimeFold runs, open the interface.
    ///
    /// Otherwise nothing at all appears to happen: Windows hides new tray icons
    /// behind the arrow, so a new user reasonably concludes the app is broken.
    /// Only once. After that it starts quietly, as intended.
    /// </summary>
    private void OpenTheInterfaceIfThisIsTheFirstRun()
    {
        const string settingKey = "firstrun.completed";

        if (_host.Settings.GetValue(settingKey) == "true")
        {
            return;
        }

        OpenTheInterface();

        _host.Settings.SetValue(settingKey, "true");
        _host.Logger.Info("First run - opened the interface so the app is visible.");
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

        if (_singleInstanceMutex != null)
        {
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Takes a system-wide named lock. If another copy of TimeFold already holds
    /// it, this one must not start - two recorders would write duplicate records.
    /// </summary>
    private bool TryClaimSingleInstance()
    {
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, "TimeFold.SingleInstance", out createdNew);
        return createdNew;
    }
}
