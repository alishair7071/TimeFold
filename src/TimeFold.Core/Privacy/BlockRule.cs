namespace TimeFold.Core.Privacy;

/// <summary>How a rule decides whether it matches.</summary>
public enum BlockMatchType
{
    /// <summary>
    /// The program file name, for example "keepass.exe".
    /// Survives the app being installed in a different folder.
    /// </summary>
    ExeFileName,

    /// <summary>
    /// Part of the full program path, for example "\Banking\".
    /// Useful for blocking a whole folder.
    /// </summary>
    ExePathContains,

    /// <summary>
    /// Part of the window title, for example "Password".
    /// Catches private content inside an app that is otherwise allowed.
    /// </summary>
    WindowTitleContains
}

/// <summary>
/// One rule saying "do not record this".
///
/// Phase 1 kept these as a fixed list in code. Since Phase 2.1 they live in the
/// block_rules table, so they are your data and you can add your own.
/// </summary>
public class BlockRule
{
    /// <summary>
    /// The database row number. Zero for a rule that has not been saved yet.
    /// Used only to switch a rule off or delete it.
    /// </summary>
    public long Id { get; set; }

    public BlockMatchType MatchType { get; set; }

    /// <summary>What to look for. Matching ignores upper and lower case.</summary>
    public string Pattern { get; set; }

    /// <summary>Why this rule exists. For humans only.</summary>
    public string Note { get; set; }

    /// <summary>
    /// A switched-off rule stays in the list but stops blocking. This is kinder
    /// than deleting: you can turn a rule off to check something, then back on,
    /// without typing it again.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// True for the rules TimeFold ships with, false for ones you added.
    /// The screen uses this to show which are yours.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    public BlockRule()
    {
        IsEnabled = true;
    }

    public BlockRule(BlockMatchType matchType, string pattern, string note)
    {
        MatchType = matchType;
        Pattern = pattern;
        Note = note;
        IsEnabled = true;
    }

    /// <summary>Does this rule apply to what is on screen right now?</summary>
    public bool Matches(string exePath, string windowTitle)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (string.IsNullOrEmpty(Pattern))
        {
            return false;
        }

        switch (MatchType)
        {
            case BlockMatchType.ExeFileName:
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }
                string fileName = Path.GetFileName(exePath);
                return string.Equals(fileName, Pattern, StringComparison.OrdinalIgnoreCase);

            case BlockMatchType.ExePathContains:
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }
                return exePath.Contains(Pattern, StringComparison.OrdinalIgnoreCase);

            case BlockMatchType.WindowTitleContains:
                if (string.IsNullOrEmpty(windowTitle))
                {
                    return false;
                }
                return windowTitle.Contains(Pattern, StringComparison.OrdinalIgnoreCase);

            default:
                return false;
        }
    }
}
