using System.Drawing;
using System.Windows.Forms;
using TimeFold.App.Composition;

namespace TimeFold.App.Tray;

/// <summary>
/// The icon near the clock, and its right-click menu.
///
/// This is TimeFold's real home. The window is just something you open
/// occasionally; the tray icon is always there and shows whether recording is on.
/// </summary>
public class TrayIconController : IDisposable
{
    private readonly AppHost _host;
    private readonly Action _onOpenRequested;
    private readonly Action _onQuitRequested;

    private NotifyIcon _notifyIcon;
    private ToolStripMenuItem _pauseMenuItem;

    public TrayIconController(AppHost host, Action onOpenRequested, Action onQuitRequested)
    {
        _host = host;
        _onOpenRequested = onOpenRequested;
        _onQuitRequested = onQuitRequested;
    }

    public void Show()
    {
        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = LoadIcon();
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = BuildMenu();

        // Double-clicking the icon opens the window.
        _notifyIcon.DoubleClick += delegate { _onOpenRequested(); };

        UpdateAppearance();
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();

        // The pause state can also be changed from the window, so refresh the
        // menu text every time the menu is opened. Otherwise the two can disagree.
        menu.Opening += delegate { UpdateAppearance(); };

        ToolStripMenuItem openItem = new ToolStripMenuItem("Open TimeFold");
        openItem.Click += delegate { _onOpenRequested(); };
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        _pauseMenuItem = new ToolStripMenuItem("Pause recording");
        _pauseMenuItem.Click += OnPauseClicked;
        menu.Items.Add(_pauseMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += delegate { _onQuitRequested(); };
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnPauseClicked(object sender, EventArgs e)
    {
        bool nowPaused = !_host.Recording.IsPaused;
        _host.Recording.SetPaused(nowPaused);
        UpdateAppearance();
    }

    /// <summary>
    /// Keeps the menu text and the tooltip in step with whether we are recording.
    /// The user must always be able to tell, at a glance, if TimeFold is on.
    /// </summary>
    public void UpdateAppearance()
    {
        if (_notifyIcon == null)
        {
            return;
        }

        bool paused = _host.Recording.IsPaused;

        _pauseMenuItem.Text = paused ? "Resume recording" : "Pause recording";
        _notifyIcon.Text = paused ? "TimeFold - paused" : "TimeFold - recording";
    }

    /// <summary>
    /// Loads the app icon. If the icon file is missing for any reason we fall
    /// back to a standard Windows icon, because an app with no tray icon would
    /// be invisible and impossible to quit.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            string exePath = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(exePath))
            {
                Icon extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // Fall through to the default below.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
