using System;
using System.Diagnostics;
using System.Text;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Infrastructure.Logging
{
    /// <summary>
    /// Default implementation of ILogService.
    /// Routes logs to:
    ///   - Event subscribers (UI)
    ///   - System.Diagnostics.Trace (for DebugView/VS Output)
    ///   - In-memory ring buffer (last 500 entries for diagnostic panels)
    /// </summary>
    public class LogService : ILogService
    {
        private const int MaxBufferEntries = 500;
        private readonly LogEntry[] _buffer = new LogEntry[MaxBufferEntries];
        private int _bufferIndex;
        private readonly object _bufferLock = new();

        public event Action<LogEntry>? OnLogEntry;

        public void Log(LogLevel level, LogCategory category, string message, string? source = null)
        {
            var entry = new LogEntry
            {
                Level = level,
                Category = category,
                Message = message,
                Source = source
            };

            // 1. Add to ring buffer
            lock (_bufferLock)
            {
                _buffer[_bufferIndex % MaxBufferEntries] = entry;
                _bufferIndex++;
            }

            // 2. Write to Trace for DebugView / VS Output
            Trace.WriteLine(entry.ToString());

            // 3. Fire UI event
            OnLogEntry?.Invoke(entry);
        }

        public void Info(LogCategory category, string message, string? source = null)
            => Log(LogLevel.Info, category, message, source);

        public void Success(LogCategory category, string message, string? source = null)
            => Log(LogLevel.Success, category, message, source);

        public void Warning(LogCategory category, string message, string? source = null)
            => Log(LogLevel.Warning, category, message, source);

        public void Error(LogCategory category, string message, string? source = null)
            => Log(LogLevel.Error, category, message, source);

        public void Debug(LogCategory category, string message, string? source = null)
            => Log(LogLevel.Debug, category, message, source);

        /// <summary>
        /// Returns all buffered log entries (max 500) for diagnostic panels.
        /// Most recent entries first.
        /// </summary>
        public LogEntry[] GetBufferedEntries()
        {
            lock (_bufferLock)
            {
                int count = Math.Min(_bufferIndex, MaxBufferEntries);
                var result = new LogEntry[count];

                if (_bufferIndex <= MaxBufferEntries)
                {
                    Array.Copy(_buffer, 0, result, 0, count);
                }
                else
                {
                    int start = _bufferIndex % MaxBufferEntries;
                    int tailLen = MaxBufferEntries - start;
                    Array.Copy(_buffer, start, result, 0, tailLen);
                    Array.Copy(_buffer, 0, result, tailLen, start);
                }

                Array.Reverse(result);
                return result;
            }
        }

        /// <summary>
        /// Builds a plain-text dump of the log buffer for clipboard or file export.
        /// </summary>
        public string GetLogDump()
        {
            var sb = new StringBuilder();
            var entries = GetBufferedEntries();
            Array.Reverse(entries);
            foreach (var e in entries)
            {
                sb.AppendLine(e.ToString());
            }
            return sb.ToString();
        }
    }
}
