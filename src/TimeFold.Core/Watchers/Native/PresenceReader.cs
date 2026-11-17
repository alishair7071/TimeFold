using TimeFold.Core.Watchers.Native;

namespace TimeFold.Core.Watchers.Native;

/// <summary>What the computer looks like right now.</summary>
public enum PresenceState
{
    /// <summary>You are using the computer.</summary>
    Active,

    /// <summary>The computer is unlocked but you have not touched it for a while.</summary>
    Idle,

    /// <summary>The screen is locked.</summary>
    Locked
}

/// <summary>
/// Answers one question: are you actually at the computer?
///
/// This matters because a window stays in the foreground whether you are there
/// or not. Leave Chrome open and go to lunch, and without this TimeFold would
/// record two hours of "using Chrome". Your history would say you worked all
/// afternoon when you were not in the building.
///
/// Two separate signals, because they mean different things:
///
///   Locked - you locked the screen, or Windows did. A clear, exact signal.
///   Idle   - you have not touched the keyboard or mouse for a while. A guess,
///            and sometimes a wrong one: watching a video is not idle to you,
///            but it is to Windows.
///
/// Because idle is a guess, TimeFold records it as its own kind of entry rather
/// than throwing the time away. Then your timeline can say "away from 1pm to
/// 2pm" - which is itself useful to remember - instead of silently pretending
/// nothing happened.
/// </summary>
public class PresenceReader
{
    /// <summary>
    /// How long without keyboard or mouse before we call it idle.
    ///
    /// Five minutes is a compromise. Shorter, and reading a long page counts as
    /// being away. Longer, and a lunch break looks like work.
    /// </summary>
    public int IdleAfterSeconds { get; set; }

    public PresenceReader()
    {
        IdleAfterSeconds = 300;
    }

    public PresenceState Read()
    {
        // Locked is checked first, because a locked screen is also idle and the
        // more exact answer should win.
        if (IsScreenLocked())
        {
            return PresenceState.Locked;
        }

        if (GetSecondsSinceLastInput() >= IdleAfterSeconds)
        {
            return PresenceState.Idle;
        }

        return PresenceState.Active;
    }

    /// <summary>
    /// How many seconds since you last touched the keyboard or mouse.
    /// Returns 0 if Windows will not say, which errs towards "you are here" -
    /// the same direction as the privacy gate: when unsure, do the less
    /// surprising thing.
    /// </summary>
    public int GetSecondsSinceLastInput()
    {
        try
        {
            NativeMethods.LASTINPUTINFO info = new NativeMethods.LASTINPUTINFO();
            info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);

            if (!NativeMethods.GetLastInputInfo(ref info))
            {
                return 0;
            }

            // Both numbers count milliseconds since Windows started. Subtracting
            // them with unsigned arithmetic is deliberate: the counter wraps
            // round to zero after about 49 days of uptime, and unsigned
            // subtraction still gives the right answer across that wrap.
            uint nowSinceBoot = (uint)Environment.TickCount;
            uint millisecondsIdle = nowSinceBoot - info.TimeOfLastInput;

            return (int)(millisecondsIdle / 1000);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// True when the screen is locked.
    ///
    /// There is no Windows function called "IsScreenLocked". The way everyone
    /// does this is to try to open the desktop that currently has input: when
    /// the screen is locked Windows has switched to a separate secure desktop
    /// that ordinary programs may not open, so the call fails.
    ///
    /// A failure for any other reason also reads as locked. That is the safe
    /// direction - we would rather record a lock that did not happen than
    /// record activity that did not happen.
    /// </summary>
    public bool IsScreenLocked()
    {
        IntPtr desktop = IntPtr.Zero;

        try
        {
            desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_SWITCHDESKTOP);

            return desktop == IntPtr.Zero;
        }
        catch
        {
            return true;
        }
        finally
        {
            if (desktop != IntPtr.Zero)
            {
                NativeMethods.CloseDesktop(desktop);
            }
        }
    }
}
