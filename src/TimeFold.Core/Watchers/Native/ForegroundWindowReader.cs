using System.Diagnostics;
using System.Text;

namespace TimeFold.Core.Watchers.Native;

/// <summary>
/// Asks Windows: which window is the user working in right now?
///
/// This is the only class that uses NativeMethods, so all the awkward
/// Windows-specific code sits in one place. If we ever change HOW we detect the
/// window (Phase 1 note: we may switch from checking every second to a Windows
/// event hook), only this class and WindowWatcher change.
/// </summary>
public class ForegroundWindowReader
{
    // Looking up a program's friendly name is slow-ish, and the answer never
    // changes for a given path. So we remember it after the first time.
    private readonly Dictionary<string, string> _appNameCache = new Dictionary<string, string>();

    public ForegroundWindowInfo Read()
    {
        try
        {
            IntPtr windowHandle = NativeMethods.GetForegroundWindow();

            if (windowHandle == IntPtr.Zero)
            {
                // Happens while the screen is locked, or between switching windows.
                return ForegroundWindowInfo.Invalid();
            }

            string title = ReadWindowTitle(windowHandle);

            uint processId;
            NativeMethods.GetWindowThreadProcessId(windowHandle, out processId);

            if (processId == 0)
            {
                return ForegroundWindowInfo.Invalid();
            }

            string exePath = ReadProcessExePath(processId);

            ForegroundWindowInfo info = new ForegroundWindowInfo();
            info.IsValid = true;
            info.WindowTitle = title;
            info.ExePath = exePath;
            info.AppName = GetFriendlyAppName(exePath, processId);

            return info;
        }
        catch
        {
            // Reading the foreground window must never crash the app.
            // An unreadable window simply becomes "nothing to record",
            // which the Privacy Gate then drops.
            return ForegroundWindowInfo.Invalid();
        }
    }

    private static string ReadWindowTitle(IntPtr windowHandle)
    {
        int length = NativeMethods.GetWindowTextLength(windowHandle);

        if (length <= 0)
        {
            return string.Empty;
        }

        // +1 leaves room for the invisible end-of-text marker Windows adds.
        StringBuilder buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(windowHandle, buffer, buffer.Capacity);

        return buffer.ToString();
    }

    private static string ReadProcessExePath(uint processId)
    {
        IntPtr processHandle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);

        if (processHandle == IntPtr.Zero)
        {
            // Normal for protected system programs. We return null, and the
            // Privacy Gate will then drop the record (rule: fail closed).
            return null;
        }

        try
        {
            uint capacity = 1024;
            StringBuilder buffer = new StringBuilder((int)capacity);

            bool success = NativeMethods.QueryFullProcessImageName(processHandle, 0, buffer, ref capacity);

            if (!success)
            {
                return null;
            }

            return buffer.ToString();
        }
        finally
        {
            // Always close the handle, even if something above failed.
            NativeMethods.CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// Turns "C:\...\chrome.exe" into "Google Chrome" when Windows knows that name,
    /// otherwise into "chrome".
    /// </summary>
    private string GetFriendlyAppName(string exePath, uint processId)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            return null;
        }

        if (_appNameCache.ContainsKey(exePath))
        {
            return _appNameCache[exePath];
        }

        string name = null;

        try
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);

            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                name = versionInfo.FileDescription.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
            {
                name = versionInfo.ProductName.Trim();
            }
        }
        catch
        {
            // Some programs have no version information. That is fine.
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(exePath);
        }

        _appNameCache[exePath] = name;
        return name;
    }
}
