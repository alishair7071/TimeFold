namespace TimeFold.Core.Logging;

/// <summary>
/// Writes messages about what the app is doing.
///
/// IMPORTANT RULE: the log may record that something happened. It may never
/// record your activity data - no window titles, no URLs, no file names.
/// If private data ends up in the log file, the Privacy Gate has achieved nothing.
/// </summary>
public interface IAppLogger
{
    void Info(string message);

    void Error(string message, Exception error);
}
