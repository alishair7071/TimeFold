using System.Runtime.InteropServices;
using System.Text;

namespace TimeFold.Core.Watchers.Native;

/// <summary>
/// Direct calls into Windows itself.
///
/// These functions live inside Windows (user32.dll and kernel32.dll). C# cannot
/// call them normally, so we declare them here with [DllImport]. That is called
/// "P/Invoke" - platform invoke.
///
/// This file contains declarations ONLY. No logic. Everything that uses these
/// is in ForegroundWindowReader.cs, so that the awkward Windows details stay in
/// one small place.
/// </summary>
internal static class NativeMethods
{
    /// <summary>Gets a handle to the window you are currently working in.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>How many characters the window title has.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr windowHandle);

    /// <summary>Reads the window title into a text buffer.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    /// <summary>Finds which program owns a window.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    /// <summary>Opens a program so we can ask questions about it.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    /// <summary>Closes a handle from OpenProcess. Must always be called, or we leak.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    /// <summary>Gets the full path of a running program's .exe file.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        uint flags,
        StringBuilder exeName,
        ref uint size);

    /// <summary>
    /// The smallest permission that still lets us read a program's path.
    ///
    /// We deliberately ask for the least access possible. A bigger permission
    /// would fail on protected system programs and would be more than we need.
    /// </summary>
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}
