using TimeFold.Core.Models;
using TimeFold.Core.Privacy;
using TimeFold.Core.Watchers.Native;
using Xunit;

namespace TimeFold.Core.Tests.Watchers;

/// <summary>
/// Tests for noticing that you are away from the computer.
///
/// Whether the machine running these tests is idle or locked is not something
/// a test can control, so these check the things that must be true either way.
/// </summary>
public class PresenceTests
{
    [Fact]
    public void IdleTimeIsNeverNegative()
    {
        // The counter Windows uses wraps round to zero after about 49 days of
        // uptime. Subtracting across that wrap with ordinary signed arithmetic
        // would give a large negative number, and TimeFold would decide you had
        // been active for the last seven weeks.
        PresenceReader reader = new PresenceReader();

        Assert.True(reader.GetSecondsSinceLastInput() >= 0);
    }

    [Fact]
    public void AnswersWithOneOfTheThreeStates()
    {
        PresenceReader reader = new PresenceReader();

        PresenceState state = reader.Read();

        Assert.True(
            state == PresenceState.Active ||
            state == PresenceState.Idle ||
            state == PresenceState.Locked);
    }

    [Fact]
    public void AVeryLongIdleThresholdMeansYouAreAlwaysHere()
    {
        // Unless the screen is locked, which must still win.
        PresenceReader reader = new PresenceReader();
        reader.IdleAfterSeconds = int.MaxValue;

        PresenceState state = reader.Read();

        Assert.True(state == PresenceState.Active || state == PresenceState.Locked);
    }

    [Fact]
    public void AwayRecordsSurviveThePrivacyGate()
    {
        // An away record genuinely has no program behind it, and the gate drops
        // anything with no program path. So away records carry a stand-in path.
        // If that ever stopped working, being away would silently vanish from
        // your history and the day would look like a gap.
        PrivacyGate gate = new PrivacyGate(DefaultBlockList.Create());

        ActivityRecord away = new ActivityRecord();
        away.Kind = RecordKinds.Idle;
        away.Source = "presence-watcher";
        away.ExePath = "timefold:absence";
        away.WindowTitle = "Away";
        away.AppName = "Away";

        Assert.True(gate.Check(away, false).IsAllowed);
    }

    [Fact]
    public void AwayRecordsAreStillDroppedWhilePaused()
    {
        PrivacyGate gate = new PrivacyGate(DefaultBlockList.Create());

        ActivityRecord away = new ActivityRecord();
        away.Kind = RecordKinds.Lock;
        away.ExePath = "timefold:absence";
        away.WindowTitle = "Screen locked";

        Assert.False(gate.Check(away, true).IsAllowed);
    }

    [Fact]
    public void TheAbsenceStandInPathCannotBeHitByANormalBlockRule()
    {
        // "timefold:absence" is not a Windows path, so no rule a person would
        // reasonably write could match it by accident.
        BlockRule rule = new BlockRule(BlockMatchType.ExeFileName, "chrome.exe", "test");

        Assert.False(rule.Matches("timefold:absence", "Away"));
    }
}
