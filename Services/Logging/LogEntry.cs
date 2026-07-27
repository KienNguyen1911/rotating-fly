using System;

namespace AssetAutomator.Services.Logging
{
    /// <summary>
    /// Log severity levels for structured logging across the application.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Categories for log routing — allows UI to filter logs by source.
    /// </summary>
    public enum LogCategory
    {
        General,
        GeminiCreator,
        GeminiApi,
        PythonServer,
        Pipeline,
        CookieSync,
        ImageGen,
        Voiceover,
        Config
    }

    /// <summary>
    /// A structured log entry with metadata for UI display and diagnostic tracing.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public LogLevel Level { get; init; }
        public LogCategory Category { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Source { get; init; }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
        public string LevelIcon => Level switch
        {
            LogLevel.Debug   => "🔍",
            LogLevel.Info    => "ℹ️",
            LogLevel.Success => "✅",
            LogLevel.Warning => "⚠️",
            LogLevel.Error   => "❌",
            _ => "•"
        };

        public string CategoryLabel => Category switch
        {
            LogCategory.PythonServer  => "[Python]",
            LogCategory.GeminiApi     => "[GeminiAPI]",
            LogCategory.GeminiCreator => "[Gemini]",
            LogCategory.Pipeline      => "[Pipeline]",
            LogCategory.CookieSync    => "[Cookie]",
            LogCategory.ImageGen      => "[ImageGen]",
            LogCategory.Voiceover     => "[Voice]",
            LogCategory.Config        => "[Config]",
            _ => ""
        };

        public override string ToString()
            => $"{FormattedTimestamp} {LevelIcon} {CategoryLabel} {Message}";
    }
}
