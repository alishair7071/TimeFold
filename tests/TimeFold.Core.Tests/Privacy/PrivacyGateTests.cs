using TimeFold.Core.Models;
using TimeFold.Core.Privacy;
using Xunit;

namespace TimeFold.Core.Tests.Privacy;

/// <summary>
/// Tests for the privacy gate.
///
/// The gate is the one class in TimeFold where being wrong is not an
/// inconvenience but a broken promise: a record that should never have been
/// written cannot be un-written. So it gets tested properly.
///
/// These tests need no database, no window and no Windows. The gate is a plain
/// function, which is exactly why it was written that way.
/// </summary>
public class PrivacyGateTests
{
    // ---- helpers -----------------------------------------------------------

    /// <summary>Builds an ordinary, allowable record that tests can then spoil.</summary>
    private static ActivityRecord MakeRecord(string exePath, string windowTitle)
    {
        ActivityRecord record = new ActivityRecord();

        record.Id = "test-id";
        record.Kind = RecordKinds.Window;
        record.Source = "window-watcher";
        record.StartedAtUtcMs = 1700000000000;
        record.EndedAtUtcMs = 1700000010000;
        record.ExePath = exePath;
        record.WindowTitle = windowTitle;
        record.AppName = "Test App";

        return record;
    }

    private static PrivacyGate GateWithDefaultRules()
    {
        return new PrivacyGate(DefaultBlockList.Create());
    }

    // ---- the normal case ---------------------------------------------------

