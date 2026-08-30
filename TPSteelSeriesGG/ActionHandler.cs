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

    public ActionHandler(ITouchPortalClient client, SonarClient sonar, ILogger logger)
    {
        _client = client;
        _sonar = sonar;
        _logger = logger;
    }

    /// <summary>Subscribes to the library events that keep the dynamic choice lists fresh.</summary>
    public void Attach()
    {
        _sonar.Events.Connected += (_, _) => _ = RefreshChoiceListsAsync("connected");
        // Keep the "running apps" list fresh as sessions come and go.
        _sonar.Events.AudioSessionOpened += (_, _) => _ = RefreshAppListAsync();
        _sonar.Events.AudioSessionClosed += (_, _) => _ = RefreshAppListAsync();
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
                    await _sonar.AppRouting.RouteAppAsync((int)pid, channel);
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

    /// <summary>Applies a Touch Portal slider move to the matching Sonar value.</summary>
    public async Task HandleConnectorChangeAsync(ConnectorChangeEvent message)
    {
        try
        {
            switch (message.ConnectorId)
            {
                case TpIds.Connectors.VolumeClassic:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is { } channel)
                        await _sonar.VolumeSettings.SetVolumeAsync(channel, message.Value / 100.0);
                    break;
                }

                case TpIds.Connectors.VolumeStreamer:
                {
                    if (TpMappings.ParseChannel(message[TpIds.Data.Channel]) is { } channel &&
                        TpMappings.ParseStreamerMix(message[TpIds.Data.Mix]) is { } mix)
                        await _sonar.VolumeSettings.SetVolumeAsync(channel, mix, message.Value / 100.0);
                    break;
                }

                case TpIds.Connectors.ChatMix:
                    await _sonar.ChatMix.SetAsync(message.Value / 50.0 - 1.0); // 0-100 -> -1..1
                    break;
            }
        }
        catch (SteelSeriesException ex)
        {
            _logger.LogWarning(ex, "Connector {ConnectorId} failed", message.ConnectorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling connector {ConnectorId}", message.ConnectorId);
        }
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

            var devices = await _sonar.Devices.GetAllAsync();
            _client.ChoiceUpdate(TpIds.Data.Device,
                devices.Where(d => !d.IsSonarVirtual).Select(d => d.Name).Distinct().Order().ToArray());

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