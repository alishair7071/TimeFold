using System.Diagnostics;
using TimeFold.Core.Logging;

namespace TimeFold.Core.SystemIntegration;

/// <summary>
/// Finds the programs on this computer that a person would recognise.
///
/// Why not just list every .exe on the disk? Because a normal Windows machine
/// has tens of thousands of them, nearly all helpers, installers and background
/// services. A list like that is unusable, and picking the wrong thing out of it
/// is how you end up blocking something important by accident.
///
/// So we look in three places instead:
///
///   1. The Start menu. Every normal program puts a shortcut there, and the
///      shortcut carries both the friendly name and the real program path.
///   2. A short list of Windows programs that have no Start menu shortcut but
///      that everybody uses - File Explorer above all.
///   3. Programs TimeFold has already recorded you using. This catches anything
///      the first two miss, and by definition those are apps you actually use.
///
/// Reading a shortcut needs a small piece of old Windows plumbing (COM), which
/// is explained where it is used.
/// </summary>
public class InstalledAppsScanner
{
    private readonly IAppLogger _logger;

    public InstalledAppsScanner(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds the list, newest-looking name first.
    ///
    /// <paramref name="appsAlreadySeen"/> is the list of programs from your own
    /// history - pass null if you do not have it.
    ///
    /// This reads several hundred files, so it takes a second or two. Call it
    /// from a background thread, not from the screen thread.
    /// </summary>
    public List<InstalledApp> Scan(List<InstalledApp> appsAlreadySeen)
    {
        // Keyed by lower-case file name, so the same program found twice in two
        // different places appears once.
        Dictionary<string, InstalledApp> found = new Dictionary<string, InstalledApp>();

        AddWindowsBuiltInApps(found);
        AddStartMenuApps(found);
        AddRunningApps(found);
        AddAppsAlreadySeen(found, appsAlreadySeen);

        List<InstalledApp> result = new List<InstalledApp>(found.Values);
        result.Sort(delegate (InstalledApp a, InstalledApp b)
        {
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    // ---- 1. the Start menu -------------------------------------------------

    private void AddStartMenuApps(Dictionary<string, InstalledApp> found)
    {
        List<string> startMenuFolders = new List<string>();

        // Programs installed for everybody...
        startMenuFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));

        // ...and programs installed only for you.
        startMenuFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));

        foreach (string folder in startMenuFolders)
        {
            AddShortcutsIn(found, folder);
        }
    }

    private void AddShortcutsIn(Dictionary<string, InstalledApp> found, string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        string[] shortcutFiles;

        try
        {
            shortcutFiles = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories);
        }
        catch (Exception error)
        {
            // A folder we are not allowed to read is not a reason to give up on
            // the whole list.
            _logger.Error("Could not read a Start menu folder.", error);
            return;
        }

