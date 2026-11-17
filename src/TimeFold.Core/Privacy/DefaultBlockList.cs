namespace TimeFold.Core.Privacy;

/// <summary>
/// Apps that TimeFold never records, straight out of the box.
///
/// Phase 1 keeps this list short and in code on purpose. The point is only that
/// the very first version can never record a password manager by accident.
/// Phase 2 moves these into the database and lets you add your own.
/// </summary>
public static class DefaultBlockList
{
    public static List<BlockRule> Create()
    {
        List<BlockRule> rules = new List<BlockRule>();

        // ---- Password managers -------------------------------------------
        AddExe(rules, "KeePass.exe", "Password manager");
        AddExe(rules, "KeePassXC.exe", "Password manager");
        AddExe(rules, "1Password.exe", "Password manager");
        AddExe(rules, "Bitwarden.exe", "Password manager");
        AddExe(rules, "LastPass.exe", "Password manager");
        AddExe(rules, "Dashlane.exe", "Password manager");
        AddExe(rules, "NordPass.exe", "Password manager");
        AddExe(rules, "Enpass.exe", "Password manager");
        AddExe(rules, "RoboForm.exe", "Password manager");
        AddExe(rules, "ProtonPass.exe", "Password manager");

        // ---- TimeFold itself ---------------------------------------------
        // Watching yourself watching yourself is not useful history. Without
        // this, simply reading your own timeline creates records about reading
        // your own timeline.
        AddExe(rules, "TimeFold.exe", "TimeFold does not record itself");

        // ---- Windows security screens ------------------------------------
        AddExe(rules, "LogonUI.exe", "Windows lock and sign-in screen");
        AddExe(rules, "CredentialUIBroker.exe", "Windows credential prompt");
        AddExe(rules, "consent.exe", "Windows administrator prompt");

        return rules;
    }

    private static void AddExe(List<BlockRule> rules, string exeFileName, string note)
    {
        rules.Add(new BlockRule(BlockMatchType.ExeFileName, exeFileName, note));
    }
}
