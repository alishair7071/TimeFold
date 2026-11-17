using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TimeFold.App.Composition;
using TimeFold.Core.Models;
using TimeFold.Core.Time;

namespace TimeFold.App.Views;

/// <summary>
/// The window showing what you did today.
///
/// It only reads and displays. It never records anything, never touches the
/// database directly, and never calls Windows APIs. It asks AppHost for data
/// and shows it.
///
/// Phase 3 replaces this with a proper timeline that can show any date.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(AppHost host)
    {
        InitializeComponent();

        _host = host;

        // Refresh every few seconds so the list stays current while open.
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += delegate { RefreshNow(); };
        _refreshTimer.Start();

        RefreshNow();
    }

    /// <summary>Reloads today's records and redraws the window.</summary>
    public void RefreshNow()
    {
        try
        {
            long fromUtcMs;
            long toUtcMs;
            AppClock.GetLocalDayRangeUtcMs(DateTime.Now, out fromUtcMs, out toUtcMs);

            List<ActivityRecord> records = _host.Store.GetRecordsBetween(fromUtcMs, toUtcMs);

            // Newest at the top - that is almost always what you want to see first.
            records.Reverse();

            List<ActivityRow> rows = new List<ActivityRow>();
            foreach (ActivityRecord record in records)
            {
                rows.Add(ActivityRow.FromRecord(record));
            }

            ActivityList.ItemsSource = rows;
            EmptyMessage.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateHeaderAndStatus(rows.Count);
        }
        catch (Exception error)
        {
            _host.Logger.Error("Could not refresh the window.", error);
            StatusText.Text = "Could not read the database. See the log file for details.";
        }
    }

    private void UpdateHeaderAndStatus(int rowCount)
    {
        DateText.Text = DateTime.Now.ToString("dddd, d MMMM yyyy");

        bool paused = _host.Recording.IsPaused;

        PauseButton.Content = paused ? "Resume recording" : "Pause recording";

        string state = paused
            ? "PAUSED - nothing is being recorded."
            : "Recording.";

        StatusText.Text =
            state + "   " +
            rowCount + " entries today.   " +
            _host.Recording.SavedCount + " saved this session, " +
            _host.Recording.DroppedCount + " blocked by the privacy gate.";

        long totalRecords = 0;
        try
        {
            totalRecords = _host.Store.CountAllRecords();
        }
        catch
        {
            // Not important enough to show an error for.
        }

        double databaseMb = _host.Database.GetFileSizeBytes() / 1024.0 / 1024.0;

        FooterText.Text =
            totalRecords + " records stored in total   |   " +
            "database " + databaseMb.ToString("0.0") + " MB   |   " +
            _host.Database.FilePath;
    }

    private void OnPauseButtonClicked(object sender, RoutedEventArgs e)
    {
        bool nowPaused = !_host.Recording.IsPaused;
        _host.Recording.SetPaused(nowPaused);
        RefreshNow();
    }

    private void OnRefreshButtonClicked(object sender, RoutedEventArgs e)
    {
        RefreshNow();
    }

    /// <summary>
    /// Closing the window HIDES it. It does not quit TimeFold.
    ///
    /// This is the most important behaviour in the whole app: TimeFold must keep
    /// recording with no window open. Quit is in the tray menu, and nowhere else.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();

        base.OnClosing(e);
    }
}
