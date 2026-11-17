namespace TimeFold.Core.Privacy;

/// <summary>
/// The rules TimeFold ships with.
///
/// Since Phase 2.1 this list is no longer what the gate reads. It is the
/// starting contents of the block_rules table, written once into a new
/// database by BlockListSeeder. After that the table is yours: switch these
/// off, delete them, add your own.
///
/// The list stays deliberately short. It covers only the things that would be
/// indefensible to record by accident - password managers and the Windows
/// sign-in screens. Guessing at what else you consider private is your call to
/// make, not ours.
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
        BlockRule rule = new BlockRule(BlockMatchType.ExeFileName, exeFileName, note);

        // Marked so the screen can show which rules came from TimeFold and
        // which ones you added yourself.
        rule.IsBuiltIn = true;

        rules.Add(rule);
    }
}
