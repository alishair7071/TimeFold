using TimeFold.Core.SystemIntegration;
using Xunit;

namespace TimeFold.Core.Tests.SystemIntegration;

/// <summary>
/// Tests for finding the apps on this computer.
///
/// These look at the real machine, so they check things that are true of every
/// Windows PC rather than anything specific to one. A test that asserted
/// "Chrome is installed" would fail on a machine without Chrome, which would
/// tell us nothing useful.
/// </summary>
public class InstalledAppsScannerTests
{
    [Fact]
    public void FindsSomeApps()
    {
        InstalledAppsScanner scanner = new InstalledAppsScanner(new NullLogger());

        List<InstalledApp> apps = scanner.Scan(null);

        Assert.NotEmpty(apps);
    }

    [Fact]
    public void AlwaysIncludesFileExplorer()
    {
        // File Explorer has no Start menu shortcut, so if this passes we know
        // the built-in list is doing its job. It is also the app most people
        // would look for first.
        InstalledAppsScanner scanner = new InstalledAppsScanner(new NullLogger());

        List<InstalledApp> apps = scanner.Scan(null);

        Assert.Contains(apps, a =>
            string.Equals(a.ExeFileName, "explorer.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryAppHasTheFieldsTheScreenNeeds()
    {
        InstalledAppsScanner scanner = new InstalledAppsScanner(new NullLogger());

        List<InstalledApp> apps = scanner.Scan(null);

        Assert.All(apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(app.ExeFileName));
            Assert.False(string.IsNullOrWhiteSpace(app.ExePath));
            Assert.False(string.IsNullOrWhiteSpace(app.FoundIn));
        });
    }

    [Fact]
    public void ListsEachProgramOnlyOnce()
    {
        // The same program is often found in several places - a Start menu
        // shortcut, the built-in list, and your own history. Showing it three
        // times would make the tick boxes disagree with each other.
        InstalledAppsScanner scanner = new InstalledAppsScanner(new NullLogger());

        List<InstalledApp> apps = scanner.Scan(null);

        HashSet<string> seen = new HashSet<string>();

        foreach (InstalledApp app in apps)
        {
            string key = app.ExeFileName.ToLowerInvariant();

            Assert.True(seen.Add(key), "Found " + key + " more than once.");
        }
    }

    [Fact]
    public void IncludesAppsFromYourHistoryThatAreNotInstalledNormally()
    {
        InstalledApp seenBefore = new InstalledApp();
        seenBefore.DisplayName = "Some Portable Tool";
        seenBefore.ExePath = "D:\\portable\\SomePortableTool.exe";
        seenBefore.ExeFileName = "SomePortableTool.exe";
        seenBefore.FoundIn = "Used recently";

        InstalledAppsScanner scanner = new InstalledAppsScanner(new NullLogger());

        List<InstalledApp> apps = scanner.Scan(new List<InstalledApp> { seenBefore });

        Assert.Contains(apps, a =>
            string.Equals(a.ExeFileName, "SomePortableTool.exe", StringComparison.OrdinalIgnoreCase));
    }

    private class NullLogger : Core.Logging.IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}
