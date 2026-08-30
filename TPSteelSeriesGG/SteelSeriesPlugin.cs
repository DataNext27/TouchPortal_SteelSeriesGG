using Microsoft.Extensions.Logging;
using SteelSeriesAPI.Sonar;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;

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
    private readonly ManualResetEventSlim _shutdown = new(false);

    public SteelSeriesPlugin(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("Plugin");
        _client = TouchPortalFactory.CreateClient(this);
        _sonar = new SonarClient(loggerFactory.CreateLogger("SteelSeriesAPI"));
        _publisher = new StatePublisher(_client, _sonar, loggerFactory.CreateLogger("StatePublisher"));
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
        // and the publisher's Connected handler will do the initial full state push.
        _publisher.Attach();
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
    }

    /// <inheritdoc />
    public void OnSettingsEvent(SettingsEvent message)
    {
        _logger.LogInformation("Plugin settings changed");
        _publisher.ApplySettings(message.Values);
    }

    /// <inheritdoc />
    public void OnClosedEvent(string message)
    {
        _logger.LogInformation("Touch Portal closed the plugin: {Message}", message);
        _shutdown.Set();
    }

    /// <inheritdoc />
    public void OnActionEvent(ActionEvent message) =>
        _logger.LogDebug("Action received: {ActionId}", message.ActionId);

    /// <inheritdoc />
    public void OnConnecterChangeEvent(ConnectorChangeEvent message) =>
        _logger.LogDebug("Connector change: {ConnectorId}", message.ConnectorId);

    /// <inheritdoc />
    public void OnListChangedEvent(ListChangeEvent message) =>
        _logger.LogDebug("List changed: {ListId}", message.ListId);

    /// <inheritdoc />
    public void OnBroadcastEvent(BroadcastEvent message) { }

    /// <inheritdoc />
    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message) { }

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