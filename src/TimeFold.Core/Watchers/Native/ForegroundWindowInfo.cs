namespace TimeFold.Core.Watchers.Native;

/// <summary>
/// What we could see about the window you are working in right now.
/// </summary>
public class ForegroundWindowInfo
{
    /// <summary>False when Windows gave us nothing useful (for example during a screen lock).</summary>
    public bool IsValid { get; set; }

    /// <summary>The window title, for example "PHASES.md - TimeFold - Visual Studio Code".</summary>
    public string WindowTitle { get; set; }

    /// <summary>Full path of the program, for example "C:\Program Files\...\Code.exe".</summary>
    public string ExePath { get; set; }

    /// <summary>
    /// A friendly name for the program, for example "Visual Studio Code".
    /// Falls back to the file name when Windows has no friendly name for it.
    /// </summary>
    public string AppName { get; set; }

    public static ForegroundWindowInfo Invalid()
    {
        return new ForegroundWindowInfo { IsValid = false };
    }

    // Note: this class used to build the "has it changed?" key. It no longer
    // does, because that key must be built from the CLEANED title (see
    // WindowTitleCleaner) and cleaning titles is the watcher's job, not the
    // job of something that only reports what Windows said.
}
