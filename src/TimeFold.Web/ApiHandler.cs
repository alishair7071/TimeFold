using System.Net;
using System.Text;
using System.Text.Json;
using TimeFold.Core.Backup;
using TimeFold.Core.Logging;
using TimeFold.Core.Models;
using TimeFold.Core.Privacy;
using TimeFold.Core.Recording;
using TimeFold.Core.Settings;
using TimeFold.Core.Storage;
using TimeFold.Core.SystemIntegration;
using TimeFold.Core.Time;

namespace TimeFold.Web;

/// <summary>
/// Everything the web page can ask for.
///
/// This is the only place where the web side touches TimeFold.Core. It is the
/// same role the old MainWindow had: translate between what is stored and what
/// is shown. The page never sees a UTC millisecond or a SHA-256 id it did not
/// need; it gets times already formatted for reading.
///
/// One rule worth keeping: nothing here writes SQL, calls Windows, or decides
/// what is private. It asks Core, the same way the window used to.
/// </summary>
public class ApiHandler
{
    private readonly IActivityStore _store;
    private readonly IBlockRuleStore _blockRules;
    private readonly BlockRuleProvider _blockRuleProvider;
    private readonly RecordingService _recording;
    private readonly SettingsService _settings;
    private readonly Database _database;
    private readonly IAppLogger _logger;

    /// <summary>
    /// The list of apps, found once and kept. Scanning reads several hundred
    /// shortcut files, which is too slow to do on every page load.
    /// </summary>
    private List<InstalledApp> _installedApps;

    public ApiHandler(
        IActivityStore store,
        IBlockRuleStore blockRules,
        BlockRuleProvider blockRuleProvider,
        RecordingService recording,
        SettingsService settings,
        Database database,
        IAppLogger logger)
    {
        _store = store;
        _blockRules = blockRules;
        _blockRuleProvider = blockRuleProvider;
        _recording = recording;
        _settings = settings;
        _database = database;
        _logger = logger;
    }

    public void Handle(HttpListenerContext context, string path)
    {
        string method = context.Request.HttpMethod;

        switch (path.ToLowerInvariant())
        {
            case "/api/state":
                SendJson(context, GetState());
                return;

            case "/api/day":
                SendJson(context, GetDay(context.Request.QueryString["date"]));
                return;

            case "/api/search":
                SendJson(context, GetSearch(context.Request.QueryString["q"]));
                return;

            case "/api/days":
                SendJson(context, GetDaysWithRecords());
                return;

            case "/api/theme":
                SendJson(context, SetTheme(ReadBody(context)));
                return;

            case "/api/pause":
                SendJson(context, SetPaused(ReadBody(context)));
                return;

            case "/api/apps":
                SendJson(context, GetApps(context.Request.QueryString["rescan"] == "1"));
                return;

            case "/api/apps/block":
                SendJson(context, SetAppBlocked(ReadBody(context)));
                return;

            case "/api/rules":
                if (method == "POST")
                {
                    SendJson(context, AddRule(ReadBody(context)));
                }
                else
                {
                    SendJson(context, GetRules());
                }
                return;

            case "/api/rules/enabled":
                SendJson(context, SetRuleEnabled(ReadBody(context)));
                return;

            case "/api/rules/delete":
                SendJson(context, DeleteRule(ReadBody(context)));
                return;

            case "/api/records/delete":
                SendJson(context, DeleteRecord(ReadBody(context)));
                return;

            case "/api/records/delete-app":
                SendJson(context, DeleteApp(ReadBody(context)));
                return;

            case "/api/records/delete-day":
                SendJson(context, DeleteDay(ReadBody(context)));
                return;

            case "/api/records/delete-all":
                SendJson(context, DeleteEverything());
                return;

            case "/api/export":
                SendExportFile(context);
                return;

            case "/api/import":
                SendJson(context, Import(ReadBody(context)));
                return;

            default:
                context.Response.StatusCode = 404;
                SendJson(context, new { error = "No such thing." });
                return;
        }
    }

    // =======================================================================
    //  Reading
    // =======================================================================

    private object GetState()
    {
        long? firstUtcMs;
        long? lastUtcMs;
        _store.GetRecordedDateRange(out firstUtcMs, out lastUtcMs);

