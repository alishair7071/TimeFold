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
/// In Phase 1 the rules are a fixed list in code (DefaultBlockList).
/// In Phase 2 they move into the database so you can add your own.
/// </summary>
public class BlockRule
{
    public BlockMatchType MatchType { get; set; }

    /// <summary>What to look for. Matching ignores upper and lower case.</summary>
    public string Pattern { get; set; }

    /// <summary>Why this rule exists. For humans only.</summary>
    public string Note { get; set; }

    public BlockRule(BlockMatchType matchType, string pattern, string note)
    {
        MatchType = matchType;
        Pattern = pattern;
        Note = note;
    }

    /// <summary>Does this rule apply to what is on screen right now?</summary>
    public bool Matches(string exePath, string windowTitle)
    {
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
