using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SteelSeriesAPI.Core;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;

namespace TPSteelSeriesGG;

/// <summary>
/// Handles Touch Portal actions, connector moves and dynamic list updates by calling
/// the library's managers. The counterpart of StatePublisher: TP -> Sonar.
/// </summary>
public sealed class ActionHandler
{
    private readonly ITouchPortalClient _client;
    private readonly SonarClient _sonar;
    private readonly ILogger _logger;
    private readonly ConnectorEchoGuard _echoGuard;

    // Latest pending volume write per connector, flushed every ~80ms: slider drags
    // send one Touch Portal message per step, but only the last value matters.
    private readonly object _volumeGate = new();
    private readonly Dictionary<string, Func<Task>> _pendingVolumeWrites = new();
    private bool _volumeFlushScheduled;

    private int _appListRefreshQueued;

    public ActionHandler(ITouchPortalClient client, SonarClient sonar, ConnectorEchoGuard echoGuard, ILogger logger)
    {
        _client = client;
        _sonar = sonar;
        _echoGuard = echoGuard;
        _logger = logger;
    }

    /// <summary>Subscribes to the library events that keep the dynamic choice lists fresh.</summary>
    public void Attach()
    {
        _sonar.Events.Connected += (_, _) => _ = RefreshChoiceListsAsync("connected");
        // Keep the "running apps" list fresh as sessions come and go (debounced:
        // session events arrive in bursts, one refresh per burst is enough).
        _sonar.Events.AudioSessionOpened += (_, _) => QueueAppListRefresh();
        _sonar.Events.AudioSessionClosed += (_, _) => QueueAppListRefresh();
    }

    // ----------------------------------------------------------------
    // Actions
    // ----------------------------------------------------------------

    /// <summary>Routes one Touch Portal action to the matching library call.</summary>
    public async Task HandleActionAsync(ActionEvent message)
    {
        try
        {
            switch (message.ActionId)
            {
                case TpIds.Actions.SwitchMode:
                {
                    Mode current = await _sonar.Mode.GetAsync();
                    await _sonar.Mode.SetAsync(current == Mode.Classic ? Mode.Streamer : Mode.Classic);
                    break;
                }

                case TpIds.Actions.SetMode:
                {
                    if (TpMappings.ParseMode(message[TpIds.Data.Mode]) is { } mode)
                        await _sonar.Mode.SetAsync(mode);
                    break;
                }

                case TpIds.Actions.SetClassicMute:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    bool muted = message[TpIds.Data.Action] switch
                    {
                        "Mute" => true,
                        "Unmute" => false,
                        _ => !(await _sonar.VolumeSettings.GetAsync(channel)).Muted, // Toggle
                    };
                    await _sonar.VolumeSettings.SetMuteAsync(channel, muted);
                    break;
                }

                case TpIds.Actions.SetStreamerMute:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel ||
                        TpMappings.ParseStreamerMix(message[TpIds.Data.Mix]) is not { } mix) break;
                    bool muted = message[TpIds.Data.Action] switch
                    {
                        "Mute" => true,
                        "Unmute" => false,
                        _ => !(await _sonar.VolumeSettings.GetAsync(channel, mix)).Muted, // Toggle
                    };
                    await _sonar.VolumeSettings.SetMuteAsync(channel, mix, muted);
                    break;
                }

