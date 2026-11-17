using Microsoft.Data.Sqlite;

namespace TimeFold.Core.Storage;

/// <summary>
/// Opens connections to the SQLite file and prepares the database on startup.
///
/// Everything that reads or writes the database asks this class for a connection.
/// Nothing else in TimeFold knows where the file is.
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public string FilePath { get; private set; }

    public Database(string databaseFilePath)
    {
        FilePath = databaseFilePath;

        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
        builder.DataSource = databaseFilePath;
        builder.Mode = SqliteOpenMode.ReadWriteCreate;
        // Pooling keeps connections ready, so opening one is cheap.
        builder.Pooling = true;

        _connectionString = builder.ToString();
    }

    /// <summary>
    /// Opens a connection. The caller must dispose it (use a "using" statement).
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Called once at startup, before anything else uses the database.
    /// </summary>
    public void Prepare()
    {
        using (SqliteConnection connection = OpenConnection())
        {
            // WAL ("write ahead log") means reading and writing can happen at the
            // same time, and an unexpected shutdown does not corrupt the file.
            // This is the single most important setting for an app that writes
            // continuously in the background.
            Execute(connection, "PRAGMA journal_mode = WAL;");

            // NORMAL is the right balance here: safe against app crashes,
            // and far fewer disk writes than FULL.
            Execute(connection, "PRAGMA synchronous = NORMAL;");

            // Wait up to 5 seconds if another connection is busy, instead of failing.
            Execute(connection, "PRAGMA busy_timeout = 5000;");
        }
    }

    /// <summary>How big the database file is, in bytes. Shown in the UI.</summary>
    public long GetFileSizeBytes()
    {
        try
        {
            FileInfo info = new FileInfo(FilePath);
            if (info.Exists)
            {
                return info.Length;
            }
        }
        catch
        {
            // Not important enough to fail over.
        }

        return 0;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
