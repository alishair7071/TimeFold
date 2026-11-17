using Microsoft.Data.Sqlite;
using TimeFold.Core.Storage;
using TimeFold.Core.Time;

namespace TimeFold.Core.Settings;

/// <summary>
/// Reads and writes app settings. They live in the settings table of the same
/// database, so there is only ever one file to back up or delete.
///
/// Phase 2 adds many more settings here (retention, blocked apps, screenshot
/// options). The pattern stays the same: one named key, one value.
/// </summary>
public class SettingsService
{
    private const string KeyPaused = "recording.paused";
    private const string KeyDeviceId = "device.id";

    private readonly Database _database;

    public SettingsService(Database database)
    {
        _database = database;
    }

    // ------------------------------------------------------------------
    // Pause
    // ------------------------------------------------------------------

    /// <summary>True when the user has paused recording.</summary>
    public bool IsPaused()
    {
        return GetValue(KeyPaused) == "true";
    }

    public void SetPaused(bool paused)
    {
        SetValue(KeyPaused, paused ? "true" : "false");
    }

    // ------------------------------------------------------------------
    // Device id
    // ------------------------------------------------------------------

    /// <summary>
    /// A random id for this installation, created once and then reused.
    ///
    /// It is part of how record ids are built, so that in Phase 9 two computers
    /// backing up to the same place cannot produce clashing ids.
    /// It identifies the installation, not you - it contains nothing personal.
    /// </summary>
    public string GetOrCreateDeviceId()
    {
        string existing = GetValue(KeyDeviceId);

        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        string newId = Guid.NewGuid().ToString("N");
        SetValue(KeyDeviceId, newId);
        return newId;
    }

    // ------------------------------------------------------------------
    // The two methods everything above is built on
    // ------------------------------------------------------------------

    public string GetValue(string key)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT value FROM settings WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);

            object result = command.ExecuteScalar();

            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return result.ToString();
        }
    }

    public void SetValue(string key, string value)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                INSERT INTO settings (key, value, updated_at_utc_ms)
                VALUES ($key, $value, $updatedAt)
                ON CONFLICT(key) DO UPDATE SET
                    value             = excluded.value,
                    updated_at_utc_ms = excluded.updated_at_utc_ms;
            ";

            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value == null ? (object)DBNull.Value : value);
            command.Parameters.AddWithValue("$updatedAt", AppClock.NowUtcMs());

            command.ExecuteNonQuery();
        }
    }
}