        return new
        {
            paused = _recording.IsPaused,
            theme = _settings.GetTheme(),
            savedThisSession = _recording.SavedCount,
            droppedThisSession = _recording.DroppedCount,
            totalRecords = _store.CountAllRecords(),
            databaseBytes = _database.GetFileSizeBytes(),
            databasePath = _database.FilePath,
            firstDay = firstUtcMs == null ? null : FormatDay(firstUtcMs.Value),
            today = DateTime.Today.ToString("yyyy-MM-dd")
        };
    }

    private object GetDay(string date)
    {
        DateTime day = ParseDay(date);

        long fromUtcMs;
        long toUtcMs;
        AppClock.GetLocalDayRangeUtcMs(day, out fromUtcMs, out toUtcMs);

        List<ActivityRecord> records = _store.GetRecordsBetween(fromUtcMs, toUtcMs);

        // Newest first - almost always what you want to see.
        records.Reverse();

        return new
        {
            day = day.ToString("yyyy-MM-dd"),
            entries = ToEntries(records, false),
            totalSeconds = TotalSeconds(records)
        };
    }

    private object GetSearch(string typedText)
    {
        List<ActivityRecord> records = _store.Search(typedText, 500);

        return new
        {
            query = typedText,
            // Search results come from any day, so each one carries its date.
            entries = ToEntries(records, true),
            totalSeconds = TotalSeconds(records)
        };
    }

    private object GetDaysWithRecords()
    {
        List<DateTime> days = _store.GetDaysWithRecords();
        List<string> text = new List<string>();

        foreach (DateTime day in days)
        {
            text.Add(day.ToString("yyyy-MM-dd"));
        }

        return new { days = text };
    }

    /// <summary>
    /// Turns stored records into something the page can display directly.
    ///
    /// The page is given readable times, not UTC milliseconds. Working out what
    /// "1763312688000 with offset 300" means is this side's job - it is the
    /// side that knows the record's own timezone, which is not necessarily the
    /// one the browser is in.
    /// </summary>
    private static List<object> ToEntries(List<ActivityRecord> records, bool includeTheDate)
    {
        List<object> entries = new List<object>();

        foreach (ActivityRecord record in records)
        {
            DateTime startLocal = AppClock.ToLocalTime(
                record.StartedAtUtcMs,
                record.TimeZoneOffsetMinutes);

            long endUtcMs = record.EndedAtUtcMs ?? record.StartedAtUtcMs;

            DateTime endLocal = AppClock.ToLocalTime(endUtcMs, record.TimeZoneOffsetMinutes);

            long seconds = (endUtcMs - record.StartedAtUtcMs) / 1000;

            DateTime hour = new DateTime(
                startLocal.Year, startLocal.Month, startLocal.Day, startLocal.Hour, 0, 0);

            entries.Add(new
            {
                id = record.Id,
                kind = record.Kind,
                appName = record.AppName,
                exeFileName = string.IsNullOrEmpty(record.ExePath)
                    ? null
                    : Path.GetFileName(record.ExePath),
                windowTitle = record.WindowTitle,
                startText = startLocal.ToString("HH:mm"),
                endText = endLocal.ToString("HH:mm"),
                seconds = seconds,
                durationText = FormatDuration(seconds),
                group = includeTheDate
                    ? hour.ToString("ddd d MMM, HH:00")
                    : hour.ToString("HH:00"),
                dayText = startLocal.ToString("yyyy-MM-dd")
            });
        }

        return entries;
    }

    private static long TotalSeconds(List<ActivityRecord> records)
    {
        long total = 0;

        foreach (ActivityRecord record in records)
        {
            long endUtcMs = record.EndedAtUtcMs ?? record.StartedAtUtcMs;
            total += (endUtcMs - record.StartedAtUtcMs) / 1000;
        }

        return total;
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds < 60)
        {
            return seconds + "s";
        }

        if (seconds < 3600)
        {
            return (seconds / 60) + "m " + (seconds % 60) + "s";
        }

        long hours = seconds / 3600;
        long minutes = (seconds % 3600) / 60;