                case TpIds.Actions.SetConfig:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    string? name = message[TpIds.Data.Config];
                    var configs = await _sonar.Configs.GetAllAsync(channel);
                    var config = configs.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (config is null)
                    {
                        _logger.LogWarning("Config '{Name}' not found for channel {Channel}", name, channel);
                        break;
                    }
                    await _sonar.Configs.SelectAsync(config.Id);
                    break;
                }

                case TpIds.Actions.SetClassicDevice:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    if (await FindDeviceByNameAsync(message[TpIds.Data.Device]) is not { } device) break;
                    await _sonar.Redirections.SetClassicDeviceAsync(channel, device.Id);
                    break;
                }

                case TpIds.Actions.SetStreamerDevice:
                {
                    Mix? mix = message[TpIds.Data.Target] switch
                    {
                        "Personal Mix" => Mix.Personal,
                        "Stream Mix" => Mix.Stream,
                        _ => null,
                    };
                    if (mix is null) break;
                    if (await FindDeviceByNameAsync(message[TpIds.Data.Device]) is not { } device) break;
                    await _sonar.Redirections.SetMixDeviceAsync(mix.Value, device.Id);
                    break;
                }

                case TpIds.Actions.SetMix:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel ||
                        TpMappings.ParseStreamerMix(message[TpIds.Data.Mix]) is not { } mix) break;
                    bool enabled;
                    switch (message[TpIds.Data.Action])
                    {
                        case "Enable": enabled = true; break;
                        case "Disable": enabled = false; break;
                        default: // Toggle
                        {
                            var state = await _sonar.Redirections.GetStreamRedirectionsAsync();
                            var mixState = mix == Mix.Personal ? state.Personal : state.Stream;
                            enabled = !(mixState?.EnabledChannels.GetValueOrDefault(channel) ?? false);
                            break;
                        }
                    }
                    await _sonar.Redirections.SetMixChannelEnabledAsync(mix, channel, enabled);
                    break;
                }

                case TpIds.Actions.SetAudienceMonitoring:
                {
                    bool enabled = message[TpIds.Data.Action] switch
                    {
                        "Enable" => true,
                        "Disable" => false,
                        _ => !await _sonar.Redirections.GetStreamMonitoringEnabledAsync(), // Toggle
                    };
                    await _sonar.Redirections.SetStreamMonitoringEnabledAsync(enabled);
                    break;
                }

                case TpIds.Actions.RouteActiveWindow:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                    if (pid == 0)
                    {
                        _logger.LogWarning("Could not identify the active window's process");
                        break;
                    }

                    // The window's PID is often NOT the audio session's PID (multi-process
                    // browsers, launchers...): match the audio session by PID first, then
                    // by process name, preferring active sessions.
                    string? processName = null;
                    try { processName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                    catch { /* process may have exited */ }

                    var routings = await _sonar.AppRouting.GetRoutingsAsync();
                    var session = routings
                        .SelectMany(r => r.Sessions)
                        .Where(s => !s.IsSystemSound &&
                                    (s.ProcessId == (int)pid ||
                                     (processName is not null &&
                                      string.Equals(s.ProcessName, processName, StringComparison.OrdinalIgnoreCase))))
                        .OrderByDescending(s => s.ProcessId == (int)pid)
                        .ThenByDescending(s => s.IsActive)
                        .FirstOrDefault();
                    if (session is null)
                    {
                        _logger.LogWarning("No audio session found for the active window (process '{Name}', pid {Pid})",
                            processName ?? "?", pid);
                        break;
                    }
                    await _sonar.AppRouting.RouteAppAsync(session.ProcessId, channel);
                    break;
                }

                case TpIds.Actions.RouteApp:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    string? appName = message[TpIds.Data.App];

                    // Process ids are volatile: always resolve the app by name at call time.
                    var routings = await _sonar.AppRouting.GetRoutingsAsync();
                    var session = routings
                        .SelectMany(r => r.Sessions)
                        .Where(s => !s.IsSystemSound &&
                                    (string.Equals(s.DisplayName, appName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(s.ProcessName, appName, StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(s => s.IsActive)
                        .FirstOrDefault();
                    if (session is null)
                    {
                        _logger.LogWarning("Application '{App}' not found among audio sessions", appName);
                        break;
                    }
                    await _sonar.AppRouting.RouteAppAsync(session.ProcessId, channel);
                    break;
                }

                default:
                    _logger.LogWarning("Unknown action: {ActionId}", message.ActionId);
                    break;
            }
        }
        catch (SonarWrongModeException)
        {
            _logger.LogWarning("Action {ActionId} is not available in the current Sonar mode", message.ActionId);
        }
        catch (SteelSeriesException ex)
        {
            _logger.LogWarning(ex, "Action {ActionId} failed", message.ActionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling action {ActionId}", message.ActionId);
        }
    }

    // ----------------------------------------------------------------
    // Connectors (sliders)
    // ----------------------------------------------------------------

    /// <summary>
    /// Applies a Touch Portal slider move to the matching Sonar value. Writes are
    /// coalesced (last value wins, ~80ms flush) and the echo guard is armed so the
    /// polling does not bounce the change back to Touch Portal mid-drag.
    /// </summary>
    public Task HandleConnectorChangeAsync(ConnectorChangeEvent message)
    {
        try
        {
            switch (message.ConnectorId)
            {
                case TpIds.Connectors.VolumeClassic:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel) break;
                    string key = TpMappings.ConnectorKey(message.ConnectorId, channel.Display());
                    _echoGuard.NoteTpMove(key);
                    double volume = message.Value / 100.0;
                    EnqueueVolumeWrite(key, () => _sonar.VolumeSettings.SetVolumeAsync(channel, volume));
                    break;
                }

                case TpIds.Connectors.VolumeStreamer:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is not { } channel ||
                        TpMappings.ParseStreamerMix(message[TpIds.Data.Mix]) is not { } mix) break;
                    string key = TpMappings.ConnectorKey(message.ConnectorId, channel.Display(),
                        mix == Mix.Personal ? "Personal" : "Stream");
                    _echoGuard.NoteTpMove(key);
                    double volume = message.Value / 100.0;
                    EnqueueVolumeWrite(key, () => _sonar.VolumeSettings.SetVolumeAsync(channel, mix, volume));
                    break;
                }

                case TpIds.Connectors.ChatMix:
                {
                    _echoGuard.NoteTpMove(TpIds.Connectors.ChatMix);
                    double balance = message.Value / 50.0 - 1.0; // 0-100 -> -1..1
                    EnqueueVolumeWrite(TpIds.Connectors.ChatMix, () => _sonar.ChatMix.SetAsync(balance));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling connector {ConnectorId}", message.ConnectorId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Stores the latest write for a connector and flushes pending writes every ~80ms.</summary>
    private void EnqueueVolumeWrite(string key, Func<Task> write)
    {
        lock (_volumeGate)
        {
            _pendingVolumeWrites[key] = write;
            if (_volumeFlushScheduled) return;
            _volumeFlushScheduled = true;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(80);

                Func<Task>[] batch;
                lock (_volumeGate)
                {
                    batch = _pendingVolumeWrites.Values.ToArray();
                    _pendingVolumeWrites.Clear();
                    if (batch.Length == 0)
                    {
                        _volumeFlushScheduled = false;
                        return;
                    }
                }

                foreach (Func<Task> pending in batch)
                {
                    try { await pending(); }
                    catch (SteelSeriesException ex) { _logger.LogWarning(ex, "Coalesced connector write failed"); }
                    catch (Exception ex) { _logger.LogError(ex, "Unexpected error in coalesced connector write"); }
                }
            }
        });
    }

    // ----------------------------------------------------------------
    // Dynamic choice lists
    // ----------------------------------------------------------------

    /// <summary>
    /// Narrows instance-specific lists when the user changes another field:
    /// picking a channel in "Set channel config" narrows the config list to that channel.
    /// </summary>
    public async Task HandleListChangeAsync(ListChangeEvent message)
    {
        try
        {
            if (message.ActionId == TpIds.Actions.SetConfig && message.ListId == TpIds.Data.Channel &&
                TpMappings.ParseChannel(message.Value) is { } channel)
            {
                var configs = await _sonar.Configs.GetAllAsync(channel);
                _client.ChoiceUpdate(TpIds.Data.Config,
                    configs.Select(c => c.Name).Distinct().Order().ToArray(),
                    message.InstanceId);
            }
            else if (message.ActionId == TpIds.Actions.SetClassicDevice && message.ListId == TpIds.Data.Channel &&
                     TpMappings.ParseChannel(message.Value) is { } deviceChannel)
            {
                // Mic is a capture redirection; every other channel is a render one.
                var devices = await _sonar.Devices.GetAllAsync(
                    deviceChannel == Channel.Mic ? AudioDataFlow.Capture : AudioDataFlow.Render);
                _client.ChoiceUpdate(TpIds.Data.Device,
                    devices.Select(d => d.Name).Distinct().Order().ToArray(),
                    message.InstanceId);
            }
            else if (message.ActionId == TpIds.Actions.SetStreamerDevice && message.ListId == TpIds.Data.Target)
            {
                // Both mixes are render targets.
                var devices = await _sonar.Devices.GetAllAsync(AudioDataFlow.Render);
                _client.ChoiceUpdate(TpIds.Data.Device,
                    devices.Select(d => d.Name).Distinct().Order().ToArray(),
                    message.InstanceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "List update failed for {ListId}", message.ListId);
        }
    }

    /// <summary>Populates the global dynamic lists: devices, apps, and all config names.</summary>
    private async Task RefreshChoiceListsAsync(string reason)
    {
        try
        {
            _logger.LogInformation("Refreshing dynamic choice lists ({Reason})", reason);

            // Default device list: render devices, matching the actions' default targets
            // (Game / Personal Mix). Picking Mic narrows to capture via the list update.
            var devices = await _sonar.Devices.GetAllAsync(AudioDataFlow.Render);
            _client.ChoiceUpdate(TpIds.Data.Device,
                devices.Select(d => d.Name).Distinct().Order().ToArray());

            var configs = await _sonar.Configs.GetAllAsync();
            _client.ChoiceUpdate(TpIds.Data.Config,
                configs.Select(c => c.Name).Distinct().Order().ToArray());

            await RefreshAppListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dynamic list refresh failed");
        }
    }

    /// <summary>Debounces app list refreshes: one refresh per burst of session events.</summary>
    private void QueueAppListRefresh()
    {
        if (Interlocked.Exchange(ref _appListRefreshQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            Interlocked.Exchange(ref _appListRefreshQueued, 0);
            await RefreshAppListAsync();
        });
    }

    /// <summary>Refreshes the "running apps" list from the current audio sessions.</summary>
    private async Task RefreshAppListAsync()
    {
        try
        {
            var routings = await _sonar.AppRouting.GetRoutingsAsync();
            string[] apps = routings
                .SelectMany(r => r.Sessions)
                .Where(s => !s.IsSystemSound)
                .Select(s => s.DisplayName is { Length: > 0 } name ? name : s.ProcessName)
                .Distinct()
                .Order()
                .ToArray();
            _client.ChoiceUpdate(TpIds.Data.App, apps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "App list refresh failed");
        }
    }

    /// <summary>Finds a non-virtual device by its friendly name, fresh (ids are volatile).</summary>
    private async Task<SteelSeriesAPI.Sonar.Models.AudioDevice?> FindDeviceByNameAsync(string? name)
    {
        var devices = await _sonar.Devices.GetAllAsync();
        var device = devices.FirstOrDefault(d =>
            !d.IsSonarVirtual && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (device is null)
            _logger.LogWarning("Device '{Name}' not found", name);
        return device;
    }

    // ----------------------------------------------------------------
    // Win32: identify the foreground window's process
    // ----------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}