using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
/// The timeline: what you did, on any day you have recorded.
///
/// It only reads and displays. It never records anything, never touches the
/// database directly, and never calls Windows APIs. It asks AppHost for data
/// and shows it.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(AppHost host)
    {
        InitializeComponent();

        _host = host;

        DayPicker.SelectedDate = _shownDay;
        LimitThePickerToDaysThatExist();

        // Refresh every few seconds so the list stays current while open.
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += delegate { RefreshIfItWouldNotInterrupt(); };
        _refreshTimer.Start();

        RefreshNow();
    }

    // =======================================================================
    //  Which day am I looking at, and am I searching?
    // =======================================================================

    /// <summary>The day being shown. Ignored while a search is running.</summary>
    private DateTime _shownDay = DateTime.Today;

    /// <summary>What is typed in the search box, or empty for none.</summary>
    private string _searchText = "";

    private bool IsSearching
    {
        get { return !string.IsNullOrWhiteSpace(_searchText); }
    }

    /// <summary>
    /// Greys out dates with nothing behind them.
    ///
    /// You cannot have recorded anything before the first record or after
    /// today, so offering those dates would only let you land on an empty page
    /// and wonder whether something was lost.
    /// </summary>
    private void LimitThePickerToDaysThatExist()
    {
        try
        {
            long? firstUtcMs;
            long? lastUtcMs;

            _host.Store.GetRecordedDateRange(out firstUtcMs, out lastUtcMs);

            DayPicker.DisplayDateEnd = DateTime.Today;

            if (firstUtcMs != null)
            {
                DayPicker.DisplayDateStart =
                    AppClock.ToLocalTime(firstUtcMs.Value, AppClock.CurrentOffsetMinutes()).Date;
            }
        }
        catch (Exception error)
        {
            // Not worth bothering you about - the picker simply stays open.
            _host.Logger.Error("Could not work out the recorded date range.", error);
        }
    }

    /// <summary>
    /// The automatic refresh, which must not get in your way.
    ///
    /// Redrawing the list throws away where you had scrolled to and what you
    /// had selected. That is fine while you watch today filling up, and
    /// infuriating while you are reading a day from last month or typing in the
    /// search box. Neither of those changes on its own, so there is nothing to
    /// refresh for.
    /// </summary>
    private void RefreshIfItWouldNotInterrupt()
    {
        if (IsSearching)
        {
            return;
        }

        if (_shownDay != DateTime.Today)
        {
            return;
        }

        RefreshNow();
    }

    private void OnPreviousDayClicked(object sender, RoutedEventArgs e)
    {
        GoToDay(_shownDay.AddDays(-1));
    }

    private void OnNextDayClicked(object sender, RoutedEventArgs e)
    {
        GoToDay(_shownDay.AddDays(1));
    }

    private void OnTodayClicked(object sender, RoutedEventArgs e)
    {
        GoToDay(DateTime.Today);
    }

    private void OnDayPicked(object sender, SelectionChangedEventArgs e)
    {
        if (DayPicker.SelectedDate == null)
        {
            return;
        }

        GoToDay(DayPicker.SelectedDate.Value.Date);
    }

    private void GoToDay(DateTime day)
    {
        _shownDay = day.Date;

        // Choosing a day means you want to look at that day, so a search left
        // in the box would hide what you just asked to see.
        if (IsSearching)
        {
            _searchText = "";
            SearchBox.Text = "";
        }

        if (DayPicker.SelectedDate != _shownDay)
        {
            DayPicker.SelectedDate = _shownDay;
        LimitThePickerToDaysThatExist();
        }

        RefreshNow();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text == null ? "" : SearchBox.Text;
        RefreshNow();
    }

    private void OnClearSearchClicked(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _searchText = "";
        RefreshNow();
    }

    // =======================================================================
    //  Drawing the list
    // =======================================================================

    /// <summary>Reloads what is being shown and redraws the window.</summary>
    public void RefreshNow()
    {
        try
        {
            List<ActivityRecord> records;
            bool searching = IsSearching;

            if (searching)
            {
                // Searches cover every day, so there is no date range here.
                records = _host.Store.Search(_searchText, MaximumSearchResults);
            }
            else
            {
                long fromUtcMs;
                long toUtcMs;
                AppClock.GetLocalDayRangeUtcMs(_shownDay, out fromUtcMs, out toUtcMs);

                records = _host.Store.GetRecordsBetween(fromUtcMs, toUtcMs);

                // Newest at the top - almost always what you want to see first.
                records.Reverse();
            }

            List<ActivityRow> rows = new List<ActivityRow>();

            foreach (ActivityRecord record in records)
            {
                // Search results carry their date in the heading, because two
                // results an hour apart might be a month apart.
                rows.Add(ActivityRow.FromRecord(record, searching));
            }

            ShowRows(rows);
            UpdateHeaderAndStatus(rows.Count, searching);
        }
        catch (Exception error)
        {
            _host.Logger.Error("Could not refresh the window.", error);
            StatusText.Text = "Could not read the database. See the log file for details.";
        }
    }

    /// <summary>
    /// How many search results to fetch.
    ///
    /// A limit is needed because searching a common word across years could
    /// match tens of thousands of rows, and nobody reads past the first page.
    /// Ranked results mean the best ones are inside the limit anyway.
    /// </summary>
    private const int MaximumSearchResults = 500;

    /// <summary>
    /// Puts the rows on screen, grouped by hour.
    ///
    /// CollectionViewSource is what does the grouping. The alternative - adding
    /// fake heading rows into the list - would mean every piece of code that
    /// touches a row having to check whether it is a real one first.
    /// </summary>
    private void ShowRows(List<ActivityRow> rows)
    {
        CollectionViewSource view = new CollectionViewSource();
        view.Source = rows;
        view.GroupDescriptions.Add(new PropertyGroupDescription("HourGroup"));

        ActivityList.ItemsSource = view.View;

        EmptyMessage.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (rows.Count == 0)
        {
            EmptyMessage.Text = IsSearching
                ? "Nothing found for what you typed."
                : "Nothing recorded on this day.";
        }
    }

    /// <summary>
    /// "Today", "Yesterday", or the date. The two words save you working out
    /// whether 16 November was yesterday, which is the sort of small friction
    /// that makes an app feel like work.
    /// </summary>
    private static string DescribeDay(DateTime day)
    {
        if (day == DateTime.Today)
        {
            return "Today";
        }

        if (day == DateTime.Today.AddDays(-1))
        {
            return "Yesterday";
        }

        return day.ToString("d MMMM yyyy");
    }

    private void UpdateHeaderAndStatus(int rowCount, bool searching)
    {
        if (searching)
        {
            HeadingText.Text = "Search";
            DateText.Text = "Every day you have ever recorded.";
        }
        else
        {
            HeadingText.Text = DescribeDay(_shownDay);
            DateText.Text = _shownDay.ToString("dddd, d MMMM yyyy");
        }

        // There is no history in the future, so there is nowhere to go.
        NextDayButton.IsEnabled = _shownDay < DateTime.Today;

        bool paused = _host.Recording.IsPaused;

        PauseButton.Content = paused ? "Resume recording" : "Pause recording";

        string state = paused
            ? "PAUSED - nothing is being recorded."
            : "Recording.";

        string what = searching
            ? rowCount + " result(s)."
            : rowCount + " entries on this day.";

        StatusText.Text =
            state + "   " +
            what + "   " +
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
