using TimeFold.Core.Backup;
using TimeFold.Core.Models;
using TimeFold.Core.Storage;
using TimeFold.Core.Storage.Migrations;
using Xunit;

namespace TimeFold.Core.Tests.Storage;

/// <summary>
/// Tests for deleting history and for export/restore.
///
/// Deleting is the only thing in TimeFold that cannot be undone, so the tests
/// here care as much about what is NOT deleted as about what is. A delete that
/// removes slightly too much is far worse than one that removes too little.
/// </summary>
public class DeleteAndExportTests : IDisposable
{
    private readonly string _databaseFile;
    private readonly Database _database;
    private readonly SqliteActivityStore _store;

    public DeleteAndExportTests()
    {
        _databaseFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-" + Guid.NewGuid().ToString("N") + ".db");

        _database = new Database(_databaseFile);
        _database.Prepare();

        new MigrationRunner(_database, new NullLogger()).RunPendingMigrations();

        _store = new SqliteActivityStore(_database);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_databaseFile);
        }
        catch
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    // ---- helpers -----------------------------------------------------------

    private ActivityRecord Save(string id, string exePath, long startedAtUtcMs)
    {
        ActivityRecord record = new ActivityRecord();

        record.Id = id;
        record.Kind = RecordKinds.Window;
        record.Source = "test";
        record.StartedAtUtcMs = startedAtUtcMs;
        record.EndedAtUtcMs = startedAtUtcMs + 5000;
        record.TimeZoneOffsetMinutes = 300;
        record.ExePath = exePath;
        record.AppName = Path.GetFileNameWithoutExtension(exePath);
        record.WindowTitle = "A window";

        _store.Save(record);

        return record;
    }

    // ---- deleting one record ----------------------------------------------

    [Fact]
    public void DeletesOneRecordAndLeavesTheRest()
    {
        Save("a", "C:\\apps\\one.exe", 1000);
        Save("b", "C:\\apps\\two.exe", 2000);

        int deleted = _store.DeleteRecord("a");

        Assert.Equal(1, deleted);
        Assert.Equal(1, _store.CountAllRecords());
    }

    [Fact]
    public void DeletingSomethingThatIsNotThereIsHarmless()
    {
        Save("a", "C:\\apps\\one.exe", 1000);

        int deleted = _store.DeleteRecord("does-not-exist");

        Assert.Equal(0, deleted);
        Assert.Equal(1, _store.CountAllRecords());
    }

    // ---- deleting a time range --------------------------------------------

    [Fact]
    public void DeletesOnlyWhatIsInsideTheRange()
    {
        Save("before", "C:\\apps\\one.exe", 500);
        Save("inside", "C:\\apps\\one.exe", 1500);
        Save("after", "C:\\apps\\one.exe", 2500);

        int deleted = _store.DeleteRecordsBetween(1000, 2000);

        Assert.Equal(1, deleted);
        Assert.Equal(2, _store.CountAllRecords());
    }

    [Fact]
    public void TheRangeIncludesItsStartAndExcludesItsEnd()
    {
        // Days sit next to each other with no gap. If both ends were included,
        // deleting Monday would take the first record of Tuesday with it.
        Save("at-start", "C:\\apps\\one.exe", 1000);
        Save("at-end", "C:\\apps\\one.exe", 2000);

        _store.DeleteRecordsBetween(1000, 2000);

        List<ActivityRecord> left = _store.GetAllRecords();

        Assert.Single(left);
        Assert.Equal("at-end", left[0].Id);
    }

    // ---- deleting by app ---------------------------------------------------

    [Fact]
    public void DeletesEveryRecordForOneApp()
    {
        Save("a", "C:\\Program Files\\Chrome\\chrome.exe", 1000);
        Save("b", "C:\\Program Files\\Chrome\\chrome.exe", 2000);
        Save("c", "C:\\apps\\code.exe", 3000);

        int deleted = _store.DeleteRecordsForApp("chrome.exe");

        Assert.Equal(2, deleted);
        Assert.Equal(1, _store.CountAllRecords());
    }

    [Fact]
    public void DeletingByAppIgnoresUpperAndLowerCase()
    {
        Save("a", "C:\\Program Files\\Chrome\\CHROME.EXE", 1000);

        int deleted = _store.DeleteRecordsForApp("chrome.exe");

        Assert.Equal(1, deleted);
    }

    [Fact]
    public void DeletingByAppDoesNotTouchASimilarlyNamedApp()
    {
        // "notchrome.exe" ends with "chrome.exe" as plain text. Matching on the
        // text alone would delete it too - which is why the match includes the
        // folder separator before the name.
        Save("real", "C:\\apps\\chrome.exe", 1000);
        Save("other", "C:\\apps\\notchrome.exe", 2000);

        _store.DeleteRecordsForApp("chrome.exe");

        List<ActivityRecord> left = _store.GetAllRecords();

        Assert.Single(left);
        Assert.Equal("other", left[0].Id);
    }

    [Fact]
    public void CountsRecordsForAnAppBeforeDeletingThem()
    {
        Save("a", "C:\\apps\\chrome.exe", 1000);
        Save("b", "C:\\apps\\chrome.exe", 2000);
        Save("c", "C:\\apps\\code.exe", 3000);

        Assert.Equal(2, _store.CountRecordsForApp("chrome.exe"));
        Assert.Equal(0, _store.CountRecordsForApp("firefox.exe"));
    }

    // ---- deleting everything -----------------------------------------------

    [Fact]
    public void DeletesEverything()
    {
        Save("a", "C:\\apps\\one.exe", 1000);
        Save("b", "C:\\apps\\two.exe", 2000);

        int deleted = _store.DeleteAllRecords();

        Assert.Equal(2, deleted);
        Assert.Equal(0, _store.CountAllRecords());
    }

    [Fact]
    public void DeletingEverythingKeepsYourSettings()
    {
        // Your history going is what you asked for. Your pause state and device
        // id going with it would be a surprise.
        Core.Settings.SettingsService settings = new Core.Settings.SettingsService(_database);
        settings.SetPaused(true);

        Save("a", "C:\\apps\\one.exe", 1000);

        _store.DeleteAllRecords();

        Assert.True(settings.IsPaused());
    }

    // ---- export and restore ------------------------------------------------

    [Fact]
    public void ExportsAndRestoresEverything()
    {
        Save("a", "C:\\apps\\one.exe", 1000);
        Save("b", "C:\\apps\\two.exe", 2000);

        string exportFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-export-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            BackupService backup = new BackupService(_store, new NullLogger());

            int exported = backup.ExportToFile(exportFile);
            Assert.Equal(2, exported);

            _store.DeleteAllRecords();
            Assert.Equal(0, _store.CountAllRecords());

            int restored = backup.ImportFromFile(exportFile);

            Assert.Equal(2, restored);
            Assert.Equal(2, _store.CountAllRecords());
        }
        finally
        {
            File.Delete(exportFile);
        }
    }

    [Fact]
    public void RestoringTwiceDoesNotDoubleYourHistory()
    {
        // Ids are built from the data, so saving the same record twice updates
        // one row instead of adding a second. This is what makes restore safe
        // to repeat when you are not sure whether it worked the first time.
        Save("a", "C:\\apps\\one.exe", 1000);

        string exportFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-export-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            BackupService backup = new BackupService(_store, new NullLogger());
            backup.ExportToFile(exportFile);

            backup.ImportFromFile(exportFile);
            backup.ImportFromFile(exportFile);

            Assert.Equal(1, _store.CountAllRecords());
        }
        finally
        {
            File.Delete(exportFile);
        }
    }

    [Fact]
    public void RestoringKeepsTheTimesExactly()
    {
        Save("a", "C:\\apps\\one.exe", 1763312688000);

        string exportFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-export-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            BackupService backup = new BackupService(_store, new NullLogger());
            backup.ExportToFile(exportFile);

            _store.DeleteAllRecords();
            backup.ImportFromFile(exportFile);

            List<ActivityRecord> restored = _store.GetAllRecords();

            Assert.Single(restored);
            Assert.Equal(1763312688000, restored[0].StartedAtUtcMs);
            Assert.Equal(300, restored[0].TimeZoneOffsetMinutes);
        }
        finally
        {
            File.Delete(exportFile);
        }
    }

    [Fact]
    public void RefusesAFileThatIsNotATimeFoldExport()
    {
        string junkFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-junk-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            File.WriteAllText(junkFile, "{}");

            BackupService backup = new BackupService(_store, new NullLogger());

            Assert.Throws<InvalidDataException>(() => backup.ImportFromFile(junkFile));
        }
        finally
        {
            File.Delete(junkFile);
        }
    }

    private class NullLogger : Core.Logging.IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}
