using Microsoft.Data.Sqlite;
using TimeFold.Core.Logging;
using TimeFold.Core.Time;

namespace TimeFold.Core.Storage.Migrations;

/// <summary>
/// Applies any migrations that have not been applied yet.
/// Runs once at startup, before anything else touches the database.
///
/// How it works:
///   1. Make sure the schema_migrations table exists.
///   2. Read which version numbers are already applied.
///   3. Apply the missing ones, in order, each inside its own transaction.
///
/// If a migration fails, its transaction is rolled back, so the database is
/// never left half-changed.
/// </summary>
public class MigrationRunner
{
    private readonly Database _database;
    private readonly IAppLogger _logger;

    public MigrationRunner(Database database, IAppLogger logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// All migrations, in order. Add new ones to the end of this list.
    /// </summary>
    private static List<IMigration> GetAllMigrations()
    {
        return new List<IMigration>
        {
            new Migration001_CreateTables(),
            new Migration002_CreateBlockRules()
            // Phase 3 will add: new Migration003_FullTextSearch()
        };
    }

    public void RunPendingMigrations()
    {
        using (SqliteConnection connection = _database.OpenConnection())
        {
            CreateMigrationsTableIfMissing(connection);

            List<int> appliedVersions = GetAppliedVersions(connection);
            List<IMigration> allMigrations = GetAllMigrations();

            foreach (IMigration migration in allMigrations.OrderBy(m => m.Version))
            {
                if (appliedVersions.Contains(migration.Version))
                {
                    continue;
                }

                ApplyMigration(connection, migration);
            }
        }
    }

    private void ApplyMigration(SqliteConnection connection, IMigration migration)
    {
        _logger.Info("Applying database migration " + migration.Version + ": " + migration.Name);

        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            try
            {
                migration.Up(connection, transaction);

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "INSERT INTO schema_migrations (version, name, applied_at_utc_ms) " +
                        "VALUES ($version, $name, $appliedAt);";
                    command.Parameters.AddWithValue("$version", migration.Version);
                    command.Parameters.AddWithValue("$name", migration.Name);
                    command.Parameters.AddWithValue("$appliedAt", AppClock.NowUtcMs());
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                _logger.Info("Migration " + migration.Version + " applied.");
            }
            catch (Exception error)
            {
                transaction.Rollback();
                _logger.Error("Migration " + migration.Version + " failed and was rolled back.", error);
                throw;
            }
        }
    }

    private static void CreateMigrationsTableIfMissing(SqliteConnection connection)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version           INTEGER NOT NULL PRIMARY KEY,
                    name              TEXT    NOT NULL,
                    applied_at_utc_ms INTEGER NOT NULL
                );
            ";
            command.ExecuteNonQuery();
        }
    }

    private static List<int> GetAppliedVersions(SqliteConnection connection)
    {
        List<int> versions = new List<int>();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version FROM schema_migrations;";

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    versions.Add(reader.GetInt32(0));
                }
            }
        }

        return versions;
    }
}