        return hours + "h " + minutes.ToString("00") + "m";
    }

    // =======================================================================
    //  Look
    // =======================================================================

    /// <summary>
    /// The saved theme, for the server to build into the page before it is sent.
    /// Always exactly "light" or "dark".
    /// </summary>
    public string GetTheme()
    {
        return _settings.GetTheme();
    }

    private object SetTheme(JsonElement body)
    {
        _settings.SetTheme(ReadString(body, "theme"));

        return new { theme = _settings.GetTheme() };
    }

    // =======================================================================
    //  Recording
    // =======================================================================

    private object SetPaused(JsonElement body)
    {
        bool paused = ReadBool(body, "paused", false);

        _recording.SetPaused(paused);

        return new { paused = _recording.IsPaused };
    }

    // =======================================================================
    //  Blocking
    // =======================================================================

    private object GetApps(bool rescan)
    {
        if (_installedApps == null || rescan)
        {
            InstalledAppsScanner scanner = new InstalledAppsScanner(_logger);
            _installedApps = scanner.Scan(_store.GetAppsSeenBefore());
        }

        // Which program names are blocked right now, and whether the block is
        // one TimeFold ships with.
        Dictionary<string, bool> blocked = new Dictionary<string, bool>();

        foreach (BlockRule rule in _blockRules.GetAll())
        {
            if (rule.MatchType == BlockMatchType.ExeFileName && rule.IsEnabled)
            {
                blocked[rule.Pattern.ToLowerInvariant()] = rule.IsBuiltIn;
            }
        }

        List<object> apps = new List<object>();

        foreach (InstalledApp app in _installedApps)
        {
            string key = app.ExeFileName.ToLowerInvariant();
            bool isBlocked = blocked.ContainsKey(key);

            apps.Add(new
            {
                displayName = app.DisplayName,
                exeFileName = app.ExeFileName,
                foundIn = app.FoundIn,
                blocked = isBlocked,
                blockedByTimeFold = isBlocked && blocked[key]
            });
        }

        return new { apps = apps };
    }

    private object SetAppBlocked(JsonElement body)
    {
        string exeFileName = ReadString(body, "exeFileName");
        bool shouldBlock = ReadBool(body, "blocked", false);

        if (string.IsNullOrWhiteSpace(exeFileName))
        {
            return new { error = "No app given." };
        }

        BlockRule existing = FindProgramNameRule(exeFileName);

        if (shouldBlock)
        {
            if (existing != null)
            {
                // Already there but switched off. Switch it on rather than
                // adding a second rule saying the same thing.
                _blockRules.SetEnabled(existing.Id, true);
            }
            else
            {
                _blockRules.Add(new BlockRule(
                    BlockMatchType.ExeFileName, exeFileName, "Blocked by you"));
            }
        }
        else if (existing != null)
        {
            if (existing.IsBuiltIn)
            {
                // Keep it, switched off, so it can be switched back on.
                _blockRules.SetEnabled(existing.Id, false);
            }
            else
            {
                _blockRules.Delete(existing.Id);
            }
        }

        _blockRuleProvider.Reload();

        // The page uses this to offer deleting what the app already recorded.
        long pastRecords = shouldBlock ? _store.CountRecordsForApp(exeFileName) : 0;

        return new { blocked = shouldBlock, pastRecords = pastRecords };
    }

    private BlockRule FindProgramNameRule(string exeFileName)
    {
        foreach (BlockRule rule in _blockRules.GetAll())
        {
            if (rule.MatchType != BlockMatchType.ExeFileName)
            {
                continue;
            }

            if (string.Equals(rule.Pattern, exeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private object GetRules()
    {
        List<object> rules = new List<object>();

        foreach (BlockRule rule in _blockRules.GetAll())
        {
            rules.Add(new
            {
                id = rule.Id,
                matchType = rule.MatchType.ToString(),
                matchTypeText = DescribeMatchType(rule.MatchType),
                pattern = rule.Pattern,
                note = rule.Note,
                enabled = rule.IsEnabled,
                builtIn = rule.IsBuiltIn
            });
        }

        return new { rules = rules };
    }

    private static string DescribeMatchType(BlockMatchType matchType)
    {
        switch (matchType)
        {
            case BlockMatchType.ExeFileName:
                return "Program name";

            case BlockMatchType.ExePathContains:
                return "Folder or path";

            case BlockMatchType.WindowTitleContains:
                return "Words in title";

            default:
                return matchType.ToString();
        }
    }

    private object AddRule(JsonElement body)
    {
        string matchTypeText = ReadString(body, "matchType");
        string pattern = ReadString(body, "pattern");

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new { error = "Type what to block first." };
        }

        BlockMatchType matchType;

        if (!Enum.TryParse(matchTypeText, true, out matchType))
        {
            matchType = BlockMatchType.ExeFileName;
        }

        _blockRules.Add(new BlockRule(matchType, pattern.Trim(), "Added by you"));
        _blockRuleProvider.Reload();

        return new { added = true };
    }

    private object SetRuleEnabled(JsonElement body)
    {
        long id = ReadLong(body, "id");
        bool enabled = ReadBool(body, "enabled", true);

        _blockRules.SetEnabled(id, enabled);
        _blockRuleProvider.Reload();

        return new { ok = true };
    }

    private object DeleteRule(JsonElement body)
    {
        _blockRules.Delete(ReadLong(body, "id"));
        _blockRuleProvider.Reload();

        return new { ok = true };
    }

    // =======================================================================
    //  Deleting
    // =======================================================================

    private object DeleteRecord(JsonElement body)
    {
        string id = ReadString(body, "id");

        int deleted = _store.DeleteRecord(id);

        _logger.Info("Deleted one record, at your request.");

        return new { deleted = deleted };
    }

    private object DeleteApp(JsonElement body)
    {
        string exeFileName = ReadString(body, "exeFileName");

        if (string.IsNullOrWhiteSpace(exeFileName))
        {
            return new { error = "No app given." };
        }

        int deleted = _store.DeleteRecordsForApp(exeFileName);

        _logger.Info("Deleted " + deleted + " records for one app, at your request.");

        return new { deleted = deleted };
    }

    private object DeleteDay(JsonElement body)
    {
        DateTime day = ParseDay(ReadString(body, "date"));

        long fromUtcMs;
        long toUtcMs;
        AppClock.GetLocalDayRangeUtcMs(day, out fromUtcMs, out toUtcMs);

        int deleted = _store.DeleteRecordsBetween(fromUtcMs, toUtcMs);

        _logger.Info("Deleted " + deleted + " records for one day, at your request.");

        return new { deleted = deleted };
    }

    private object DeleteEverything()
    {
        int deleted = _store.DeleteAllRecords();

        _logger.Info("Deleted the entire history (" + deleted + " records), at your request.");

        return new { deleted = deleted };
    }

    // =======================================================================
    //  Export and restore
    // =======================================================================

    private void SendExportFile(HttpListenerContext context)
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(), "timefold-export-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            BackupService backup = new BackupService(_store, _logger);
            backup.ExportToFile(temporaryPath);

            byte[] bytes = File.ReadAllBytes(temporaryPath);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Content-Disposition"] =
                "attachment; filename=\"" + BackupService.SuggestFileName() + "\"";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A leftover temp file is not worth failing the download over.
            }
        }
    }

    private object Import(JsonElement body)
    {
        string json = ReadString(body, "json");

        if (string.IsNullOrWhiteSpace(json))
        {
            return new { error = "Nothing to restore." };
        }

        string temporaryPath = Path.Combine(
            Path.GetTempPath(), "timefold-import-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            File.WriteAllText(temporaryPath, json);

            BackupService backup = new BackupService(_store, _logger);

            return new { restored = backup.ImportFromFile(temporaryPath) };
        }
        catch (Exception error)
        {
            _logger.Error("Could not restore a backup file.", error);

            return new { error = error.Message };
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // Same as above.
            }
        }
    }

    // =======================================================================
    //  Plumbing
    // =======================================================================

    private static DateTime ParseDay(string text)
    {
        DateTime day;

        if (DateTime.TryParse(text, out day))
        {
            return day.Date;
        }

        return DateTime.Today;
    }

    private static string FormatDay(long utcMs)
    {
        return AppClock.ToLocalTime(utcMs, AppClock.CurrentOffsetMinutes())
            .ToString("yyyy-MM-dd");
    }

    private static JsonElement ReadBody(HttpListenerContext context)
    {
        using (StreamReader reader = new StreamReader(
            context.Request.InputStream, Encoding.UTF8))
        {
            string text = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new JsonElement();
            }

            try
            {
                return JsonDocument.Parse(text).RootElement.Clone();
            }
            catch
            {
                return new JsonElement();
            }
        }
    }

    private static string ReadString(JsonElement body, string name)
    {
        JsonElement value;

        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out value))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool ReadBool(JsonElement body, string name, bool fallback)
    {
        JsonElement value;

        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return fallback;
    }

    private static long ReadLong(JsonElement body, string name)
    {
        JsonElement value;

        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out value))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt64();
            }
        }

        return 0;
    }

    private static void SendJson(HttpListenerContext context, object payload)
    {
        string json = JsonSerializer.Serialize(payload);

        byte[] bytes = Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }
}
