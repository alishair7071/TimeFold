using Microsoft.Data.Sqlite;

namespace TimeFold.Core.Storage.Migrations;

/// <summary>
/// One change to the shape of the database.
///
/// Why we have these from day one, even with only a single migration:
/// once TimeFold is installed and recording your real history, we can no longer
/// delete the database to change a table. Every future change has to be applied
/// on top of data that already exists. Migrations are how that is done safely.
///
/// Rules:
///   - Never change a migration that has already shipped. Add a new one instead.
///   - Version numbers only go up.
/// </summary>
public interface IMigration
{
    /// <summary>Order number. 1, 2, 3... Must be unique.</summary>
    int Version { get; }

    /// <summary>Short description, written into the database for reference.</summary>
    string Name { get; }

    /// <summary>Apply the change. Runs inside a transaction.</summary>
    void Up(SqliteConnection connection, SqliteTransaction transaction);
}