        foreach (string shortcutFile in shortcutFiles)
        {
            TryAddOneShortcut(found, shortcutFile);
        }
    }

    private void TryAddOneShortcut(Dictionary<string, InstalledApp> found, string shortcutFile)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(shortcutFile);

            if (LooksLikeSomethingToSkip(name))
            {
                return;
            }

            string exePath = ReadShortcutTarget(shortcutFile);

            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!File.Exists(exePath))
            {
                return;
            }

            Add(found, name, exePath, "Start menu");
        }
        catch
        {
            // One unreadable shortcut must not stop the scan.
        }
    }

    /// <summary>
    /// Reads where a .lnk shortcut points.
    ///
    /// A shortcut is a small binary file, not text, so we cannot just read it.
    /// Windows exposes a helper for this called WScript.Shell, which has been
    /// part of Windows since the 1990s. It is reached through COM, an old way
    /// of calling into Windows components.
    ///
    /// "dynamic" here means: do not check these method names when compiling,
    /// look them up at the moment we call them. That is necessary because COM
    /// objects have no C# type for the compiler to check against. It is the
    /// one place in TimeFold where we give up compile-time checking, which is
    /// why everything here is wrapped in a try/catch.
    /// </summary>
    private static string ReadShortcutTarget(string shortcutFile)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");

        if (shellType == null)
        {
            return null;
        }

        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(shortcutFile);

        string target = shortcut.TargetPath;

        return target;
    }

    /// <summary>
    /// Skips shortcuts that are not really programs you use: uninstallers,
    /// help files, web links and readme documents.
    /// </summary>
    private static bool LooksLikeSomethingToSkip(string name)
    {
        string lower = name.ToLowerInvariant();

        string[] wordsToSkip = new string[]
        {
            "uninstall", "unins", "remove ",
            "readme", "release notes", "license", "licence",
            "help", "documentation", "manual",
            "website", "home page", "visit "
        };

        foreach (string word in wordsToSkip)
        {
            if (lower.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    // ---- 2. Windows programs with no shortcut ------------------------------

    /// <summary>
    /// A few Windows programs have no Start menu shortcut but are used by
    /// everybody. File Explorer is the obvious one: it would be very strange
    /// for it to be missing from a list of "your apps".
    /// </summary>
    private static void AddWindowsBuiltInApps(Dictionary<string, InstalledApp> found)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        AddIfFileExists(found, "File Explorer", Path.Combine(windows, "explorer.exe"));
        AddIfFileExists(found, "Notepad", Path.Combine(system32, "notepad.exe"));
        AddIfFileExists(found, "Command Prompt", Path.Combine(system32, "cmd.exe"));
        AddIfFileExists(found, "Windows PowerShell",
            Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"));
        AddIfFileExists(found, "Task Manager", Path.Combine(system32, "Taskmgr.exe"));
    }

    private static void AddIfFileExists(
        Dictionary<string, InstalledApp> found,
        string displayName,
        string exePath)
    {
        if (File.Exists(exePath))
        {
            Add(found, displayName, exePath, "Windows");
        }
    }

    // ---- 3. programs running right now -------------------------------------

    /// <summary>
    /// Adds the programs running at this moment.
    ///
    /// This covers a gap in the Start menu list. Some shortcuts do not point at
    /// the program that actually runs: the Office 2016 shortcut for Word points
    /// at wordicon.exe, while the window you work in belongs to winword.exe.
    /// Blocking the shortcut target would do nothing, and you would have no way
    /// of knowing.
    ///
    /// Looking at what is running finds the real program file. Open Word once
    /// and winword.exe appears in the list correctly.
    /// </summary>
    private void AddRunningApps(Dictionary<string, InstalledApp> found)
    {
        Process[] running;

        try
        {
            running = Process.GetProcesses();
        }
        catch (Exception error)
        {
            _logger.Error("Could not list running programs.", error);
            return;
        }

        foreach (Process process in running)
        {
            try
            {
                // Only programs with a window on screen.
                //
                // This is the filter that matters. A normal machine runs around
                // two hundred processes, nearly all of them invisible helpers -
                // background services, build tools, parts of Docker. None of
                // them can ever appear in your timeline, because TimeFold only
                // records the window you are working in. Listing them would
                // bury the twenty apps you actually use.
                //
                // Having a window is exactly the test for "could this show up
                // in my history?", which is the only reason to block something.
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                // Windows refuses this for protected and system processes. That
                // is fine - those are not apps you would want in this list.
                string exePath = process.MainModule.FileName;

                if (string.IsNullOrEmpty(exePath))
                {
                    continue;
                }

                Add(found, null, exePath, "Running now");
            }
            catch
            {
                // Not allowed to ask about this one. Skip it quietly - this
                // happens for dozens of Windows processes on every machine.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    // ---- 4. apps from your own history -------------------------------------

    private static void AddAppsAlreadySeen(
        Dictionary<string, InstalledApp> found,
        List<InstalledApp> appsAlreadySeen)
    {
        if (appsAlreadySeen == null)
        {
            return;
        }

        foreach (InstalledApp app in appsAlreadySeen)
        {
            if (string.IsNullOrEmpty(app.ExePath))
            {
                continue;
            }

            Add(found, app.DisplayName, app.ExePath, "Used recently");
        }
    }

    // ---- shared ------------------------------------------------------------

    private static void Add(
        Dictionary<string, InstalledApp> found,
        string displayName,
        string exePath,
        string foundIn)
    {
        string exeFileName = Path.GetFileName(exePath);

        if (string.IsNullOrEmpty(exeFileName))
        {
            return;
        }

        string key = exeFileName.ToLowerInvariant();

        // Already have it. The first place we looked wins, because the order of
        // AddWindowsBuiltInApps / AddStartMenuApps / AddAppsAlreadySeen is from
        // most reliable name to least.
        if (found.ContainsKey(key))
        {
            return;
        }

        InstalledApp app = new InstalledApp();

        app.DisplayName = BestName(displayName, exePath);
        app.ExePath = exePath;
        app.ExeFileName = exeFileName;
        app.FoundIn = foundIn;

        found[key] = app;
    }

    /// <summary>
    /// Works out what to call a program.
    ///
    /// The Start menu shortcut name wins, because that is the name you already
    /// know the app by - it is what you see when you search the Start menu.
    ///
    /// We tried the other way round first, asking the program what it calls
    /// itself, and it went badly: every Office shortcut reported itself as
    /// "Microsoft Office 2016 component", so Word, Excel, PowerPoint and Access
    /// all appeared under the same unusable name.
    ///
    /// The program's own description is still the fallback, for apps found in
    /// your history rather than in the Start menu, where no shortcut name exists.
    /// </summary>
    private static string BestName(string shortcutName, string exePath)
    {
        if (!string.IsNullOrWhiteSpace(shortcutName))
        {
            return shortcutName.Trim();
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);

            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }
        }
        catch
        {
            // Fall through to the file name.
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }
}
