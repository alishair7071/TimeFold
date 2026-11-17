using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeFold.App.Composition;
using TimeFold.Core.Privacy;

// This project uses both WPF (for windows) and Windows Forms (for the tray
// icon), and several class names exist in both. These lines say: in this file,
// always mean the WPF one. Same fix as in App.xaml.cs.
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace TimeFold.App.Views;

/// <summary>
/// The screen where you decide what TimeFold must never record.
///
/// Every change here does two things, always in this order:
///
///   1. write it to the database
///   2. tell the privacy gate to reload
///
/// Step 2 is what makes a new rule take effect at once, without restarting.
/// A privacy control that only works after a restart is worse than none,
/// because you would believe you were protected when you were not.
/// </summary>
public partial class BlockedAppsWindow : Window
{
    private readonly AppHost _host;

    // Set while the list is being filled, so that ticking the boxes in code
    // does not look like you ticking them.
    private bool _isRefreshing;

    public BlockedAppsWindow(AppHost host)
    {
        _host = host;

        InitializeComponent();

        FillMatchTypeChoices();
        RefreshNow();
    }

    private void FillMatchTypeChoices()
    {
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.ExeFileName));
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.ExePathContains));
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.WindowTitleContains));

        // Program name first, because it is the one that should be used most.
        MatchTypeBox.SelectedIndex = 0;
    }

    /// <summary>Turns the chosen line of the drop-down back into a match type.</summary>
    private BlockMatchType GetSelectedMatchType()
    {
        switch (MatchTypeBox.SelectedIndex)
        {
            case 1:
                return BlockMatchType.ExePathContains;

            case 2:
                return BlockMatchType.WindowTitleContains;

            default:
                return BlockMatchType.ExeFileName;
        }
    }

    private void OnMatchTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HelpText == null)
        {
            return;
        }

        switch (GetSelectedMatchType())
        {
            case BlockMatchType.ExeFileName:
                HelpText.Text =
                    "The program's file name, for example KeePass.exe. This is the one to " +
                    "use normally: it keeps working when the app updates or is installed " +
                    "somewhere else.";
                break;

            case BlockMatchType.ExePathContains:
                HelpText.Text =
                    "Any part of the program's full path, for example \\Banking\\. Blocks " +
                    "every program in a folder at once. Be careful: a short word like " +
                    "\\Program Files\\ would block almost everything.";
                break;

            case BlockMatchType.WindowTitleContains:
                HelpText.Text =
                    "Any word in the window title, for example Incognito or Payslip. This " +
                    "is the only way to block private content inside an app you otherwise " +
                    "want recorded - but it over-blocks easily, so keep the word specific.";
                break;
        }
    }

    // ---- adding ------------------------------------------------------------

    private void OnPatternKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddClicked(sender, null);
        }
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        string pattern = PatternBox.Text == null ? "" : PatternBox.Text.Trim();

        if (pattern.Length == 0)
        {
            MessageBox.Show(
                this,
                "Type what to block first.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        BlockMatchType matchType = GetSelectedMatchType();

        // A very short pattern can match almost everything, which would quietly
        // stop TimeFold recording anything at all. Better to ask than to let
        // someone wipe out their own history by accident.
        if (pattern.Length < 3)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                "\"" + pattern + "\" is very short and may block far more than you expect.\n\n" +
                "Add it anyway?",
                "TimeFold",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        BlockRule rule = new BlockRule(matchType, pattern, "Added by you");

        _host.BlockRules.Add(rule);
        _host.BlockRuleProvider.Reload();

        PatternBox.Text = "";
        RefreshNow();

        StatusText.Text = "Added. Nothing matching \"" + pattern + "\" will be recorded from now on.";
    }

    // ---- switching on and off ---------------------------------------------

    private void OnRuleToggled(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        CheckBox box = sender as CheckBox;

        if (box == null || box.Tag == null)
        {
            return;
        }

        long id = (long)box.Tag;
        bool isEnabled = box.IsChecked == true;

        _host.BlockRules.SetEnabled(id, isEnabled);
        _host.BlockRuleProvider.Reload();

        RefreshNow();

        StatusText.Text = isEnabled
            ? "Rule switched on."
            : "Rule switched off. Anything it was blocking will now be recorded.";
    }

    // ---- deleting ----------------------------------------------------------

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        BlockRuleRow selected = RulesList.SelectedItem as BlockRuleRow;

        if (selected == null)
        {
            MessageBox.Show(
                this,
                "Choose a rule in the list first.",
                "TimeFold",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Delete this rule?\n\n" +
            selected.MatchTypeText + ": " + selected.Pattern + "\n\n" +
            "Anything it was blocking will be recorded from now on.",
            "TimeFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _host.BlockRules.Delete(selected.Id);
        _host.BlockRuleProvider.Reload();

        RefreshNow();

        StatusText.Text = "Rule deleted.";
    }

    // ---- filling the list --------------------------------------------------

    public void RefreshNow()
    {
        _isRefreshing = true;

        try
        {
            List<BlockRule> rules = _host.BlockRules.GetAll();
            List<BlockRuleRow> rows = new List<BlockRuleRow>();

            foreach (BlockRule rule in rules)
            {
                rows.Add(BlockRuleRow.FromRule(rule));
            }

            RulesList.ItemsSource = rows;

            EmptyMessage.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int activeCount = _host.BlockRuleProvider.GetRules().Count;

            StatusText.Text =
                rows.Count + " rule(s), " + activeCount + " switched on.";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
