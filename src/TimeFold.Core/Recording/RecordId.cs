using System.Security.Cryptography;
using System.Text;

namespace TimeFold.Core.Recording;

/// <summary>
/// Builds the id for a record.
///
/// The id is calculated FROM the record's own data, not generated randomly.
/// The same activity always produces the same id.
///
/// Why this matters:
///   - A record is saved several times while it is still going on (every 30
///     seconds). All those saves must update one row, not create many.
///   - If the app crashes and re-reports something, we get one row, not two.
///   - In Phase 9, restoring a backup cannot duplicate what is already there.
///
/// Adding this later would have been painful, so it is here from day one.
/// </summary>
public static class RecordId
{
    public static string Create(
        string deviceId,
        string kind,
        string source,
        long startedAtUtcMs,
        string exePath,
        string windowTitle)
    {
        string raw = string.Join("|",
            deviceId ?? "",
            kind ?? "",
            source ?? "",
            startedAtUtcMs.ToString(),
            exePath ?? "",
            windowTitle ?? "");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

        // 32 hex characters is far more than enough to never clash,
        // and short enough to read in a database tool.
        return Convert.ToHexString(hash).Substring(0, 32).ToLowerInvariant();
    }
}
