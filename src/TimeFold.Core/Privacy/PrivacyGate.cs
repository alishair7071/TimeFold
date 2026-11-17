using TimeFold.Core.Models;

namespace TimeFold.Core.Privacy;

/// <summary>
/// THE MOST IMPORTANT CLASS IN TIMEFOLD.
///
/// Every record passes through here before it is saved. The gate answers one
/// question: may this be written to disk?
///
/// Three rules that never change:
///
///   1. It runs BEFORE saving, not before showing.
///      Blocked data is never written at all. The way to test this is to open
///      the database and find nothing - not to check that the screen hides it.
///
///   2. If it is not sure, it says no.
///      Losing one boring record costs nothing. Saving one private record
///      cannot be undone.
///
///   3. It is a plain function: same input, same answer, no files, no database.
///      That is what makes it easy to test properly, and testing it properly is
///      the only reason to trust it.
/// </summary>
public class PrivacyGate
{
    private readonly List<BlockRule> _rules;

    public PrivacyGate(List<BlockRule> rules)
    {
        _rules = rules ?? new List<BlockRule>();
    }

    /// <summary>
    /// Decides what to do with one record.
    /// </summary>
    /// <param name="record">What the watcher saw.</param>
    /// <param name="isPaused">True when the user has paused recording.</param>
    public GateVerdict Check(ActivityRecord record, bool isPaused)
    {
        // Nothing at all is recorded while paused.
        if (isPaused)
        {
            return GateVerdict.Drop("paused");
        }

        if (record == null)
        {
            return GateVerdict.Drop("empty_record");
        }

        // Rule 2 - fail closed. If we cannot tell which program this was, we
        // cannot check it against the block list, so we must not save it.
        if (string.IsNullOrWhiteSpace(record.ExePath))
        {
            return GateVerdict.Drop("unknown_program");
        }

        // A window with no title is usually a background or system window.
        // It tells us nothing useful, so there is no reason to keep it.
        if (string.IsNullOrWhiteSpace(record.WindowTitle))
        {
            return GateVerdict.Drop("empty_title");
        }

        foreach (BlockRule rule in _rules)
        {
            if (rule.Matches(record.ExePath, record.WindowTitle))
            {
                // Note we return only a short code. We never put the blocked
                // title or path into the reason, because reasons get logged.
                return GateVerdict.Drop("blocked_rule");
            }
        }

        return GateVerdict.Allow();
    }
}
