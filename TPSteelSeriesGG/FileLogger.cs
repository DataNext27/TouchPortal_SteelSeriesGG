using System.Text;
using Microsoft.Extensions.Logging;

namespace TPSteelSeriesGG;

/// <summary>
/// Minimal file logger: one shared append stream, thread-safe, timestamped lines.
/// The file lives next to the executable so users can attach it to issue reports.
/// A logging failure must never hurt the plugin: creation is guarded by the caller,
/// and rotation swallows its own errors.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string path, LogLevel minLevel = LogLevel.Information)
    {
        _minLevel = minLevel;
        // FileShare.Read lets the user open/copy log.txt while the plugin runs.
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        { AutoFlush = true };
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        string line = FormatLine(DateTime.Now, level, category, message, exception);
        try
        {
            lock (_gate) _writer.WriteLine(line);
        }
        catch (ObjectDisposedException)
        {
            // A straggler logging during shutdown is not worth a crash.
        }
    }

    /// <summary>One log line: "2026-08-30 18:00:00.123 [INF] StatePublisher: message" (+ exception below).</summary>
    internal static string FormatLine(DateTime timestamp, LogLevel level, string category, string message, Exception? exception)
    {
        string label = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

        var sb = new StringBuilder()
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(label).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
            sb.AppendLine().Append(exception);
        return sb.ToString();
    }

    /// <summary>
    /// Startup rotation: once log.txt grows past the cap, it becomes log-old.txt
    /// (replacing the previous one) and a fresh file starts. Two files, ~4 MB worst case.
    /// </summary>
    public static void RotateIfNeeded(string path, long maxBytes = 2 * 1024 * 1024)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= maxBytes) return;

            string old = Path.Combine(info.DirectoryName!,
                Path.GetFileNameWithoutExtension(path) + "-old" + info.Extension);
            File.Move(path, old, overwrite: true);
        }
        catch
        {
            // Rotation must never block startup; worst case the file keeps growing.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
