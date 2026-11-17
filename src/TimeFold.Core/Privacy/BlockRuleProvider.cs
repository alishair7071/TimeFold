namespace TimeFold.Core.Privacy;

/// <summary>
/// Holds the switched-on block rules in memory and hands them to the privacy gate.
///
/// Why this exists at all:
///
/// The gate is asked a question roughly once a second, and it must answer from
/// memory. Reading the database on every check would put a file read on the
/// recording path for no benefit - the rules change when you edit them, which
/// is almost never.
///
/// So the rules are loaded once and kept. Anything that changes a rule calls
/// Reload(), and the very next check uses the new list. There is no delay and
/// nothing to remember to restart.
///
/// Safety rule: if loading ever fails, we keep the rules we already had. An
/// empty list would mean "block nothing", and silently blocking nothing is the
/// one outcome this whole part of the app exists to prevent.
/// </summary>
public class BlockRuleProvider
{
    private readonly IBlockRuleStore _store;

    // Guards the swap, because Reload() is called from the screen thread while
    // the gate reads this from the watcher's background thread.
    private readonly object _lock = new object();

    private List<BlockRule> _rules = new List<BlockRule>();

    public BlockRuleProvider(IBlockRuleStore store)
    {
        _store = store;
    }

    /// <summary>The rules the gate should apply right now.</summary>
    public List<BlockRule> GetRules()
    {
        lock (_lock)
        {
            return _rules;
        }
    }

    /// <summary>
    /// Re-reads the rules from the database. Call this after adding, removing,
    /// or switching a rule.
    /// </summary>
    public void Reload()
    {
        List<BlockRule> loaded = _store.GetEnabled();

        // Never replace a good list with nothing because of a failed read.
        if (loaded == null)
        {
            return;
        }

        lock (_lock)
        {
            _rules = loaded;
        }
    }
}
