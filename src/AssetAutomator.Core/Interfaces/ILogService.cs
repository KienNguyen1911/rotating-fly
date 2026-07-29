namespace AssetAutomator.Core.Interfaces
{
    /// <summary>
    /// Centralized logging abstraction. Routes structured log entries
    /// to all registered sinks (UI, trace, file, etc.) simultaneously.
    /// </summary>
    public interface ILogService
    {
        /// <summary>
        /// Fires on every log entry — UI can subscribe to update in real time.
        /// </summary>
        event Action<LogEntry>? OnLogEntry;

        /// <summary>
        /// Log a message with explicit level and category.
        /// </summary>
        void Log(LogLevel level, LogCategory category, string message, string? source = null);

        /// <summary>
        /// Convenience: Log Info.
        /// </summary>
        void Info(LogCategory category, string message, string? source = null);

        /// <summary>
        /// Convenience: Log Success.
        /// </summary>
        void Success(LogCategory category, string message, string? source = null);

        /// <summary>
        /// Convenience: Log Warning.
        /// </summary>
        void Warning(LogCategory category, string message, string? source = null);

        /// <summary>
        /// Convenience: Log Error.
        /// </summary>
        void Error(LogCategory category, string message, string? source = null);

        /// <summary>
        /// Convenience: Log Debug.
        /// </summary>
        void Debug(LogCategory category, string message, string? source = null);
    }
}
