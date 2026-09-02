namespace StealthRAT.Interfaces;

/// <summary>
/// Defines a contract for logging services used throughout the application.
/// Implementing this interface allows for different logging backends
/// (file-based, UI-based, network-based) to be used interchangeably.
/// </summary>
public interface ILoggerService
{
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    void LogInfo(string message);

    /// <summary>
    /// Logs an error message with an optional exception.
    /// </summary>
    /// <param name="message">The error description.</param>
    /// <param name="exception">The associated exception, if any.</param>
    void LogError(string message, Exception? exception = null);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The warning message.</param>
    void LogWarning(string message);
}
