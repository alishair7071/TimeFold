using Microsoft.Win32;
using TimeFold.Core.Logging;

namespace TimeFold.Core.SystemIntegration;

/// <summary>
/// Makes TimeFold start automatically when you log in to Windows.
///
/// It does this by writing one value into the registry under:
///   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
///
/// HKEY_CURRENT_USER means "this user only", so no administrator rights are
/// needed. The user can remove it at any time from Task Manager > Startup.
/// </summary>
public class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TimeFold";

    private readonly IAppLogger _logger;

    public AutoStartService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null)
                {
                    return false;
                }

                object value = key.GetValue(ValueName);
                return value != null;
            }
        }
        catch (Exception error)
        {
            _logger.Error("Could not read the autostart setting.", error);
            return false;
        }
    }

    public void Enable(string exePath)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }

                // Quotes matter: without them a path containing a space breaks.
                key.SetValue(ValueName, "\"" + exePath + "\"");
            }

            _logger.Info("Autostart enabled.");
        }
        catch (Exception error)
        {
            _logger.Error("Could not enable autostart.", error);
        }
    }

    public void Disable()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }

                key.DeleteValue(ValueName, false);
            }

            _logger.Info("Autostart disabled.");
        }
        catch (Exception error)
        {
            _logger.Error("Could not disable autostart.", error);
        }
    }
}
