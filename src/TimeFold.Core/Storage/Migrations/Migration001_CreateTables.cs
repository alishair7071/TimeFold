using Microsoft.Data.Sqlite;

namespace TimeFold.Core.Storage.Migrations;

/// <summary>
/// The first version of the database: the activity table and the settings table.
/// </summary>
public class Migration001_CreateTables : IMigration
{
    public int Version
    {
        get { return 1; }
    }

    public string Name
    {
        get { return "Create activity_records and settings tables"; }
    }

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // ---------------------------------------------------------------
        // activity_records - the main table. Everything TimeFold records
        // goes here, in every phase.
        // ---------------------------------------------------------------
        Execute(connection, transaction, @"
            CREATE TABLE activity_records (
                id                   TEXT    NOT NULL PRIMARY KEY,
                kind                 TEXT    NOT NULL,
                source               TEXT    NOT NULL,
                started_at_utc_ms    INTEGER NOT NULL,
                ended_at_utc_ms      INTEGER NULL,
                tz_offset_minutes    INTEGER NOT NULL,
                app_name             TEXT    NULL,
                exe_path             TEXT    NULL,
                window_title         TEXT    NULL,
                details_json         TEXT    NULL,
                saved_at_utc_ms      INTEGER NOT NULL,
                updated_at_utc_ms    INTEGER NOT NULL,
                row_version          INTEGER NOT NULL
            );
        ");

        // Almost every question starts with 'between these two times',
        // so this index is the important one.
        Execute(connection, transaction, @"
            CREATE INDEX ix_activity_started_at
                ON activity_records (started_at_utc_ms);
        ");

        // For 'show me everything I did in Chrome'.
        Execute(connection, transaction, @"
            CREATE INDEX ix_activity_app_started
                ON activity_records (app_name, started_at_utc_ms);
        ");

        // For 'show me only websites' once Phase 4 exists.
        Execute(connection, transaction, @"
            CREATE INDEX ix_activity_kind_started
                ON activity_records (kind, started_at_utc_ms);
        ");

        // ---------------------------------------------------------------
        // settings - simple name/value pairs, for example paused = true.
        // ---------------------------------------------------------------
        Execute(connection, transaction, @"
            CREATE TABLE settings (
                key               TEXT    NOT NULL PRIMARY KEY,
                value             TEXT    NULL,
                updated_at_utc_ms INTEGER NOT NULL
            );
        ");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
