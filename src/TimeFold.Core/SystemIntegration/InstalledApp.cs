namespace TimeFold.Core.SystemIntegration;

/// <summary>
/// One program found on this computer.
///
/// This is not a complete list of every executable on the disk - that would be
/// tens of thousands of files, most of them helpers you have never heard of.
/// It is the list of programs a person would recognise: the ones with a Start
/// menu entry, plus the ones TimeFold has actually seen you using.
/// </summary>
public class InstalledApp
{
    /// <summary>The name a person would recognise, for example "Google Chrome".</summary>
    public string DisplayName { get; set; }

    /// <summary>The full path to the program, for example "C:\...\chrome.exe".</summary>
    public string ExePath { get; set; }

    /// <summary>
    /// Just the file name, for example "chrome.exe". This is what a block rule
    /// matches on, so it is the field that matters most.
    /// </summary>
    public string ExeFileName { get; set; }

    /// <summary>
    /// Where we found it: "Start menu", "Windows" or "Used recently".
    /// Shown on screen so you can tell why an app is in the list.
    /// </summary>
    public string FoundIn { get; set; }
}
