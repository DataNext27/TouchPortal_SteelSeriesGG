using Microsoft.Extensions.Logging;

namespace TPSteelSeriesGG;

internal static class Program
{
    private static void Main()
    {
        // log.txt lives next to the executable so users can attach it to issue reports.
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string logPath = Path.Combine(exeDir, "log.txt");
        FileLoggerProvider.RotateIfNeeded(logPath);
        FileLoggerProvider? fileProvider = TryCreateFileProvider(logPath);

        // Touch Portal captures this process' stdout into its own logs:
        // the console logger covers TP-side diagnostics, log.txt covers issue reports.
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Information);
            if (fileProvider is not null)
                builder.AddProvider(fileProvider); // the factory disposes it with itself
        });

        var log = loggerFactory.CreateLogger("Plugin");
        log.LogInformation("TPSteelSeriesGG {Version} starting (pid {Pid}, .NET {Runtime})",
            UpdateChecker.CurrentVersion, Environment.ProcessId, Environment.Version);

        // A crashing plugin must leave a trace: these are the first lines to read in an issue.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception, the plugin is going down");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        using var plugin = new SteelSeriesPlugin(loggerFactory);
        plugin.Run();

        // The TP client listens on a background thread; block until the plugin says goodbye.
        plugin.WaitForShutdown();
        log.LogInformation("TPSteelSeriesGG stopped cleanly");
    }

    /// <summary>The log file is a nice-to-have: if it cannot be created, the plugin still runs.</summary>
    private static FileLoggerProvider? TryCreateFileProvider(string path)
    {
        try
        {
            return new FileLoggerProvider(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not create {path}: {ex.Message}");
            return null;
        }
    }
}