    [Fact]
    public void AllowsAnOrdinaryRecord()
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\Program Files\\Notepad\\notepad.exe", "notes.txt - Notepad"),
            isPaused: false);

        Assert.True(verdict.IsAllowed);
    }

    // ---- pause -------------------------------------------------------------

    [Fact]
    public void DropsEverythingWhilePaused()
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\Program Files\\Notepad\\notepad.exe", "notes.txt - Notepad"),
            isPaused: true);

        Assert.False(verdict.IsAllowed);
        Assert.Equal("paused", verdict.ReasonCode);
    }

    [Fact]
    public void PauseBeatsEverythingElse()
    {
        // Even a record that would otherwise be perfectly fine is dropped.
        // Pause means pause.
        PrivacyGate gate = new PrivacyGate(new List<BlockRule>());

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\app.exe", "Something harmless"),
            isPaused: true);

        Assert.False(verdict.IsAllowed);
    }

    // ---- failing closed ----------------------------------------------------

    [Fact]
    public void DropsANullRecord()
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(null, isPaused: false);

        Assert.False(verdict.IsAllowed);
        Assert.Equal("empty_record", verdict.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DropsRecordsWhenTheProgramIsUnknown(string exePath)
    {
        // This is the most important test in the file.
        //
        // If we do not know which program a window belongs to, we cannot check
        // it against the block list. An unchecked record might be a password
        // manager. So "I do not know" must mean "do not save".
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord(exePath, "Some window title"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
        Assert.Equal("unknown_program", verdict.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DropsRecordsWithNoWindowTitle(string windowTitle)
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\app.exe", windowTitle),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
        Assert.Equal("empty_title", verdict.ReasonCode);
    }

    // ---- the block list ----------------------------------------------------

    [Theory]
    [InlineData("C:\\Program Files\\KeePass\\KeePass.exe")]
    [InlineData("C:\\Program Files\\KeePassXC\\KeePassXC.exe")]
    [InlineData("C:\\Program Files\\1Password\\1Password.exe")]
    [InlineData("C:\\Program Files\\Bitwarden\\Bitwarden.exe")]
    [InlineData("C:\\Program Files\\LastPass\\LastPass.exe")]
    [InlineData("C:\\Program Files\\Dashlane\\Dashlane.exe")]
    [InlineData("C:\\Program Files\\NordPass\\NordPass.exe")]
    [InlineData("C:\\Program Files\\Enpass\\Enpass.exe")]
    [InlineData("C:\\Program Files\\RoboForm\\RoboForm.exe")]
    [InlineData("C:\\Program Files\\ProtonPass\\ProtonPass.exe")]
    public void DropsEveryPasswordManagerInTheDefaultList(string exePath)
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord(exePath, "My Vault"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
        Assert.Equal("blocked_rule", verdict.ReasonCode);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\LogonUI.exe")]
    [InlineData("C:\\Windows\\System32\\CredentialUIBroker.exe")]
    [InlineData("C:\\Windows\\System32\\consent.exe")]
    public void DropsWindowsSecurityScreens(string exePath)
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord(exePath, "Sign in"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Fact]
    public void DropsTimeFoldItself()
    {
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\Users\\someone\\AppData\\Local\\TimeFold\\app\\TimeFold.exe", "TimeFold - Today"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Theory]
    [InlineData("C:\\Program Files\\KeePass\\KEEPASS.EXE")]
    [InlineData("C:\\Program Files\\KeePass\\keepass.exe")]
    [InlineData("C:\\Program Files\\KeePass\\KeePass.Exe")]
    public void BlockingIgnoresUpperAndLowerCase(string exePath)
    {
        // Windows does not care about the case of a filename, so neither may we.
        // If it did, renaming the file would defeat the block.
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord(exePath, "My Vault"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Fact]
    public void BlocksNoMatterWhereTheProgramIsInstalled()
    {
        // The rule matches the file name, not the full path, so moving the
        // program somewhere unusual does not get past the gate.
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("D:\\portable-apps\\stuff\\KeePass.exe", "My Vault"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Fact]
    public void DoesNotBlockAProgramWhoseNameMerelyContainsABlockedName()
    {
        // "NotKeePass.exe" is not "KeePass.exe". Over-blocking silently loses
        // real history, so the match has to be the whole file name.
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\tools\\NotKeePass.exe", "Just a tool"),
            isPaused: false);

        Assert.True(verdict.IsAllowed);
    }

    // ---- custom rules ------------------------------------------------------

    [Fact]
    public void BlocksOnWordsInTheWindowTitle()
    {
        List<BlockRule> rules = new List<BlockRule>
        {
            new BlockRule(BlockMatchType.WindowTitleContains, "Incognito", "Private browsing")
        };

        PrivacyGate gate = new PrivacyGate(rules);

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\chrome.exe", "Something - Google Chrome (Incognito)"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Fact]
    public void BlocksOnPartOfThePath()
    {
        List<BlockRule> rules = new List<BlockRule>
        {
            new BlockRule(BlockMatchType.ExePathContains, "\\Banking\\", "Anything in the banking folder")
        };

        PrivacyGate gate = new PrivacyGate(rules);

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\Apps\\Banking\\MyBank.exe", "Accounts"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
    }

    [Fact]
    public void WorksWithNoRulesAtAll()
    {
        // An empty list must not crash, and must not block everything either.
        // The cast says which constructor we mean: there is also one that takes
        // a BlockRuleProvider, and plain null could be either.
        PrivacyGate gate = new PrivacyGate((List<BlockRule>)null);

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\app.exe", "A window"),
            isPaused: false);

        Assert.True(verdict.IsAllowed);
    }

    // ---- what the gate must never leak ------------------------------------

    [Fact]
    public void TheReasonNeverContainsTheBlockedData()
    {
        // Reason codes get written to the log file. If the reason contained the
        // window title, blocking a password manager would write that title into
        // a plain text log - the exact thing we were preventing.
        PrivacyGate gate = GateWithDefaultRules();

        GateVerdict verdict = gate.Check(
            MakeRecord("C:\\Program Files\\KeePass\\KeePass.exe", "SECRET-BANK-LOGIN"),
            isPaused: false);

        Assert.False(verdict.IsAllowed);
        Assert.DoesNotContain("SECRET", verdict.ReasonCode);
        Assert.DoesNotContain("KeePass", verdict.ReasonCode);
    }
}
