namespace TimeFold.Core.Models;

/// <summary>
/// The kinds of record TimeFold can store.
///
/// We use plain text instead of an enum so that old databases keep working
/// when we add new kinds in later phases. An enum saved as a number would
/// break if we ever reordered it.
/// </summary>
public static class RecordKinds
{
    /// <summary>You used an application window. Phase 1.</summary>
    public const string Window = "window";

    /// <summary>You stopped using the computer. Phase 2.</summary>
    public const string Idle = "idle";

    /// <summary>Windows was locked. Phase 2.</summary>
    public const string Lock = "lock";

    /// <summary>You visited a website. Phase 4.</summary>
    public const string Website = "website";

    /// <summary>A screenshot was taken. Phase 5.</summary>
    public const string Screenshot = "screenshot";
}
