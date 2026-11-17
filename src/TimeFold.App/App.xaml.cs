using System.Threading;
using System.Windows;
using TimeFold.App.Composition;
using TimeFold.App.Tray;
using TimeFold.App.Views;

// This project uses both WPF (for the window) and Windows Forms (for the tray
// icon). Both of them have a class called Application and a class called
// MessageBox, so C# cannot tell which one we mean.
// These two lines say: in this file, always mean the WPF one.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TimeFold.App;

/// <summary>
/// The entry point of the program.
///
/// Note what this does NOT do: it does not open a window. TimeFold starts
/// invisibly, as a tray icon. You open the window when you want to look at
/// your history.
/// </summary>
public partial class App : Application
{
    // Used to make sure only one copy of TimeFold runs at a time.
    private Mutex _singleInstanceMutex;

    private AppHost _host;
    private TrayIconController _tray;
    private MainWindow _mainWindow;

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

            // The window is created now but stays hidden until you open it.
            _mainWindow = new MainWindow(_host);

            // The tray icon is the app's real home.
            _tray = new TrayIconController(_host, ShowMainWindow, QuitApplication);
            _tray.Show();

            ShowWindowIfThisIsTheFirstRun();
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
    /// The very first time TimeFold runs, open the window.
    ///
    /// Normally TimeFold starts invisibly, as a tray icon. But Windows hides
    /// new tray icons behind the "^" arrow, so a brand new user sees nothing
    /// at all happen and reasonably concludes the app is broken. Showing the
    /// window once proves it is alive and shows where its history lives.
    ///
    /// Only once. After that it starts quietly, as intended.
    /// </summary>
    private void ShowWindowIfThisIsTheFirstRun()
    {
        const string settingKey = "firstrun.completed";

        if (_host.Settings.GetValue(settingKey) == "true")
        {
            return;
        }

        ShowMainWindow();

        _host.Settings.SetValue(settingKey, "true");
        _host.Logger.Info("First run - opened the window so the app is visible.");
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.RefreshNow();
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
