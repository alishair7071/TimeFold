using System.Text;

namespace TimeFold.Core.Logging;

/// <summary>
/// Writes log lines to a text file, one file per day.
/// Old files are deleted after a number of days so the folder cannot grow forever.
/// </summary>
public class FileLogger : IAppLogger
{
    private readonly string _logsFolder;
    private readonly int _keepDays;

    // Only one thread may write to the file at a time.
    private readonly object _writeLock = new object();

    public FileLogger(string logsFolder, int keepDays = 7)
    {
        _logsFolder = logsFolder;
        _keepDays = keepDays;

        Directory.CreateDirectory(_logsFolder);
        DeleteOldLogFiles();
    }

    public void Info(string message)
    {
        WriteLine("INFO ", message);
    }

    public void Error(string message, Exception error)
    {
        string text = message;

        if (error != null)
        {
            text = message + " | " + error.GetType().Name + ": " + error.Message;
        }

        WriteLine("ERROR", text);
    }

    private void WriteLine(string level, string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(GetTodayLogFilePath(), line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // If we cannot write the log, we must not crash the app.
                // Logging is never more important than recording.
            }
        }
    }

    private string GetTodayLogFilePath()
    {
        string fileName = "timefold-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
        return Path.Combine(_logsFolder, fileName);
    }

    private void DeleteOldLogFiles()
    {
        try
        {
            DateTime oldestAllowed = DateTime.Now.AddDays(-_keepDays);

            foreach (string file in Directory.GetFiles(_logsFolder, "timefold-*.log"))
            {
                if (File.GetLastWriteTime(file) < oldestAllowed)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Cleaning up logs is best-effort only.
        }
    }
}
