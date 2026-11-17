using TimeFold.Core.Logging;
using TimeFold.Core.Settings;

namespace TimeFold.Core.Privacy;

/// <summary>
/// Puts the rules TimeFold ships with into a new database, once.
///
/// The "once" matters. If we wrote the built-in rules on every start, a rule
/// you deliberately deleted would come back every time you opened the app -
/// which would feel like the program overruling you, and would be a bug you
/// could never work around.
///
/// So we remember that we have done it, using a setting. After that the table
/// belongs to you.
/// </summary>
public class BlockListSeeder
{
    /// <summary>
    /// Remembers that the shipped rules have been written. The number on the
    /// end is a version: if a future release adds an important new built-in
    /// rule, it bumps this to "2" and only the new rules are added, leaving
    /// your edits alone.
    /// </summary>
    private const string SettingKey = "blocklist.seeded.v1";

    private readonly IBlockRuleStore _store;
    private readonly SettingsService _settings;
    private readonly IAppLogger _logger;

    public BlockListSeeder(IBlockRuleStore store, SettingsService settings, IAppLogger logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    public void SeedIfNeeded()
    {
        if (_settings.GetValue(SettingKey) == "true")
        {
            return;
        }

        List<BlockRule> defaults = DefaultBlockList.Create();

        foreach (BlockRule rule in defaults)
        {
            // Add() ignores a rule that already exists, so running this twice
            // cannot create duplicates even if the setting were lost.
            _store.Add(rule);
        }

        _settings.SetValue(SettingKey, "true");

        _logger.Info("Block list seeded with " + defaults.Count + " built-in rules.");
    }
}
