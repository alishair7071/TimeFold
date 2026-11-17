using TimeFold.Core.SystemIntegration;

namespace TimeFold.App.Views;

/// <summary>
/// One app as it appears in the blocked apps list.
///
/// Same separation as everywhere else: InstalledApp is what was found on the
/// computer, InstalledAppRow is how it is shown, including whether it is
/// currently blocked - which is not a property of the app at all, but of your
/// rules.
/// </summary>
public class InstalledAppRow
{
    public string DisplayName { get; set; }
    public string ExeFileName { get; set; }
    public string ExePath { get; set; }
    public string FoundIn { get; set; }
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Plain words for what is happening, so you never have to work it out
    /// from a tick box alone.
    /// </summary>
    public string StatusText { get; set; }

    public static InstalledAppRow FromApp(InstalledApp app, bool isBlocked, bool isBuiltInBlock)
    {
        InstalledAppRow row = new InstalledAppRow();

        row.DisplayName = app.DisplayName;
        row.ExeFileName = app.ExeFileName;
        row.ExePath = app.ExePath;
        row.FoundIn = app.FoundIn;
        row.IsBlocked = isBlocked;

        if (!isBlocked)
        {
            row.StatusText = "Recording";
        }
        else if (isBuiltInBlock)
        {
            row.StatusText = "Blocked by TimeFold";
        }
        else
        {
            row.StatusText = "Blocked by you";
        }

        return row;
    }
}
