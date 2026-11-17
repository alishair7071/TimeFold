using Microsoft.Data.Sqlite;
using TimeFold.Core.Storage;
using TimeFold.Core.Time;

namespace TimeFold.Core.Privacy;

/// <summary>
/// The SQLite version of IBlockRuleStore. The only class in TimeFold that
/// writes SQL about block rules.
/// </summary>
public class SqliteBlockRuleStore : IBlockRuleStore
{
    private readonly Database _database;

    public SqliteBlockRuleStore(Database database)
    {
        _database = database;
    }

    public List<BlockRule> GetAll()
    {
        return Query("SELECT " + Columns + " FROM block_rules ORDER BY is_built_in, id DESC;");
    }

    public List<BlockRule> GetEnabled()
    {
        return Query("SELECT " + Columns + " FROM block_rules WHERE is_enabled = 1 ORDER BY id;");
    }

    public long Add(BlockRule rule)
    {
        long now = AppClock.NowUtcMs();

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            // "ON CONFLICT ... DO NOTHING" relies on the unique index created in
            // migration 2. Adding a rule you already have is not an error - it
            // simply changes nothing.
            command.CommandText = @"
                INSERT INTO block_rules (
                    match_type, pattern, note,
                    is_enabled, is_built_in,
                    created_at_utc_ms, updated_at_utc_ms, row_version
                )
                VALUES (
                    $matchType, $pattern, $note,
                    $isEnabled, $isBuiltIn,
                    $now, $now, 1
                )
                ON CONFLICT(match_type, pattern) DO NOTHING;
            ";

            command.Parameters.AddWithValue("$matchType", rule.MatchType.ToString());
            command.Parameters.AddWithValue("$pattern", rule.Pattern);
            command.Parameters.AddWithValue("$note", AsDatabaseValue(rule.Note));
            command.Parameters.AddWithValue("$isEnabled", rule.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$isBuiltIn", rule.IsBuiltIn ? 1 : 0);
            command.Parameters.AddWithValue("$now", now);

            command.ExecuteNonQuery();
        }

        // Whether we inserted it or it was already there, the caller wants the
        // row number of the rule that is now in the table.
        return FindId(rule.MatchType, rule.Pattern);
    }

    public void SetEnabled(long id, bool isEnabled)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE block_rules
                SET is_enabled        = $isEnabled,
                    updated_at_utc_ms = $now,
                    row_version       = row_version + 1
                WHERE id = $id;
            ";

            command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$now", AppClock.NowUtcMs());
            command.Parameters.AddWithValue("$id", id);

            command.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM block_rules WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public long CountAll()
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM block_rules;";
            object result = command.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt64(result);
        }
    }

    // ---- the plumbing ------------------------------------------------------

    private const string Columns =
        "id, match_type, pattern, note, is_enabled, is_built_in";

    private List<BlockRule> Query(string sql)
    {
        List<BlockRule> rules = new List<BlockRule>();

        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rules.Add(ReadOneRule(reader));
                }
            }
        }

        return rules;
    }

    private static BlockRule ReadOneRule(SqliteDataReader reader)
    {
        BlockRule rule = new BlockRule();

        rule.Id = reader.GetInt64(0);
        rule.MatchType = ParseMatchType(reader.GetString(1));
        rule.Pattern = reader.GetString(2);
        rule.Note = reader.IsDBNull(3) ? null : reader.GetString(3);
        rule.IsEnabled = reader.GetInt64(4) == 1;
        rule.IsBuiltIn = reader.GetInt64(5) == 1;

        return rule;
    }

    /// <summary>
    /// Turns the stored text back into a match type.
    ///
    /// If the text is not one we recognise - an old database, or a newer
    /// version of TimeFold that knew a type this one does not - we fall back to
    /// ExeFileName rather than crashing. That is the safe direction: the rule
    /// still blocks something rather than silently disappearing.
    /// </summary>
    private static BlockMatchType ParseMatchType(string text)
    {
        BlockMatchType parsed;

        if (Enum.TryParse(text, true, out parsed))
        {
            return parsed;
        }

        return BlockMatchType.ExeFileName;
    }

    private long FindId(BlockMatchType matchType, string pattern)
    {
        using (SqliteConnection connection = _database.OpenConnection())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT id FROM block_rules
                WHERE match_type = $matchType AND pattern = $pattern;
            ";

            command.Parameters.AddWithValue("$matchType", matchType.ToString());
            command.Parameters.AddWithValue("$pattern", pattern);

            object result = command.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt64(result);
        }
    }

    private static object AsDatabaseValue(string text)
    {
        return text == null ? (object)DBNull.Value : text;
    }
}
