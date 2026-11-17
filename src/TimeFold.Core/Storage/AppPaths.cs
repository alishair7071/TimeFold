namespace TimeFold.Core.Storage;

/// <summary>
/// Every file path TimeFold uses is defined here, in one place.
///
/// Your data lives OUTSIDE the program folder, in:
///   C:\Users\&lt;you&gt;\AppData\Local\TimeFold\
///
/// Why there and not next to the .exe?
///   1. The program folder gets replaced when you update, your data must not.
///   2. It works without administrator rights.
///   3. Uninstalling the app can leave your history alone if you want to keep it.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "TimeFold";

    /// <summary>C:\Users\&lt;you&gt;\AppData\Local\TimeFold</summary>
    public static string DataFolder
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, AppFolderName);
        }
    }

    /// <summary>The SQLite database file. One file holds everything.</summary>
    public static string DatabaseFile
    {
        get { return Path.Combine(DataFolder, "timefold.db"); }
    }

    public static string LogsFolder
    {
        get { return Path.Combine(DataFolder, "logs"); }
    }

    /// <summary>Screenshot images. Not used until Phase 5.</summary>
    public static string ScreenshotsFolder
    {
        get { return Path.Combine(DataFolder, "screenshots"); }
    }

    /// <summary>Encrypted backups. Not used until Phase 9.</summary>
    public static string BackupsFolder
    {
        get { return Path.Combine(DataFolder, "backups"); }
    }

    /// <summary>Creates the folders if they do not exist yet. Safe to call every start.</summary>
    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
