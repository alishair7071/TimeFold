using TimeFold.Core.Models;

namespace TimeFold.Core.Storage;

/// <summary>
/// Saving and finding activity records.
///
/// This interface is a promise: it is the ONLY way the rest of TimeFold is
/// allowed to touch stored data. Nothing else writes SQL.
///
/// That is what makes the database replaceable. If we ever move away from
/// SQLite, we write a new class that implements this interface and nothing
/// else in the app changes.
///
/// Phase 2 will add: Delete, DeleteRange, DeleteByApp, ExportAll.
/// Phase 3 will add: Search.
/// </summary>
public interface IActivityStore
{
    /// <summary>
    /// Saves a record. If a record with the same Id already exists it is updated.
    ///
    /// This is why Ids are built from the data instead of being random: saving the
    /// same thing twice (after a crash, or during a backup restore) gives one row,
    /// not two.
    /// </summary>
    void Save(ActivityRecord record);

    /// <summary>
    /// All records that started inside this time window, oldest first.
    /// Times are UTC milliseconds.
    /// </summary>
    List<ActivityRecord> GetRecordsBetween(long fromUtcMs, long toUtcMs);

    /// <summary>How many records are stored in total. Shown in the UI.</summary>
    long CountAllRecords();

    /// <summary>When the newest record was saved, or null if there are none.</summary>
    long? GetLastSavedAtUtcMs();

    /// <summary>
    /// Every distinct program that appears anywhere in your history.
    ///
    /// Used by the blocked apps screen, so that something you clearly use shows
    /// up in the list even if it has no Start menu shortcut.
    /// </summary>
    List<SystemIntegration.InstalledApp> GetAppsSeenBefore();
}
