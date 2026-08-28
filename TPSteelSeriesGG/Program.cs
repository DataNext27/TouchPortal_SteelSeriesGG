using Microsoft.Extensions.Logging;

namespace TPSteelSeriesGG;

internal static class Program
{
    private static void Main()
    {
        // Touch Portal captures this process' stdout into its own logs:
        // one console logger covers both the plugin and the library diagnostics.
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Information));

        using var plugin = new SteelSeriesPlugin(loggerFactory);
        plugin.Run();

        // The TP client listens on a background thread; block until the plugin says goodbye.
        plugin.WaitForShutdown();
    }
}