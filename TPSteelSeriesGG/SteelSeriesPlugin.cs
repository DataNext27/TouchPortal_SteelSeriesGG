using Microsoft.Extensions.Logging;
using SteelSeriesAPI.Sonar;
using System.Diagnostics;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;
using TouchPortalSDK.Messages.Models;

namespace TPSteelSeriesGG;

/// <summary>
/// The Touch Portal plugin: bridges Touch Portal (actions, connectors, lists)
/// and SteelSeries Sonar (through the Steelseries-NET-API library).
/// </summary>
public sealed class SteelSeriesPlugin : ITouchPortalEventHandler, IDisposable
{
    /// <inheritdoc />
    public string PluginId => "steelseries-gg";

    private readonly ILogger _logger;
    private readonly ITouchPortalClient _client;
    private readonly SonarClient _sonar;
    private readonly StatePublisher _publisher;
    private readonly ActionHandler _actions;
    private readonly ManualResetEventSlim _shutdown = new(false);
    private readonly UpdateChecker _updateChecker;
    private int _updateCheckDone;
    private volatile string? _latestReleaseUrl;

    public SteelSeriesPlugin(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("Plugin");
        _client = TouchPortalFactory.CreateClient(this);
        _sonar = new SonarClient(loggerFactory.CreateLogger("SteelSeriesAPI"));
        var echoGuard = new ConnectorEchoGuard();
        _publisher = new StatePublisher(_client, _sonar, echoGuard, loggerFactory.CreateLogger("StatePublisher"));
        _actions = new ActionHandler(_client, _sonar, echoGuard, loggerFactory.CreateLogger("ActionHandler"));
        _updateChecker = new UpdateChecker(loggerFactory.CreateLogger("UpdateChecker"));
    }

    /// <summary>Connects to Touch Portal, then starts the Sonar event stream.</summary>
    public void Run()
    {
        _logger.LogInformation("Connecting to Touch Portal...");
        if (!_client.Connect())
        {
            _logger.LogError("Could not connect to Touch Portal. Is it running?");
            _shutdown.Set();
            return;
        }
        _logger.LogInformation("Connected to Touch Portal");

        // Sonar side: the listener owns discovery, reconnection, and GG liveness.
        // If GG is not running yet, the listener will simply connect when it appears,
        // and the Connected handlers will push the initial state and choice lists.
        _publisher.Attach();
        _actions.Attach();
        _sonar.Events.PollingInterval = TimeSpan.FromMilliseconds(500);
        _sonar.Events.Start();
    }

    /// <summary>Blocks until Touch Portal closes the plugin.</summary>
    public void WaitForShutdown() => _shutdown.Wait();

    // ---- Touch Portal message handlers ----

    /// <inheritdoc />
    public void OnInfoEvent(InfoEvent message)
    {
        _logger.LogInformation("Touch Portal info: SDK {Sdk}, TP {Version}", message.SdkVersion, message.TpVersionString);
        // The pairing message carries the initial settings values.
        _publisher.ApplySettings(message.Settings);

        // One version check per plugin run, off the handler thread. Silent on failure.
        if (Interlocked.Exchange(ref _updateCheckDone, 1) == 0)
            _ = NotifyIfUpdateAvailableAsync();
    }

    /// <summary>Notifies through Touch Portal when a newer plugin release exists.</summary>
    private async Task NotifyIfUpdateAvailableAsync()
    {
        var update = await _updateChecker.CheckAsync();
        if (update is null) return;

        _latestReleaseUrl = update.Url;
        _client.ShowNotification(
            TpIds.Notifications.UpdateAvailable,
            $"SteelSeries GG plugin {update.TagName} is available",
            $"You are running version {UpdateChecker.CurrentVersion}.\n" +
            "Click below to open the download page.",
            [new NotificationOptions { Id = TpIds.Notifications.GoToDownloadOption, Title = "Go to download page" }]);
    }

    /// <inheritdoc />
    public void OnSettingsEvent(SettingsEvent message)
    {
        _logger.LogInformation("Plugin settings changed");
        _publisher.ApplySettings(message.Values);
    }

    /// <inheritdoc />
    public void OnActionEvent(ActionEvent message) =>
        _ = _actions.HandleActionAsync(message); // handler catches and logs its own errors

    /// <inheritdoc />
    public void OnConnecterChangeEvent(ConnectorChangeEvent message) =>
        _ = _actions.HandleConnectorChangeAsync(message);

    /// <inheritdoc />
    public void OnListChangedEvent(ListChangeEvent message) =>
        _ = _actions.HandleListChangeAsync(message);

    /// <inheritdoc />
    public void OnClosedEvent(string message)
    {
        _logger.LogInformation("Touch Portal closed the plugin: {Message}", message);
        _shutdown.Set();
    }

    /// <inheritdoc />
    public void OnBroadcastEvent(BroadcastEvent message) { }

    /// <inheritdoc />
    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message)
    {
        if (message.OptionId != TpIds.Notifications.GoToDownloadOption) return;
        string url = _latestReleaseUrl ?? "https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest";
        try
        {
            // UseShellExecute hands the URL to the default browser.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the release page");
        }
    }

    /// <inheritdoc />
    public void OnShortConnectorIdNotificationEvent(ShortConnectorIdNotificationEvent message) { }

    /// <inheritdoc />
    public void OnUnhandledEvent(string jsonMessage) =>
        _logger.LogDebug("Unhandled TP message: {Json}", jsonMessage);

    /// <inheritdoc />
    public void Dispose()
    {
        _sonar.Dispose();
        _shutdown.Dispose();
    }
}