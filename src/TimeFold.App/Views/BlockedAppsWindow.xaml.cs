using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TimeFold.App.Composition;
using TimeFold.Core.Privacy;
using TimeFold.Core.SystemIntegration;

// This project uses both WPF (for windows) and Windows Forms (for the tray
// icon), and several class names exist in both. These lines say: in this file,
// always mean the WPF one. Same fix as in App.xaml.cs.
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace TimeFold.App.Views;

/// <summary>
/// The screen where you decide what TimeFold must never record.
///
/// Two tabs, because there are two very different jobs:
///
///   "Your apps"       - the normal way. A list of the programs on this
///                       computer; tick one to block it, untick to unblock.
///   "Advanced rules"  - for the things a list of apps cannot express: a whole
///                       folder, or private words in a window title.
///
/// Every change does two things, always in this order:
///
///   1. write it to the database
///   2. tell the privacy gate to reload
///
/// Step 2 is what makes a change take effect at once, without restarting.
/// A privacy control that only works after a restart is worse than none,
/// because you would believe you were protected when you were not.
/// </summary>
public partial class BlockedAppsWindow : Window
{
    private readonly AppHost _host;

    /// <summary>Every app found on this computer. Loaded once, then filtered.</summary>
    private List<InstalledApp> _allApps = new List<InstalledApp>();

    /// <summary>
    /// Set while a list is being filled, so that ticking boxes in code does not
    /// look like you ticking them.
    /// </summary>
    private bool _isRefreshing;

    public BlockedAppsWindow(AppHost host)
    {
        _host = host;

        InitializeComponent();

        FillMatchTypeChoices();
        RefreshRules();
        StartScanningForApps();
    }

    // =======================================================================
    //  TAB 1 - your apps
    // =======================================================================

