using Microsoft.Extensions.Logging;
using TPSteelSeriesGG;
using Xunit;

namespace TPSteelSeriesGG.Tests;

public class FileLoggerTests
{
    [Fact]
    public void FormatLine_ContainsTimestampLevelCategoryAndMessage()
    {
        var ts = new DateTime(2026, 8, 30, 18, 0, 0, 123);

        string line = FileLoggerProvider.FormatLine(ts, LogLevel.Warning, "StatePublisher", "hello", null);

        Assert.Equal("2026-08-30 18:00:00.123 [WRN] StatePublisher: hello", line);
    }

    [Fact]
    public void FormatLine_AppendsTheExceptionWithItsStack()
    {
        Exception ex;
        try { throw new InvalidOperationException("boom"); }
        catch (Exception caught) { ex = caught; }

        string line = FileLoggerProvider.FormatLine(DateTime.Now, LogLevel.Error, "X", "failed", ex);

        Assert.Contains("failed", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("boom", line);
        Assert.Contains(nameof(FormatLine_AppendsTheExceptionWithItsStack), line); // stack trace present
    }

    [Fact]
    public void Provider_WritesThroughILogger_AndFiltersBelowMinLevel()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        try
        {
            using (var provider = new FileLoggerProvider(path, LogLevel.Information))
            {
                ILogger logger = provider.CreateLogger("Test");
                logger.LogDebug("too quiet to be written");
                logger.LogInformation("loud and clear");
            }

            string content = File.ReadAllText(path);
            Assert.Contains("loud and clear", content);
            Assert.DoesNotContain("too quiet", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RotateIfNeeded_MovesOversizedFileAndKeepsSmallOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "log.txt");
        string old = Path.Combine(dir, "log-old.txt");
        try
        {
            // Missing file: no-op.
            FileLoggerProvider.RotateIfNeeded(path, maxBytes: 10);
            Assert.False(File.Exists(old));

            // Small file: stays.
            File.WriteAllText(path, "tiny");
            FileLoggerProvider.RotateIfNeeded(path, maxBytes: 10);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(old));

            // Oversized file: becomes log-old.txt, replacing any previous one.
            File.WriteAllText(path, new string('x', 100));
            FileLoggerProvider.RotateIfNeeded(path, maxBytes: 10);
            Assert.False(File.Exists(path));
            Assert.Equal(new string('x', 100), File.ReadAllText(old));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
