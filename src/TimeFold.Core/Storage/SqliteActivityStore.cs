using Microsoft.Data.Sqlite;
using TimeFold.Core.Models;
using TimeFold.Core.Time;

namespace TimeFold.Core.Storage;

/// <summary>
/// The SQLite version of IActivityStore. The only class in TimeFold that writes SQL
/// about activity records.
/// </summary>
public class SqliteActivityStore : IActivityStore
{
    private readonly Database _database;

    public SqliteActivityStore(Database database)
    {
        _database = database;
    }

    public void Save(ActivityRecord record)
    {
        long now = AppClock.NowUtcMs();

        if (record.SavedAtUtcMs == 0)
        {
            record.SavedAtUtcMs = now;
        }

        record.UpdatedAtUtcMs = now;

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            // "ON CONFLICT ... DO UPDATE" means: insert it, but if a row with this
            // id already exists, update that row instead. This is what makes saving
            // the same record twice harmless.
            command.CommandText = @"
                INSERT INTO activity_records (
                    id, kind, source,
                    started_at_utc_ms, ended_at_utc_ms, tz_offset_minutes,
                    app_name, exe_path, window_title, details_json,
                    saved_at_utc_ms, updated_at_utc_ms, row_version
                )
                VALUES (
                    $id, $kind, $source,
                    $startedAt, $endedAt, $tzOffset,
                    $appName, $exePath, $windowTitle, $detailsJson,
                    $savedAt, $updatedAt, 1
                )
                ON CONFLICT(id) DO UPDATE SET
                    ended_at_utc_ms   = excluded.ended_at_utc_ms,
                    window_title      = excluded.window_title,
                    details_json      = excluded.details_json,
                    updated_at_utc_ms = excluded.updated_at_utc_ms,
                    row_version       = activity_records.row_version + 1;
            ";

            command.Parameters.AddWithValue("$id", record.Id);
            command.Parameters.AddWithValue("$kind", record.Kind);
            command.Parameters.AddWithValue("$source", record.Source);
            command.Parameters.AddWithValue("$startedAt", record.StartedAtUtcMs);
            command.Parameters.AddWithValue("$endedAt", ValueOrNull(record.EndedAtUtcMs));
            command.Parameters.AddWithValue("$tzOffset", record.TimeZoneOffsetMinutes);
            command.Parameters.AddWithValue("$appName", ValueOrNull(record.AppName));
            command.Parameters.AddWithValue("$exePath", ValueOrNull(record.ExePath));
            command.Parameters.AddWithValue("$windowTitle", ValueOrNull(record.WindowTitle));
            command.Parameters.AddWithValue("$detailsJson", ValueOrNull(record.DetailsJson));
            command.Parameters.AddWithValue("$savedAt", record.SavedAtUtcMs);
            command.Parameters.AddWithValue("$updatedAt", record.UpdatedAtUtcMs);

            command.ExecuteNonQuery();
        }
    }

    public List<ActivityRecord> GetRecordsBetween(long fromUtcMs, long toUtcMs)
    {
        List<ActivityRecord> results = new List<ActivityRecord>();

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT id, kind, source,
                       started_at_utc_ms, ended_at_utc_ms, tz_offset_minutes,
                       app_name, exe_path, window_title, details_json,
                       saved_at_utc_ms, updated_at_utc_ms, row_version
                FROM activity_records
                WHERE started_at_utc_ms >= $from
                  AND started_at_utc_ms <  $to
                ORDER BY started_at_utc_ms ASC;
            ";

            command.Parameters.AddWithValue("$from", fromUtcMs);
            command.Parameters.AddWithValue("$to", toUtcMs);

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    results.Add(ReadRecord(reader));
                }
            }
        }

        return results;
    }

    public long CountAllRecords()
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM activity_records;";
            object result = command.ExecuteScalar();
            return Convert.ToInt64(result);
        }
    }

    public long? GetLastSavedAtUtcMs()
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT MAX(saved_at_utc_ms) FROM activity_records;";
            object result = command.ExecuteScalar();

            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt64(result);
        }
    }

    /// <summary>Turns one database row into an ActivityRecord object.</summary>
    private static ActivityRecord ReadRecord(SqliteDataReader reader)
    {
        ActivityRecord record = new ActivityRecord();

        record.Id = reader.GetString(0);
        record.Kind = reader.GetString(1);
        record.Source = reader.GetString(2);
        record.StartedAtUtcMs = reader.GetInt64(3);
        record.EndedAtUtcMs = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4);
        record.TimeZoneOffsetMinutes = reader.GetInt32(5);
        record.AppName = reader.IsDBNull(6) ? null : reader.GetString(6);
        record.ExePath = reader.IsDBNull(7) ? null : reader.GetString(7);
        record.WindowTitle = reader.IsDBNull(8) ? null : reader.GetString(8);
        record.DetailsJson = reader.IsDBNull(9) ? null : reader.GetString(9);
        record.SavedAtUtcMs = reader.GetInt64(10);
        record.UpdatedAtUtcMs = reader.GetInt64(11);
        record.RowVersion = reader.GetInt64(12);

        return record;
    }

    public List<SystemIntegration.InstalledApp> GetAppsSeenBefore()
    {
        List<SystemIntegration.InstalledApp> apps = new List<SystemIntegration.InstalledApp>();

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            // One row per program, with the most recent name we saw for it.
            // MAX(started_at_utc_ms) also puts the apps you used most recently
            // at the top, which is almost always what you are looking for.
            command.CommandText = @"
                SELECT exe_path, app_name, MAX(started_at_utc_ms)
                FROM activity_records
                WHERE exe_path IS NOT NULL
                GROUP BY exe_path
                ORDER BY MAX(started_at_utc_ms) DESC;
            ";

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    SystemIntegration.InstalledApp app = new SystemIntegration.InstalledApp();

                    app.ExePath = reader.GetString(0);
                    app.DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    app.ExeFileName = Path.GetFileName(app.ExePath);
                    app.FoundIn = "Used recently";

                    apps.Add(app);
                }
            }
        }

        return apps;
    }

    public List<ActivityRecord> GetAllRecords()
    {
        List<ActivityRecord> records = new List<ActivityRecord>();

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT id, kind, source,
                       started_at_utc_ms, ended_at_utc_ms, tz_offset_minutes,
                       app_name, exe_path, window_title, details_json,
                       saved_at_utc_ms, updated_at_utc_ms, row_version
                FROM activity_records
                ORDER BY started_at_utc_ms ASC;
            ";

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    records.Add(ReadRecord(reader));
                }
            }
        }

        return records;
    }

    // ---- deleting ----------------------------------------------------------

    public int DeleteRecord(string id)
    {
        return RunDelete(
            "DELETE FROM activity_records WHERE id = $id;",
            delegate (SqliteCommand command)
            {
                command.Parameters.AddWithValue("$id", id);
            });
    }

    public int DeleteRecordsBetween(long fromUtcMs, long toUtcMs)
    {
        return RunDelete(
            @"DELETE FROM activity_records
              WHERE started_at_utc_ms >= $from AND started_at_utc_ms < $to;",
            delegate (SqliteCommand command)
            {
                command.Parameters.AddWithValue("$from", fromUtcMs);
                command.Parameters.AddWithValue("$to", toUtcMs);
            });
    }

    public int DeleteRecordsForApp(string exeFileName)
    {
        return RunDelete(
            AppMatchSql("DELETE FROM activity_records"),
            delegate (SqliteCommand command)
            {
                command.Parameters.AddWithValue("$suffix", "%\\" + exeFileName);
                command.Parameters.AddWithValue("$exact", exeFileName);
            });
    }

    public long CountRecordsForApp(string exeFileName)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = AppMatchSql("SELECT COUNT(*) FROM activity_records");
            command.Parameters.AddWithValue("$suffix", "%\\" + exeFileName);
            command.Parameters.AddWithValue("$exact", exeFileName);

            object result = command.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt64(result);
        }
    }

    public int DeleteAllRecords()
    {
        return RunDelete("DELETE FROM activity_records;", null);
    }

    /// <summary>
    /// Finds records belonging to one program file name.
    ///
    /// exe_path holds a full path like C:\Program Files\Chrome\chrome.exe, so we
    /// match on the end of it. LIKE in SQLite ignores upper and lower case for
    /// plain text, which is what we want - Windows does not care about case in
    /// file names either.
    ///
    /// The second half covers the odd case of a bare file name with no folder.
    /// </summary>
    private static string AppMatchSql(string prefix)
    {
        return prefix + " WHERE exe_path LIKE $suffix OR exe_path = $exact;";
    }

    private int RunDelete(string sql, Action<SqliteCommand> addParameters)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;

            if (addParameters != null)
            {
                addParameters(command);
            }

            return command.ExecuteNonQuery();
        }
    }

    /// <summary>SQLite needs DBNull, not a C# null.</summary>
    private static object ValueOrNull(object value)
    {
        if (value == null)
        {
            return DBNull.Value;
        }

        return value;
    }
}
