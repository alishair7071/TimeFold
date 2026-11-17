using TimeFold.Core.Privacy;

namespace TimeFold.App.Views;

/// <summary>
/// One block rule as it appears on screen.
///
/// Same idea as ActivityRow: BlockRule is how a rule is stored, BlockRuleRow is
/// how it is shown. "ExeFileName" is a name for programmers; "Program name" is
/// a name for a person. Translating between the two is the screen's job, so the
/// wording can change without touching the rule itself.
/// </summary>
public class BlockRuleRow
{
    public long Id { get; set; }
    public bool IsEnabled { get; set; }
    public string MatchTypeText { get; set; }
    public string Pattern { get; set; }
    public string Note { get; set; }
    public string SourceText { get; set; }

    public static BlockRuleRow FromRule(BlockRule rule)
    {
        BlockRuleRow row = new BlockRuleRow();

        row.Id = rule.Id;
        row.IsEnabled = rule.IsEnabled;
        row.MatchTypeText = DescribeMatchType(rule.MatchType);
        row.Pattern = rule.Pattern;
        row.Note = rule.Note;
        row.SourceText = rule.IsBuiltIn ? "TimeFold" : "You";

        return row;
    }

    public static string DescribeMatchType(BlockMatchType matchType)
    {
        switch (matchType)
        {
            case BlockMatchType.ExeFileName:
                return "Program name";

            case BlockMatchType.ExePathContains:
                return "Folder or path";

            case BlockMatchType.WindowTitleContains:
                return "Words in title";

            default:
                return matchType.ToString();
        }
    }
}
