using System.Windows;
using TimeFold.App.Composition;
using TimeFold.Core.Backup;
using TimeFold.Core.Time;

using MessageBox = System.Windows.MessageBox;

namespace TimeFold.App.Views;

/// <summary>
/// Export, restore and delete.
///
/// These three belong on one screen because they are one subject: what happens
/// to the history you have already recorded. Export is listed first on purpose
/// - it is the thing to do before the thing below it.
///
/// Every delete here says exactly how many records went. "Done" would leave you
/// wondering whether it worked, and deleting is the one action in TimeFold that
/// cannot be taken back.
/// </summary>
public partial class YourDataWindow : Window
{
    private readonly AppHost _host;
    private readonly BackupService _backup;

    public YourDataWindow(AppHost host)
    {
        _host = host;
        _backup = new BackupService(host.Store, host.Logger);

        InitializeComponent();

        DayPicker.SelectedDate = DateTime.Today;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        long total = _host.Store.CountAllRecords();
        long sizeBytes = _host.Database.GetFileSizeBytes();

        SummaryText.Text =
            total + " records, " + FormatSize(sizeBytes) + " on disk.";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return Math.Round(bytes / 1024.0) + " KB";
        }

        return Math.Round(bytes / 1024.0 / 1024.0, 1) + " MB";
    }

    // ---- export ------------------------------------------------------------

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();

        dialog.FileName = BackupService.SuggestFileName();
        dialog.DefaultExt = ".json";
        dialog.Filter = "TimeFold export (*.json)|*.json";

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            int count = _backup.ExportToFile(dialog.FileName);

            StatusText.Text =
                "Exported " + count + " records to " + dialog.FileName;
        }
        catch (Exception error)
        {
            ShowFailure("Could not export your data.", error);
        }
    }

    // ---- restore -----------------------------------------------------------

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();

        dialog.DefaultExt = ".json";
        dialog.Filter = "TimeFold export (*.json)|*.json";

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            int count = _backup.ImportFromFile(dialog.FileName);

            RefreshSummary();

            StatusText.Text =
                "Restored " + count + " records. Records you already had were left as they were.";
        }
        catch (Exception error)
        {
            ShowFailure("Could not restore that file.", error);
        }
    }

    // ---- deleting ----------------------------------------------------------

    private void OnDeleteTodayClicked(object sender, RoutedEventArgs e)
    {
        DeleteOneDay(DateTime.Today);
    }

    private void OnDeleteChosenDayClicked(object sender, RoutedEventArgs e)
    {
        if (DayPicker.SelectedDate == null)
        {
            MessageBox.Show(
                this,
                "Pick a date first.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DeleteOneDay(DayPicker.SelectedDate.Value);
    }

    private void DeleteOneDay(DateTime day)
    {
        long fromUtcMs;
        long toUtcMs;

        AppClock.GetLocalDayRangeUtcMs(day, out fromUtcMs, out toUtcMs);

        // Count first, so the question can say how much is at stake rather than
        // asking you to agree to an unknown amount.
        int willDelete = _host.Store.GetRecordsBetween(fromUtcMs, toUtcMs).Count;

        if (willDelete == 0)
        {
            StatusText.Text = "Nothing recorded on " + day.ToString("d MMMM yyyy") + ".";
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Delete " + willDelete + " record(s) from " + day.ToString("d MMMM yyyy") + "?\n\n" +
            "This cannot be undone.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        int deleted = _host.Store.DeleteRecordsBetween(fromUtcMs, toUtcMs);

        _host.Logger.Info("Deleted " + deleted + " records for one day, at your request.");

        RefreshSummary();

        StatusText.Text = "Deleted " + deleted + " record(s) from " + day.ToString("d MMMM yyyy") + ".";
    }

    private void OnDeleteEverythingClicked(object sender, RoutedEventArgs e)
    {
        long total = _host.Store.CountAllRecords();

        if (total == 0)
        {
            StatusText.Text = "There is nothing to delete.";
            return;
        }

        // Asked twice on purpose. The first question is easy to click through
        // without reading; the second one is not, because it names what goes.
        MessageBoxResult first = MessageBox.Show(
            this,
            "Delete your entire history?\n\n" +
            total + " records will be removed. This cannot be undone.\n\n" +
            "If you have not exported a copy, say No and do that first.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (first != MessageBoxResult.Yes)
        {
            return;
        }

        MessageBoxResult second = MessageBox.Show(
            this,
            "Last check.\n\n" +
            "All " + total + " records, from every day, will be gone for good.\n\n" +
            "Your block rules and settings are kept.",
            "TimeFold - delete everything",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);

        if (second != MessageBoxResult.Yes)
        {
            return;
        }

        int deleted = _host.Store.DeleteAllRecords();

        _host.Logger.Info("Deleted the entire history (" + deleted + " records), at your request.");

        RefreshSummary();

        StatusText.Text = "Deleted everything - " + deleted + " record(s). Recording continues from now.";
    }

    // ---- shared ------------------------------------------------------------

    private void ShowFailure(string what, Exception error)
    {
        _host.Logger.Error(what, error);

        MessageBox.Show(
            this,
            what + "\n\n" + error.Message,
            "TimeFold",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        StatusText.Text = what;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
