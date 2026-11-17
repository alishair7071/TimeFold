namespace TimeFold.Core.Privacy;

/// <summary>
/// The only way to read or change block rules.
///
/// Same principle as IActivityStore: nothing outside the implementation writes
/// SQL for this table. If the storage ever changes, one file changes.
/// </summary>
public interface IBlockRuleStore
{
    /// <summary>Every rule, switched on or not, newest first.</summary>
    List<BlockRule> GetAll();

    /// <summary>Only the switched-on rules. This is what the privacy gate uses.</summary>
    List<BlockRule> GetEnabled();

    /// <summary>
    /// Saves a new rule and returns its row number.
    ///
    /// If an identical rule already exists (same match type and pattern) this
    /// does nothing and returns the existing row number, because two rules
    /// saying the same thing cannot block anything the first one did not.
    /// </summary>
    long Add(BlockRule rule);

    /// <summary>Switches a rule on or off without losing it.</summary>
    void SetEnabled(long id, bool isEnabled);

    /// <summary>Removes a rule for good.</summary>
    void Delete(long id);

    /// <summary>How many rules exist, switched on or not.</summary>
    long CountAll();
}
