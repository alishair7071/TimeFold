using Microsoft.Data.Sqlite;

namespace TimeFold.Core.Storage.Migrations;

/// <summary>
/// Phase 2.1 - block rules move out of C# code and into the database.
///
/// In Phase 1 the list of apps never to record was a fixed list written in
/// DefaultBlockList.cs. That was fine for proving the idea, but it means you
/// cannot block your own bank app without me rebuilding the program. Rules are
/// your data, not our code, so they belong in your database.
/// </summary>
public class Migration002_CreateBlockRules : IMigration
{
    public int Version
    {
        get { return 2; }
    }

    public string Name
    {
        get { return "Create block_rules table"; }
    }

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // -------------------------------------------------------------------
        // block_rules - one row per "never record this".
        //
        // match_type is stored as TEXT, not as a number, for the same reason
        // record kinds are: a number in an old database would silently mean
        // something different if the order of the enum ever changed. Text
        // cannot drift like that, and you can read the table by eye.
        //
        // is_built_in marks the rules TimeFold ships with (password managers,
        // the Windows sign-in screen). You can switch them off, but we remember
        // that they came from us so a later version can add new ones without
        // resurrecting the ones you deliberately turned off.
        //
        // updated_at_utc_ms and row_version are here from the start for the
        // same reason activity_records has them: Phase 9 sync needs to know
        // which side is newer.
        // -------------------------------------------------------------------
        Execute(connection, transaction, @"
            CREATE TABLE block_rules (
                id                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                match_type           TEXT    NOT NULL,
                pattern              TEXT    NOT NULL,
                note                 TEXT    NULL,
                is_enabled           INTEGER NOT NULL DEFAULT 1,
                is_built_in          INTEGER NOT NULL DEFAULT 0,
                created_at_utc_ms    INTEGER NOT NULL,
                updated_at_utc_ms    INTEGER NOT NULL,
                row_version          INTEGER NOT NULL DEFAULT 1
            );");

        // Two rules that say the same thing are a bug, not a feature: the
        // second one can never do anything the first did not already do.
        // The database refuses them rather than relying on us to remember.
        Execute(connection, transaction, @"
            CREATE UNIQUE INDEX ux_block_rules_match
                ON block_rules (match_type, pattern);");

        // The gate loads only the switched-on rules, every time it reloads.
        Execute(connection, transaction, @"
            CREATE INDEX ix_block_rules_enabled
                ON block_rules (is_enabled);");
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
