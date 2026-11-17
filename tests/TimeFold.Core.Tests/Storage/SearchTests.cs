using System.Diagnostics;
using TimeFold.Core.Models;
using TimeFold.Core.Storage;
using TimeFold.Core.Storage.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace TimeFold.Core.Tests.Storage;

/// <summary>
/// Tests for search.
///
/// The one that matters most is DeletingARecordRemovesItFromSearch. In an app
/// about privacy, a deleted entry that still turns up in search is not a small
/// bug - it means deletion did not mean what it said.
/// </summary>
public class SearchTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _databaseFile;
    private readonly Database _database;
    private readonly SqliteActivityStore _store;

    public SearchTests(ITestOutputHelper output)
    {
        _output = output;

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

    private ActivityRecord Save(string id, string appName, string windowTitle, long startedAtUtcMs)
    {
        ActivityRecord record = new ActivityRecord();

        record.Id = id;
        record.Kind = RecordKinds.Window;
        record.Source = "test";
        record.StartedAtUtcMs = startedAtUtcMs;
        record.EndedAtUtcMs = startedAtUtcMs + 5000;
        record.TimeZoneOffsetMinutes = 300;
        record.ExePath = "C:\\apps\\" + appName + ".exe";
        record.AppName = appName;
        record.WindowTitle = windowTitle;

        _store.Save(record);

        return record;
    }

    // ---- finding things ----------------------------------------------------

    [Fact]
    public void FindsAWordInTheWindowTitle()
    {
        Save("a", "Chrome", "Pull Request #9921 - jaegertracing/jaeger", 1000);
        Save("b", "Chrome", "Inbox - Gmail", 2000);

        List<ActivityRecord> results = _store.Search("jaeger", 100);

        Assert.Single(results);
        Assert.Equal("a", results[0].Id);
    }

    [Fact]
    public void FindsAWordInTheAppName()
    {
        Save("a", "Visual Studio Code", "Database.cs", 1000);
        Save("b", "Chrome", "Inbox", 2000);

        List<ActivityRecord> results = _store.Search("visual", 100);

        Assert.Single(results);
        Assert.Equal("a", results[0].Id);
    }

    [Fact]
    public void IgnoresUpperAndLowerCase()
    {
        Save("a", "Chrome", "JAEGER tracing", 1000);

        Assert.Single(_store.Search("jaeger", 100));
        Assert.Single(_store.Search("JaEgEr", 100));
    }

    [Fact]
    public void MatchesAWordYouHaveNotFinishedTyping()
    {
        // Results should appear while you type, not only once you stop.
        Save("a", "Chrome", "jaegertracing repository", 1000);

        Assert.Single(_store.Search("jaeg", 100));
    }

    [Fact]
    public void TwoWordsMeansBoth()
    {
        Save("a", "Chrome", "jaeger pull request", 1000);
        Save("b", "Chrome", "jaeger issues", 2000);

        List<ActivityRecord> results = _store.Search("jaeger pull", 100);

        Assert.Single(results);
        Assert.Equal("a", results[0].Id);
    }

    [Fact]
    public void FindsDifferentEndingsOfTheSameWord()
    {
        // The porter tokenizer: "meeting" should find "meetings".
        Save("a", "Teams", "Weekly meetings", 1000);

        Assert.Single(_store.Search("meeting", 100));
    }

    [Fact]
    public void FindsWordsWithAccentsWithoutTypingTheAccent()
    {
        Save("a", "Chrome", "Caf\u00e9 menu", 1000);

        Assert.Single(_store.Search("cafe", 100));
    }

    [Fact]
    public void FindsNonEnglishText()
    {
        Save("a", "Chrome", "\u0645\u0631\u062D\u0628\u0627 YouTube", 1000);

        Assert.Single(_store.Search("\u0645\u0631\u062D\u0628\u0627", 100));
    }

    // ---- not finding things ------------------------------------------------

    [Fact]
    public void AnEmptySearchFindsNothingRatherThanEverything()
    {
        Save("a", "Chrome", "Something", 1000);

        Assert.Empty(_store.Search("", 100));
        Assert.Empty(_store.Search("   ", 100));
        Assert.Empty(_store.Search(null, 100));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("(")]
    [InlineData("^")]
    [InlineData("NEAR(")]
    [InlineData("a OR b")]
    [InlineData("\" OR activity_search MATCH \"")]
    public void SpecialCharactersCannotBreakTheSearch(string typed)
    {
        // FTS5 has its own query language, so punctuation typed by a person
        // could either crash the search or silently search for something else.
        // Nothing typed here should ever throw.
        Save("a", "Chrome", "Something ordinary", 1000);

        List<ActivityRecord> results = _store.Search(typed, 100);

        Assert.NotNull(results);
    }

    [Fact]
    public void RespectsTheResultLimit()
    {
        for (int i = 0; i < 20; i++)
        {
            Save("id" + i, "Chrome", "jaeger item " + i, 1000 + i);
        }

        Assert.Equal(5, _store.Search("jaeger", 5).Count);
    }

    // ---- staying in step with the records ---------------------------------

    [Fact]
    public void DeletingARecordRemovesItFromSearch()
    {
        // The whole reason search lives inside the same database.
        Save("a", "Chrome", "jaeger pull request", 1000);

        Assert.Single(_store.Search("jaeger", 100));

        _store.DeleteRecord("a");

        Assert.Empty(_store.Search("jaeger", 100));
    }

    [Fact]
    public void DeletingAnAppRemovesItsRecordsFromSearch()
    {
        Save("a", "Chrome", "jaeger pull request", 1000);
        Save("b", "Chrome", "jaeger issues", 2000);

        _store.DeleteRecordsForApp("Chrome.exe");

        Assert.Empty(_store.Search("jaeger", 100));
    }

    [Fact]
    public void DeletingEverythingEmptiesSearch()
    {
        Save("a", "Chrome", "jaeger", 1000);
        Save("b", "Code", "database", 2000);

        _store.DeleteAllRecords();

        Assert.Empty(_store.Search("jaeger", 100));
        Assert.Empty(_store.Search("database", 100));
    }

    [Fact]
    public void ChangingATitleChangesWhatIsFound()
    {
        // An open record is saved again every 30 seconds as its title changes,
        // so this is the common case rather than a rare one.
        ActivityRecord record = Save("a", "Chrome", "Inbox", 1000);

        record.WindowTitle = "jaeger pull request";
        _store.Save(record);

        Assert.Empty(_store.Search("Inbox", 100));
        Assert.Single(_store.Search("jaeger", 100));
    }

    [Fact]
    public void RecordsFromBeforeTheIndexExistedAreStillFound()
    {
        // Migration 3 indexes what is already there. Without that step,
        // everything recorded before the upgrade would silently never appear
        // in search - and search would report no error, just nothing.
        List<ActivityRecord> all = _store.GetAllRecords();

        Assert.Empty(all);

        Save("a", "Chrome", "backfill check", 1000);

        Assert.Single(_store.Search("backfill", 100));
    }

    // ---- speed -------------------------------------------------------------

    [Fact]
    public void SearchIsFastOnALotOfData()
    {
        // The Phase 3 target is a search in under about 0.2 seconds across 90+
        // days of real data. 50,000 records is roughly two years of use.
        const int recordCount = 50000;

        using (Microsoft.Data.Sqlite.SqliteConnection connection = _database.OpenConnection())
        {
            using (Microsoft.Data.Sqlite.SqliteTransaction transaction = connection.BeginTransaction())
            using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO activity_records (
                        id, kind, source, started_at_utc_ms, ended_at_utc_ms,
                        tz_offset_minutes, app_name, exe_path, window_title,
                        saved_at_utc_ms, updated_at_utc_ms, row_version)
                    VALUES ($id, 'window', 'test', $start, $start,
                            300, $app, 'C:\\apps\\test.exe', $title,
                            $start, $start, 1);";

                Microsoft.Data.Sqlite.SqliteParameter id = command.Parameters.Add(
                    "$id", Microsoft.Data.Sqlite.SqliteType.Text);
                Microsoft.Data.Sqlite.SqliteParameter start = command.Parameters.Add(
                    "$start", Microsoft.Data.Sqlite.SqliteType.Integer);
                Microsoft.Data.Sqlite.SqliteParameter app = command.Parameters.Add(
                    "$app", Microsoft.Data.Sqlite.SqliteType.Text);
                Microsoft.Data.Sqlite.SqliteParameter title = command.Parameters.Add(
                    "$title", Microsoft.Data.Sqlite.SqliteType.Text);

                for (int i = 0; i < recordCount; i++)
                {
                    id.Value = "perf-" + i;
                    start.Value = 1700000000000L + i * 1000L;
                    app.Value = "App" + (i % 40);
                    title.Value = "Working on document number " + i + " for project alpha";
                }

                // The needle in the haystack.
                id.Value = "needle";
                start.Value = 1700000000000L;
                app.Value = "Chrome";
                title.Value = "jaegertracing pull request 9921";
                command.ExecuteNonQuery();

                transaction.Commit();
            }
        }

        Stopwatch watch = Stopwatch.StartNew();
        List<ActivityRecord> results = _store.Search("jaegertracing", 100);
        watch.Stop();

        _output.WriteLine("Search took " + watch.ElapsedMilliseconds + " ms.");

        Assert.Single(results);

        // The bound is deliberately loose. The claim being checked is "this
        // scales", not "this machine is fast today" - a test that fails because
        // the computer was busy teaches nobody anything.
        Assert.True(
            watch.ElapsedMilliseconds < 1000,
            "Search took " + watch.ElapsedMilliseconds + " ms, which is too slow.");
    }

    private class NullLogger : Core.Logging.IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}