    /// <summary>
    /// Finds the apps on this computer.
    ///
    /// This reads several hundred shortcut files, which takes a second or two,
    /// so it runs on its own thread. Doing it on the screen thread would freeze
    /// the window while it worked.
    ///
    /// The thread is set to STA because reading a shortcut uses COM, an old
    /// part of Windows that expects to be called that way.
    /// </summary>
    private void StartScanningForApps()
    {
        AppsMessage.Text = "Looking for your apps...";
        AppsMessage.Visibility = Visibility.Visible;
        RescanButton.IsEnabled = false;

        Thread thread = new Thread(ScanOnBackgroundThread);
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ScanOnBackgroundThread()
    {
        List<InstalledApp> found;

        try
        {
            InstalledAppsScanner scanner = new InstalledAppsScanner(_host.Logger);
            found = scanner.Scan(_host.Store.GetAppsSeenBefore());
        }
        catch (Exception error)
        {
            _host.Logger.Error("Could not scan for installed apps.", error);
            found = new List<InstalledApp>();
        }

        // Back to the screen thread to show the result. Only the screen thread
        // is allowed to touch anything on screen.
        Dispatcher.Invoke(delegate
        {
            _allApps = found;
            RescanButton.IsEnabled = true;
            RefreshApps();
        });
    }

    private void OnRescanClicked(object sender, RoutedEventArgs e)
    {
        StartScanningForApps();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshApps();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        RefreshApps();
    }

    /// <summary>Fills the app list, honouring the search box and the filter.</summary>
    private void RefreshApps()
    {
        _isRefreshing = true;

        try
        {
            // Which programs are blocked right now? Work this out once rather
            // than once per app.
            Dictionary<string, bool> blockedFileNames = GetBlockedProgramNames();

            string search = SearchBox.Text == null ? "" : SearchBox.Text.Trim().ToLowerInvariant();
            bool blockedOnly = ShowBlockedOnlyBox.IsChecked == true;

            List<InstalledAppRow> rows = new List<InstalledAppRow>();

            foreach (InstalledApp app in _allApps)
            {
                string key = app.ExeFileName.ToLowerInvariant();
                bool isBlocked = blockedFileNames.ContainsKey(key);

                if (blockedOnly && !isBlocked)
                {
                    continue;
                }

                if (search.Length > 0 && !MatchesSearch(app, search))
                {
                    continue;
                }

                bool isBuiltIn = isBlocked && blockedFileNames[key];

                rows.Add(InstalledAppRow.FromApp(app, isBlocked, isBuiltIn));
            }

            AppsList.ItemsSource = rows;

            if (rows.Count == 0)
            {
                AppsMessage.Text = _allApps.Count == 0
                    ? "No apps found."
                    : "No app matches what you typed.";
                AppsMessage.Visibility = Visibility.Visible;
            }
            else
            {
                AppsMessage.Visibility = Visibility.Collapsed;
            }

            StatusText.Text =
                _allApps.Count + " app(s) found, " + blockedFileNames.Count + " blocked.";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static bool MatchesSearch(InstalledApp app, string search)
    {
        if (app.DisplayName != null && app.DisplayName.ToLowerInvariant().Contains(search))
        {
            return true;
        }

        return app.ExeFileName.ToLowerInvariant().Contains(search);
    }

    /// <summary>
    /// The program file names currently blocked, and whether each block is one
    /// TimeFold shipped with.
    ///
    /// Only rules that match on the program name are considered here. A rule
    /// about a folder or a window title cannot be shown as a tick against one
    /// app, which is exactly why those live on the other tab.
    /// </summary>
    private Dictionary<string, bool> GetBlockedProgramNames()
    {
        Dictionary<string, bool> blocked = new Dictionary<string, bool>();

        foreach (BlockRule rule in _host.BlockRules.GetAll())
        {
            if (rule.MatchType != BlockMatchType.ExeFileName)
            {
                continue;
            }

            if (!rule.IsEnabled)
            {
                continue;
            }

            blocked[rule.Pattern.ToLowerInvariant()] = rule.IsBuiltIn;
        }

        return blocked;
    }

    /// <summary>Ticking blocks an app. Unticking unblocks it.</summary>
    private void OnAppBlockToggled(object sender, RoutedEventArgs e)
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

        string exeFileName = box.Tag.ToString();
        bool shouldBlock = box.IsChecked == true;

        if (shouldBlock)
        {
            BlockApp(exeFileName);
        }
        else
        {
            UnblockApp(exeFileName);
        }

        RefreshApps();
        RefreshRules();
    }

    private void BlockApp(string exeFileName)
    {
        BlockRule existing = FindProgramNameRule(exeFileName);

        if (existing != null)
        {
            // The rule is there but switched off. Switch it back on rather than
            // adding a second one that says the same thing.
            _host.BlockRules.SetEnabled(existing.Id, true);
        }
        else
        {
            _host.BlockRules.Add(
                new BlockRule(BlockMatchType.ExeFileName, exeFileName, "Blocked from the app list"));
        }

        _host.BlockRuleProvider.Reload();

        StatusText.Text = exeFileName + " is blocked. Nothing from it will be recorded from now on.";
    }

    private void UnblockApp(string exeFileName)
    {
        BlockRule existing = FindProgramNameRule(exeFileName);

        if (existing == null)
        {
            return;
        }

        if (existing.IsBuiltIn)
        {
            // Unblocking a password manager is allowed - it is your computer -
            // but it should be a deliberate act, not an accidental click.
            MessageBoxResult answer = MessageBox.Show(
                this,
                exeFileName + " is blocked by TimeFold because it is a password manager " +
                "or a Windows sign-in screen.\n\n" +
                "Unblock it anyway? Its window titles will then be recorded.",
                "TimeFold",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            // Switch it off rather than delete it, so it can be switched back on.
            _host.BlockRules.SetEnabled(existing.Id, false);
        }
        else
        {
            _host.BlockRules.Delete(existing.Id);
        }

        _host.BlockRuleProvider.Reload();

        StatusText.Text = exeFileName + " is no longer blocked. It will be recorded from now on.";
    }

    private BlockRule FindProgramNameRule(string exeFileName)
    {
        foreach (BlockRule rule in _host.BlockRules.GetAll())
        {
            if (rule.MatchType != BlockMatchType.ExeFileName)
            {
                continue;
            }

            if (string.Equals(rule.Pattern, exeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    // =======================================================================
    //  TAB 2 - advanced rules
    // =======================================================================

    private void FillMatchTypeChoices()
    {
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.ExeFileName));
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.ExePathContains));
        MatchTypeBox.Items.Add(BlockRuleRow.DescribeMatchType(BlockMatchType.WindowTitleContains));

        // Folder first here: anything that can be done by program name is
        // easier to do on the other tab.
        MatchTypeBox.SelectedIndex = 1;
    }

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
                    "The program's file name, for example KeePass.exe. Usually easier to do " +
                    "on the \"Your apps\" tab.";
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

    private void OnPatternKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
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

        _host.BlockRules.Add(
            new BlockRule(GetSelectedMatchType(), pattern, "Added by you"));

        _host.BlockRuleProvider.Reload();

        PatternBox.Text = "";
        RefreshRules();
        RefreshApps();

        StatusText.Text = "Added. Nothing matching \"" + pattern + "\" will be recorded from now on.";
    }

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

        RefreshRules();
        RefreshApps();

        StatusText.Text = isEnabled
            ? "Rule switched on."
            : "Rule switched off. Anything it was blocking will now be recorded.";
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        BlockRuleRow selected = RulesList.SelectedItem as BlockRuleRow;

        if (selected == null)
        {
            MessageBox.Show(
                this,
                "Choose a rule on the \"Advanced rules\" tab first.\n\n" +
                "To unblock an app, untick it on the \"Your apps\" tab.",
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

        RefreshRules();
        RefreshApps();

        StatusText.Text = "Rule deleted.";
    }

    private void RefreshRules()
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
