using TimeFold.Core.Logging;
using TimeFold.Core.Privacy;
using TimeFold.Core.Recording;
using TimeFold.Core.Settings;
using TimeFold.Core.Storage;
using TimeFold.Core.Storage.Migrations;
using TimeFold.Core.SystemIntegration;
using TimeFold.Core.Watchers;
using TimeFold.Core.Watchers.Native;
using TimeFold.Web;

namespace TimeFold.App.Composition;

/// <summary>
/// The one place where every part of TimeFold is created and joined together.
///
/// This is called a "composition root". Everywhere else, a class is handed the
/// things it needs; it never creates them itself. That keeps each class easy to
/// read on its own, and makes the whole startup sequence visible in one file.
///
/// Read Start() from top to bottom to understand exactly how TimeFold boots.
/// </summary>
public class AppHost
{
    public IAppLogger Logger { get; private set; }
    public Database Database { get; private set; }
    public IActivityStore Store { get; private set; }
    public SettingsService Settings { get; private set; }
    public IBlockRuleStore BlockRules { get; private set; }
    public BlockRuleProvider BlockRuleProvider { get; private set; }
    public RecordingService Recording { get; private set; }
    public AutoStartService AutoStart { get; private set; }

    /// <summary>
    /// The web interface. Not started here - it starts the first time you ask
    /// to open TimeFold, so that a port is never opened unless you want one.
    /// </summary>
    public WebServer Web { get; private set; }

    public void Start()
    {
        // 1. Make sure the data folders exist.
        AppPaths.EnsureFoldersExist();

        // 2. Logging first, so everything after this can report problems.
        Logger = new FileLogger(AppPaths.LogsFolder);
        Logger.Info("---- TimeFold starting ----");

        // 3. Open the database and bring its structure up to date.
        Database = new Database(AppPaths.DatabaseFile);
        Database.Prepare();

        MigrationRunner migrations = new MigrationRunner(Database, Logger);
        migrations.RunPendingMigrations();

        // 4. Storage and settings.
        Store = new SqliteActivityStore(Database);
        Settings = new SettingsService(Database);

        string deviceId = Settings.GetOrCreateDeviceId();

        // 5. Block rules. These live in the database so you can edit them.
        //    A brand new database is given the rules TimeFold ships with, once.
        BlockRules = new SqliteBlockRuleStore(Database);

        BlockListSeeder seeder = new BlockListSeeder(BlockRules, Settings, Logger);
        seeder.SeedIfNeeded();

        BlockRuleProvider = new BlockRuleProvider(BlockRules);
        BlockRuleProvider.Reload();

        // 6. The privacy gate. Built before the watcher on purpose - nothing
        //    should ever be watched without a gate ready to filter it.
        PrivacyGate gate = new PrivacyGate(BlockRuleProvider);

        Logger.Info("Privacy gate ready with " + BlockRuleProvider.GetRules().Count +
                    " active block rules.");

        // 7. The watchers.
        //    PresenceReader answers "are you actually at the computer?", so that
        //    walking away with a window open is recorded as being away rather
        //    than as hours of work.
        ForegroundWindowReader reader = new ForegroundWindowReader();

        PresenceReader presence = new PresenceReader();
        presence.IdleAfterSeconds = Settings.GetIdleAfterSeconds();

        WindowWatcher watcher = new WindowWatcher(reader, presence, Logger, deviceId);

        // 8. Join watcher + gate + store together.
        Recording = new RecordingService(watcher, gate, Store, Settings, Logger);

        // 9. Windows startup entry.
        AutoStart = new AutoStartService(Logger);
        LeaveAutoStartToTheUser();

        // 10. The web interface, built but NOT started. See WebServer.
        ApiHandler api = new ApiHandler(
            Store, BlockRules, BlockRuleProvider, Recording, Settings, Database, Logger);

        Web = new WebServer(api, Logger);

        // 11. Begin recording.
        Recording.Start();

        Logger.Info("TimeFold started. Database: " + Database.FilePath);
    }

    public void Stop()
    {
        try
        {
            if (Web != null)
            {
                Web.Stop();
            }

            if (Recording != null)
            {
                Recording.Stop();
            }

            if (Logger != null)
            {
                Logger.Info("---- TimeFold stopped ----");
            }
        }
        catch
        {
            // Shutting down must never throw.
        }
    }

    /// <summary>
    /// TimeFold does NOT add itself to Windows startup by itself.
    ///
    /// Starting silently on every boot is something the user should switch on
    /// deliberately, not something an app decides for them. The AutoStart
    /// service stays available here so a settings switch can call
    /// AutoStart.Enable(...) / AutoStart.Disable() later (Phase 2).
    ///
    /// Until then, TimeFold runs only when you open it from the shortcut.
    /// </summary>
    private void LeaveAutoStartToTheUser()
    {
        // Deliberately does nothing.
    }
}
