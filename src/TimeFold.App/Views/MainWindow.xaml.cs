using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TimeFold.App.Composition;
using TimeFold.Core.Models;
using TimeFold.Core.Privacy;
using TimeFold.Core.Time;

// Both WPF and Windows Forms have a MessageBox. In this file we always mean
// the WPF one. Same fix as in App.xaml.cs.
using MessageBox = System.Windows.MessageBox;

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

    private void OnYourDataButtonClicked(object sender, RoutedEventArgs e)
    {
        YourDataWindow window = new YourDataWindow(_host);
        window.Owner = this;
        window.ShowDialog();

        // Records may have been deleted or restored while it was open.
        RefreshNow();
    }

    // ---- the right-click menu on a line ------------------------------------

    private ActivityRow GetSelectedRow()
    {
        ActivityRow row = ActivityList.SelectedItem as ActivityRow;

        if (row == null)
        {
            MessageBox.Show(
                this,
                "Click a line first, then right-click it.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        return row;
    }

    private void OnDeleteEntryClicked(object sender, RoutedEventArgs e)
    {
        ActivityRow row = GetSelectedRow();

        if (row == null)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Delete this entry?\n\n" +
            row.TimeRange + "  " + row.AppName + "\n" + row.WindowTitle + "\n\n" +
            "This cannot be undone.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _host.Store.DeleteRecord(row.RecordId);
        _host.Logger.Info("Deleted one record, at your request.");

        RefreshNow();
    }

    private void OnDeleteAppHistoryClicked(object sender, RoutedEventArgs e)
    {
        ActivityRow row = GetSelectedRow();

        if (row == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(row.ExeFileName))
        {
            MessageBox.Show(
                this,
                "This entry does not belong to a program, so there is nothing to delete by app.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        long count = _host.Store.CountRecordsForApp(row.ExeFileName);

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Delete all " + count + " record(s) from " + row.AppName + "?\n\n" +
            "Every day, not just today. This cannot be undone.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        int deleted = _host.Store.DeleteRecordsForApp(row.ExeFileName);
        _host.Logger.Info("Deleted " + deleted + " records for one app, at your request.");

        RefreshNow();
    }

    /// <summary>
    /// Blocks the app on the selected line, and offers to remove what it has
    /// already recorded.
    ///
    /// Blocking only stops new records. Someone who blocks an app has just
    /// decided they do not want it in their history - leaving the old entries
    /// sitting there would half-answer that, so we ask.
    /// </summary>
    private void OnBlockAppClicked(object sender, RoutedEventArgs e)
    {
        ActivityRow row = GetSelectedRow();

        if (row == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(row.ExeFileName))
        {
            MessageBox.Show(
                this,
                "This entry does not belong to a program, so there is nothing to block.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Never record " + row.AppName + " (" + row.ExeFileName + ") again?",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _host.BlockRules.Add(new BlockRule(
            BlockMatchType.ExeFileName,
            row.ExeFileName,
            "Blocked from the timeline"));

        _host.BlockRuleProvider.Reload();

        OfferToDeletePastHistory(row.AppName, row.ExeFileName);

        RefreshNow();
    }

    /// <summary>
    /// Asks whether to remove what a newly blocked app recorded in the past.
    ///
    /// Blocking only stops new records. Someone who has just decided they do
    /// not want an app in their history would be surprised to find the old
    /// entries still sitting there, so we ask rather than assume either way.
    /// </summary>
    public void OfferToDeletePastHistory(string appName, string exeFileName)
    {
        long existing = _host.Store.CountRecordsForApp(exeFileName);

        if (existing == 0)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            appName + " is now blocked, so nothing new will be recorded.\n\n" +
            "There are still " + existing + " record(s) from before. Delete those too?\n\n" +
            "This cannot be undone.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        int deleted = _host.Store.DeleteRecordsForApp(exeFileName);
        _host.Logger.Info("Deleted " + deleted + " past records for a newly blocked app.");
    }

    /// <summary>
    /// Opens the blocked apps screen.
    ///
    /// ShowDialog keeps it in front until you close it, so there is no way to
    /// have two of these open disagreeing about what the rules are.
    /// </summary>
    private void OnBlockedAppsButtonClicked(object sender, RoutedEventArgs e)
    {
        BlockedAppsWindow window = new BlockedAppsWindow(_host);
        window.Owner = this;
        window.ShowDialog();

        // Blocking something does not remove what is already recorded, so the
        // list behind is still correct - but refresh anyway so the counts at
        // the top are up to date.
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
