using TimeFold.Core.Privacy;
using TimeFold.Core.Storage;
using TimeFold.Core.Storage.Migrations;
using Xunit;

namespace TimeFold.Core.Tests.Privacy;

/// <summary>
/// Tests for storing block rules, and for the gate noticing changes to them.
///
/// These are the only tests in TimeFold that touch a real database. They use a
/// throwaway file in the system temp folder, deleted at the end - never your
/// real timefold.db.
/// </summary>
public class BlockRuleStoreTests : IDisposable
{
    private readonly string _databaseFile;
    private readonly Database _database;
    private readonly SqliteBlockRuleStore _store;

    public BlockRuleStoreTests()
    {
        _databaseFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-" + Guid.NewGuid().ToString("N") + ".db");

        _database = new Database(_databaseFile);
        _database.Prepare();

        new MigrationRunner(_database, new NullLogger()).RunPendingMigrations();

        _store = new SqliteBlockRuleStore(_database);
    }

    public void Dispose()
    {
        // The connection pool keeps the file open, so close it before deleting.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_databaseFile);
        }
        catch
        {
            // A leftover file in the temp folder is not worth failing a test over.
        }
    }

    // ---- saving and reading ------------------------------------------------

    [Fact]
    public void SavesAndReadsBackARule()
    {
        _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank"));

        List<BlockRule> rules = _store.GetAll();

        Assert.Single(rules);
        Assert.Equal(BlockMatchType.ExeFileName, rules[0].MatchType);
        Assert.Equal("MyBank.exe", rules[0].Pattern);
        Assert.Equal("My bank", rules[0].Note);
        Assert.True(rules[0].IsEnabled);
    }

    [Fact]
    public void KeepsEachMatchTypeApart()
    {
        // The same text can mean three different things depending on the type,
        // so storing the type wrongly would silently change what gets blocked.
        _store.Add(new BlockRule(BlockMatchType.ExeFileName, "bank", "a"));
        _store.Add(new BlockRule(BlockMatchType.ExePathContains, "bank", "b"));
        _store.Add(new BlockRule(BlockMatchType.WindowTitleContains, "bank", "c"));

        List<BlockRule> rules = _store.GetAll();

        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, r => r.MatchType == BlockMatchType.ExeFileName);
        Assert.Contains(rules, r => r.MatchType == BlockMatchType.ExePathContains);
        Assert.Contains(rules, r => r.MatchType == BlockMatchType.WindowTitleContains);
    }

    [Fact]
    public void AddingTheSameRuleTwiceChangesNothing()
    {
        long firstId = _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "once"));
        long secondId = _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "again"));

        Assert.Equal(1, _store.CountAll());
        Assert.Equal(firstId, secondId);
    }

    // ---- switching off and deleting ---------------------------------------

    [Fact]
    public void ASwitchedOffRuleIsKeptButNotApplied()
    {
        long id = _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank"));

        _store.SetEnabled(id, false);

        // Still in the list you can see...
        Assert.Equal(1, _store.CountAll());

        // ...but not in the list the gate uses.
        Assert.Empty(_store.GetEnabled());
    }

    [Fact]
    public void ASwitchedOffRuleBlocksNothing()
    {
        BlockRule rule = new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank");
        rule.IsEnabled = false;

        Assert.False(rule.Matches("C:\\apps\\MyBank.exe", "Accounts"));
    }

    [Fact]
    public void DeletingARuleRemovesIt()
    {
        long id = _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank"));

        _store.Delete(id);

        Assert.Equal(0, _store.CountAll());
    }

    // ---- the gate sees changes immediately --------------------------------

    [Fact]
    public void TheGateBlocksANewRuleAsSoonAsItIsReloaded()
    {
        // This is the test that matters. A rule you add must take effect now,
        // not after a restart - otherwise you would think you were protected
        // while TimeFold carried on recording.
        BlockRuleProvider provider = new BlockRuleProvider(_store);
        provider.Reload();

        PrivacyGate gate = new PrivacyGate(provider);

        Models.ActivityRecord record = new Models.ActivityRecord();
        record.ExePath = "C:\\apps\\MyBank.exe";
        record.WindowTitle = "Accounts";

        // Before: nothing blocks it.
        Assert.True(gate.Check(record, false).IsAllowed);

        // You add the rule.
        _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank"));
        provider.Reload();

        // After: blocked, with no restart.
        Assert.False(gate.Check(record, false).IsAllowed);
    }

    [Fact]
    public void TheGateStopsBlockingWhenARuleIsSwitchedOff()
    {
        long id = _store.Add(new BlockRule(BlockMatchType.ExeFileName, "MyBank.exe", "My bank"));

        BlockRuleProvider provider = new BlockRuleProvider(_store);
        provider.Reload();

        PrivacyGate gate = new PrivacyGate(provider);

        Models.ActivityRecord record = new Models.ActivityRecord();
        record.ExePath = "C:\\apps\\MyBank.exe";
        record.WindowTitle = "Accounts";

        Assert.False(gate.Check(record, false).IsAllowed);

        _store.SetEnabled(id, false);
        provider.Reload();

        Assert.True(gate.Check(record, false).IsAllowed);
    }

    // ---- seeding -----------------------------------------------------------

    [Fact]
    public void SeedingWritesTheBuiltInRulesOnceOnly()
    {
        Core.Settings.SettingsService settings = new Core.Settings.SettingsService(_database);
        BlockListSeeder seeder = new BlockListSeeder(_store, settings, new NullLogger());

        seeder.SeedIfNeeded();
        long afterFirst = _store.CountAll();

        seeder.SeedIfNeeded();
        long afterSecond = _store.CountAll();

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void ARuleYouDeletedDoesNotComeBack()
    {
        // If the shipped rules were written on every start, deleting one would
        // be impossible: it would reappear the next time you opened the app.
        Core.Settings.SettingsService settings = new Core.Settings.SettingsService(_database);
        BlockListSeeder seeder = new BlockListSeeder(_store, settings, new NullLogger());

        seeder.SeedIfNeeded();

        List<BlockRule> rules = _store.GetAll();
        long idToDelete = rules[0].Id;
        long countBefore = _store.CountAll();

        _store.Delete(idToDelete);

        // Restarting the app would call this again.
        seeder.SeedIfNeeded();

        Assert.Equal(countBefore - 1, _store.CountAll());
    }

    [Fact]
    public void TheShippedRulesAreMarkedAsComingFromTimeFold()
    {
        Core.Settings.SettingsService settings = new Core.Settings.SettingsService(_database);
        new BlockListSeeder(_store, settings, new NullLogger()).SeedIfNeeded();

        List<BlockRule> rules = _store.GetAll();

        Assert.All(rules, r => Assert.True(r.IsBuiltIn));
    }

    /// <summary>A logger that throws everything away. Tests do not need log files.</summary>
    private class NullLogger : Core.Logging.IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}
