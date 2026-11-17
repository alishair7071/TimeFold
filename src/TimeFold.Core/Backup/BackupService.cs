using System.Text.Json;
using TimeFold.Core.Logging;
using TimeFold.Core.Models;
using TimeFold.Core.Storage;
using TimeFold.Core.Time;

namespace TimeFold.Core.Backup;

/// <summary>
/// Writes your whole history to a file, and reads it back.
///
/// Why this exists in Phase 2 rather than later: the promise of TimeFold is
/// that your data is yours. That promise is empty unless you can take it out.
/// Being able to export is also what makes deleting safe to offer - you can
/// keep a copy first.
///
/// The format is JSON: plain text you can open in Notepad, read with your own
/// eyes, and load into any other program. A format only TimeFold can read
/// would be a way of keeping you here, which is the opposite of the point.
///
/// Restoring is safe to repeat. Every record carries the id it was saved with,
/// and saving an existing id updates that row instead of adding a second one,
/// so importing the same file twice gives you the same history, not double.
/// </summary>
public class BackupService
{
    private readonly IActivityStore _store;
    private readonly IAppLogger _logger;

    public BackupService(IActivityStore store, IAppLogger logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Writes every record to a JSON file. Returns how many were written.
    /// </summary>
    public int ExportToFile(string filePath)
    {
        List<ActivityRecord> records = _store.GetAllRecords();

        BackupFile file = new BackupFile();
        file.FormatVersion = 1;
        file.ExportedAtUtcMs = AppClock.NowUtcMs();
        file.RecordCount = records.Count;
        file.Records = records;

        JsonSerializerOptions options = new JsonSerializerOptions();
        options.WriteIndented = true;

        string json = JsonSerializer.Serialize(file, options);

        // Write to a temporary file first, then move it into place. If the
        // computer loses power halfway through, you are left with your previous
        // export rather than a half-written file that looks complete.
        string temporaryPath = filePath + ".writing";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, filePath, true);

        _logger.Info("Exported " + records.Count + " records.");

        return records.Count;
    }

    /// <summary>
    /// Reads a JSON file back into the database. Returns how many records were
    /// restored. Existing records with the same id are updated, not duplicated.
    /// </summary>
    public int ImportFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);

        BackupFile file = JsonSerializer.Deserialize<BackupFile>(json);

        if (file == null || file.Records == null)
        {
            throw new InvalidDataException(
                "This file does not look like a TimeFold export.");
        }

        if (file.FormatVersion > 1)
        {
            throw new InvalidDataException(
                "This file was written by a newer version of TimeFold (format " +
                file.FormatVersion + "). Update TimeFold and try again.");
        }

        int restored = 0;

        foreach (ActivityRecord record in file.Records)
        {
            // A record with no id cannot be saved safely: without one, saving
            // it twice would create two rows. Skip it rather than guess.
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                continue;
            }

            _store.Save(record);
            restored++;
        }

        _logger.Info("Imported " + restored + " records.");

        return restored;
    }

    /// <summary>
    /// Suggests a file name with today's date in it, so repeated exports do not
    /// quietly overwrite each other.
    /// </summary>
    public static string SuggestFileName()
    {
        return "timefold-export-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
    }
}

/// <summary>
/// The shape of an export file.
///
/// FormatVersion is here from the first version. Without it, a future change to
/// the format would have no way of telling old files from new ones, and the only
/// options would be to guess or to refuse everything.
/// </summary>
public class BackupFile
{
    public int FormatVersion { get; set; }
    public long ExportedAtUtcMs { get; set; }
    public int RecordCount { get; set; }
    public List<ActivityRecord> Records { get; set; }
}
