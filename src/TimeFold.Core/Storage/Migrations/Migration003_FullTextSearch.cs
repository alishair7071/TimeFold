using Microsoft.Data.Sqlite;

namespace TimeFold.Core.Storage.Migrations;

/// <summary>
/// Phase 3 - the search index.
///
/// Searching with LIKE '%word%' means reading every row and every character of
/// every title. That is fine for a week of data and painful after a year, and
/// it cannot rank results.
///
/// FTS5 is SQLite's own full-text search. It builds an index of the words in
/// your titles, so finding every record containing "jaeger" is a lookup rather
/// than a scan.
///
/// The important part is that it lives INSIDE the same database file. Deleting
/// a record and removing it from search happen in the same transaction, so they
/// cannot come apart. A separate search system could drift, and in an app about
/// privacy a deleted item that still appears in search is not a small bug.
///
/// Keeping the two in step is done with triggers - little pieces of SQL that
/// SQLite runs automatically whenever activity_records changes. No C# code has
/// to remember to update the index, which means no C# code can forget to.
/// </summary>
public class Migration003_FullTextSearch : IMigration
{
    public int Version
    {
        get { return 3; }
    }

    public string Name
    {
        get { return "Create the full-text search index"; }
    }

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // -------------------------------------------------------------------
        // The index itself.
        //
        // Only the two fields worth searching by words: what you were looking
        // at, and which app it was in. Not the path, not the times - those are
        // searched with ordinary SQL.
        //
        // This is a plain FTS5 table rather than one linked to activity_records,
        // which means it keeps its own copy of the text. That costs a little
        // disk - text only, so a few MB a year - and buys a delete trigger that
        // is one obvious line instead of a fragile one.
        //
        // rowid is the link back: every row here has the same rowid as its
        // record in activity_records.
        //
        // tokenize porter: "meeting" also finds "meetings", "ran" finds "run".
        // unicode61 remove_diacritics: "café" and "cafe" find each other.
        // -------------------------------------------------------------------
        Execute(connection, transaction, @"
            CREATE VIRTUAL TABLE activity_search USING fts5 (
                window_title,
                app_name,
                tokenize = 'porter unicode61 remove_diacritics 2'
            );");

        // -------------------------------------------------------------------
        // Keep the index in step with the records, automatically.
        // -------------------------------------------------------------------
        Execute(connection, transaction, @"
            CREATE TRIGGER activity_records_after_insert
            AFTER INSERT ON activity_records
            BEGIN
                INSERT INTO activity_search (rowid, window_title, app_name)
                VALUES (new.rowid, new.window_title, new.app_name);
            END;");

        Execute(connection, transaction, @"
            CREATE TRIGGER activity_records_after_delete
            AFTER DELETE ON activity_records
            BEGIN
                DELETE FROM activity_search WHERE rowid = old.rowid;
            END;");

        // An open record is saved again every 30 seconds as its title and end
        // time change, so updates are the common case, not the rare one.
        Execute(connection, transaction, @"
            CREATE TRIGGER activity_records_after_update
            AFTER UPDATE ON activity_records
            BEGIN
                DELETE FROM activity_search WHERE rowid = old.rowid;

                INSERT INTO activity_search (rowid, window_title, app_name)
                VALUES (new.rowid, new.window_title, new.app_name);
            END;");

        // -------------------------------------------------------------------
        // Index what is already there.
        //
        // Triggers only fire on changes from now on. Without this, everything
        // recorded before today would be invisible to search - and you would
        // have no way of telling, because search would simply return nothing
        // rather than reporting an error.
        // -------------------------------------------------------------------
        Execute(connection, transaction, @"
            INSERT INTO activity_search (rowid, window_title, app_name)
            SELECT rowid, window_title, app_name FROM activity_records;");
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
