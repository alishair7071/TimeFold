namespace TimeFold.Core.Models;

/// <summary>What the Privacy Gate decided to do with a record.</summary>
public enum GateDecision
{
    /// <summary>Save it.</summary>
    Allow,

    /// <summary>Throw it away. It is never written to disk.</summary>
    Drop
}

/// <summary>
/// The answer from the Privacy Gate.
///
/// Note that ReasonCode is a short code like "blocked_app", never the data itself.
/// We are allowed to log THAT something was dropped. We are never allowed to log
/// WHAT was dropped - otherwise the private data just moves into the log file.
/// </summary>
public class GateVerdict
{
    public GateDecision Decision { get; private set; }

    /// <summary>A short code explaining the decision. Safe to write in a log.</summary>
    public string ReasonCode { get; private set; }

    private GateVerdict(GateDecision decision, string reasonCode)
    {
        Decision = decision;
        ReasonCode = reasonCode;
    }

    public static GateVerdict Allow()
    {
        return new GateVerdict(GateDecision.Allow, "allowed");
    }

    public static GateVerdict Drop(string reasonCode)
    {
        return new GateVerdict(GateDecision.Drop, reasonCode);
    }

    public bool IsAllowed
    {
        get { return Decision == GateDecision.Allow; }
    }
}
