using Microsoft.Extensions.Logging;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;
using SteelSeriesAPI.Sonar.Events;
using SteelSeriesAPI.Sonar.Models;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Models;

namespace TPSteelSeriesGG;

/// <summary>
/// The heart of the plugin: subscribes to the library's Sonar events and mirrors them
/// into Touch Portal as state updates, connector positions, and fired dropdown events.
/// Display states carry the user-customizable texts; the event trigger helper states
/// always carry the normalized choice values, so display customization never breaks
/// user event flows.
/// </summary>
public sealed class StatePublisher
{
    private readonly ITouchPortalClient _client;
    private readonly SonarClient _sonar;
    private readonly ILogger _logger;
    private readonly ConnectorEchoGuard _echoGuard;

    private readonly object _gate = new();
    private readonly object _walkGate = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private Mode _currentMode = Mode.Classic;

    // deviceId -> (channel, its app sessions). Channel activity is the OR over its
    // devices' active sessions; channel apps are deduplicated across channels.
    private readonly Dictionary<string, (Channel Channel, (string Name, bool Active)[] Sessions)> _deviceActivity = new();
    private readonly Dictionary<Channel, bool> _channelActivity = new();
    private readonly Dictionary<Channel, string> _channelApps = new();

    // Last values sent to Touch Portal: identical re-publishes are skipped so mode
    // switches and snapshots cost almost nothing when values did not change (flooding
    // TP with no-op messages trips its E3081 detector).
    private readonly Dictionary<string, string> _lastPublished = new();
    private readonly Dictionary<string, int> _lastConnectorValue = new();

    private int _volumeRefreshQueued;

    // Display customization pairs, keyed by setting name. Defaults are the normalized values.
    private sealed record TextPair(string On, string Off)
    {
        public string Of(bool on) => on ? On : Off;
    }

    private static Dictionary<string, TextPair> DefaultTexts() => new()
    {
        [TpIds.Settings.MuteText] = new TextPair("Muted", "Unmuted"),
        [TpIds.Settings.MixText] = new TextPair("On", "Off"),
        [TpIds.Settings.ChatMixStateText] = new TextPair("Enabled", "Disabled"),
        [TpIds.Settings.MonitoringText] = new TextPair("Enabled", "Disabled"),
        [TpIds.Settings.ActivityText] = new TextPair("Playing", "Silent"),
        [TpIds.Settings.ConnectionText] = new TextPair("Connected", "Disconnected"),
    };

    private readonly Dictionary<string, TextPair> _texts = DefaultTexts();

    public StatePublisher(ITouchPortalClient client, SonarClient sonar, ConnectorEchoGuard echoGuard, ILogger logger)
    {
        _client = client;
        _sonar = sonar;
        _echoGuard = echoGuard;
        _logger = logger;
    }

    /// <summary>Subscribes to every library event. Call once, before starting the listener.</summary>
    public void Attach()
    {
        _sonar.Events.Connected += OnConnected;
        _sonar.Events.Disconnected += OnDisconnected;
        _sonar.Events.VolumeChanged += OnVolumeChanged;
        _sonar.Events.VolumeDataReceived += OnVolumeSnapshot;
        _sonar.Events.ModeChanged += OnModeChanged;
        _sonar.Events.ChatMixChanged += OnChatMixChanged;
        _sonar.Events.ConfigSelectionChanged += OnConfigChanged;
        _sonar.Events.ClassicDeviceChanged += OnClassicDeviceChanged;
        _sonar.Events.MixDeviceChanged += OnMixDeviceChanged;
        _sonar.Events.MicDeviceChanged += OnMicDeviceChanged;
        _sonar.Events.MixChannelToggled += OnMixToggled;
        _sonar.Events.StreamMonitoringChanged += OnMonitoringChanged;
        _sonar.Events.AudioSessionOpened += OnAudioSession;
        _sonar.Events.AudioSessionClosed += OnAudioSession;
    }

    /// <summary>
    /// Applies the plugin settings (initial pairing or user change) and republishes
    /// every state so the new display texts take effect immediately.
    /// </summary>
    public void ApplySettings(IEnumerable<Setting>? settings)
    {
        if (settings is null) return;

        var defaults = DefaultTexts();
        lock (_gate)
        {
            foreach (Setting setting in settings)
            {
                if (!defaults.TryGetValue(setting.Name, out TextPair? fallback)) continue;

                string[] parts = (setting.Value ?? "").Split(',', 2, StringSplitOptions.TrimEntries);
                _texts[setting.Name] = parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0
                    ? new TextPair(parts[0], parts[1])
                    : fallback; // malformed value: fall back to defaults rather than publishing garbage
            }
        }

        _ = RefreshAllAsync("settings changed");
    }

    private TextPair Texts(string settingName)
    {
        lock (_gate) return _texts[settingName];
    }

    // ----------------------------------------------------------------
    // The event walker
    // ----------------------------------------------------------------

    /// <summary>
    /// Fires a dropdown event by walking its trigger helper state through the matching
    /// choice values ("Any" first, then the specifics), then back to blank. TP triggers
    /// each event instance whose chosen value the state changes to; ending blank
    /// guarantees the next occurrence produces a change even with identical values.
    /// Walks are serialized: two near-simultaneous occurrences never interleave steps.
    /// </summary>
    private void FireEvent(string triggerStateId, params string[] matchingValues)
    {
        // Deliberately bypasses the Publish() dedup cache: the walk re-sends the same
        // values on every occurrence ("Any" -> value -> "") and must never be skipped.
        lock (_walkGate)
        {
            _client.StateUpdate(triggerStateId, "Any");
            foreach (string value in matchingValues)
                _client.StateUpdate(triggerStateId, value);
            _client.StateUpdate(triggerStateId, "");
        }
    }

    /// <summary>
    /// Fires a triggerEvent-driven event (no dropdown, no trigger state), optionally
    /// carrying local state values. Used for events with nothing to filter on.
    /// </summary>
    private void FireSimpleEvent(string eventId, Dictionary<string, string>? localStates = null) =>
        ((ICommandHandler)_client).TriggerEvent(eventId, localStates ?? new Dictionary<string, string>());


    // ----------------------------------------------------------------
    // Connection lifecycle
    // ----------------------------------------------------------------

    private void OnConnected(object? sender, EventArgs e)
    {
        Publish(TpIds.States.Connection, Texts(TpIds.Settings.ConnectionText).Of(true));
        FireEvent(TpIds.Triggers.Connection, "Connected");
        _ = RefreshAllAsync("connected to Sonar");
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Publish(TpIds.States.Connection, Texts(TpIds.Settings.ConnectionText).Of(false));
        FireEvent(TpIds.Triggers.Connection, "Disconnected");
    }

    /// <summary>
    /// Fetches the full Sonar state and republishes every Touch Portal state and connector.
    /// Runs on connection, reconnection, and settings changes. Serialized: overlapping
    /// refreshes queue behind each other instead of interleaving.
    /// </summary>
    private async Task RefreshAllAsync(string reason)
    {
        await _refreshLock.WaitAsync();
        try
        {
            _logger.LogInformation("Refreshing all Touch Portal states ({Reason})", reason);

            Mode mode = await _sonar.Mode.GetAsync();
            lock (_gate) _currentMode = mode;
            Publish(TpIds.States.Mode, mode.ToString());

            await PublishVolumesForModeAsync(mode);

            // Chat mix
            ChatMixSetting chatMix = await _sonar.ChatMix.GetAsync();
            PublishChatMix(chatMix);

            // Selected configs
            var selected = await _sonar.Configs.GetSelectedAsync();
            foreach ((Channel channel, SonarConfig config) in selected)
                Publish(TpIds.States.Config(channel.Key()), config.Name);

            // Devices: resolve ids to names once for this refresh
            var devices = await _sonar.Devices.GetAllAsync();
            var names = devices.ToDictionary(d => d.Id, d => d.Name);
            string NameOf(string id) => names.GetValueOrDefault(id, id);

            foreach (ClassicRedirection redirection in await _sonar.Redirections.GetClassicRedirectionsAsync())
                Publish(TpIds.States.ClassicDevice(redirection.Channel.Key()), NameOf(redirection.DeviceId));

            StreamRedirections stream = await _sonar.Redirections.GetStreamRedirectionsAsync();
            if (stream.Personal is { } personal)
            {
                Publish(TpIds.States.DevicePersonalMix, NameOf(personal.DeviceId));
                PublishMixChannels(personal);
            }
            if (stream.Stream is { } streamMix)
            {
                Publish(TpIds.States.DeviceStreamMix, NameOf(streamMix.DeviceId));
                PublishMixChannels(streamMix);
            }
            if (stream.Mic is { } mic)
                Publish(TpIds.States.DeviceStreamerMic, NameOf(mic.DeviceId));

            // Audience monitoring
            bool monitoring = await _sonar.Redirections.GetStreamMonitoringEnabledAsync();
            Publish(TpIds.States.AudienceMonitoring, Texts(TpIds.Settings.MonitoringText).Of(monitoring));

            // Audio activity: rebuild the per-device cache from scratch
            var routings = await _sonar.AppRouting.GetRoutingsAsync();
            lock (_gate)
            {
                _deviceActivity.Clear();
                foreach (DeviceRouting routing in routings.Where(r => r.Channel is not null))
                    _deviceActivity[routing.DeviceId] = (routing.Channel!.Value, SessionsOf(routing));
            }
            foreach (Channel channel in new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux, Channel.Mic })
                UpdateChannelActivity(channel, fireEvents: false);
            RecomputeRoutedApps();

            _logger.LogInformation("All Touch Portal states refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full state refresh failed ({Reason})", reason);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Fetches and publishes the volumes and mutes of one mode from the authoritative
    /// HTTP route. Only the current mode's values are reliable (Sonar returns stale
    /// data for the other mode's sections).
    /// </summary>
    private async Task PublishVolumesForModeAsync(Mode mode)
    {
        Channel[] channels = [Channel.Master, Channel.Game, Channel.Chat, Channel.Media, Channel.Aux, Channel.Mic];
        if (mode == Mode.Classic)
        {
            foreach (Channel channel in channels)
                PublishVolume(channel, null, await _sonar.VolumeSettings.GetAsync(channel));
        }
        else
        {
            foreach (Channel channel in channels)
            {
                PublishVolume(channel, Mix.Personal, await _sonar.VolumeSettings.GetAsync(channel, Mix.Personal));
                PublishVolume(channel, Mix.Stream, await _sonar.VolumeSettings.GetAsync(channel, Mix.Stream));
            }
        }
    }

    // ----------------------------------------------------------------
    // Volumes, mode, chat mix
    // ----------------------------------------------------------------

    private void OnVolumeChanged(object? sender, VolumeChange e)
    {
        try
        {
            PublishVolume(e.Channel, e.Mix, new VolumeSetting(e.NewVolume, e.IsMuted));

            string combo = $"{e.Channel.Display()} ({e.Mix.Display()})";
            if (e.MuteToggled)
                FireEvent(e.IsMuted ? TpIds.Triggers.Muted : TpIds.Triggers.Unmuted, combo);
            else
                FireEvent(TpIds.Triggers.Volume, combo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Volume change handling failed");
        }
    }

    private void OnVolumeSnapshot(object? sender, VolumeSnapshot snapshot)
    {
        // Snapshots arrive on connection, mode switches, and OS/hardware-initiated
        // changes: resynchronize the states of the current mode.
        try
        {
            Mode mode;
            lock (_gate) mode = _currentMode;

            foreach ((Channel channel, ChannelVolumes volumes) in snapshot.Channels)
            {
                if (mode == Mode.Classic)
                {
                    if (volumes.Classic is { } classic) PublishVolume(channel, null, classic);
                }
                else
                {
                    if (volumes.Personal is { } personal) PublishVolume(channel, Mix.Personal, personal);
                    if (volumes.Stream is { } stream) PublishVolume(channel, Mix.Stream, stream);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Volume snapshot handling failed");
        }
    }

    private void PublishVolume(Channel channel, Mix? mix, VolumeSetting setting)
    {
        string mixKey = mix.Key();
        string channelKey = channel.Key();
        int volume = TpMappings.ToTpVolume(setting.Volume);

        Publish(TpIds.States.Volume(mixKey, channelKey), volume.ToString());
        Publish(TpIds.States.Mute(mixKey, channelKey), Texts(TpIds.Settings.MuteText).Of(setting.Muted));

        // Move the matching slider, unless Touch Portal itself is currently dragging it
        // (echoing back during a drag floods TP - its E3081 detector fires).
        string connectorKey = mix is null
            ? TpMappings.ConnectorKey(TpIds.Connectors.VolumeClassic, channel.Display())
            : TpMappings.ConnectorKey(TpIds.Connectors.VolumeStreamer, channel.Display(), mix.Display());
        if (_echoGuard.ShouldEcho(connectorKey))
            UpdateConnector(connectorKey, volume);
    }

    private void OnModeChanged(object? sender, ModeChange e)
    {
        try
        {
            lock (_gate) _currentMode = e.NewMode;
            Publish(TpIds.States.Mode, e.NewMode.ToString());
            FireEvent(TpIds.Triggers.Mode, e.NewMode.ToString());

            // Sonar's mode-switch snapshot races with the polling-based mode detection
            // (it can arrive while _currentMode is still the old mode and get filtered
            // against the stale sections). Re-fetch the new mode's volumes explicitly -
            // debounced, so spamming the switch button collapses into one refresh of
            // the final mode instead of a message flood (E3081).
            QueueVolumeRefresh();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mode change handling failed");
        }
    }

    /// <summary>Debounced, single-flight volume refresh of the current mode.</summary>
    private void QueueVolumeRefresh()
    {
        if (Interlocked.Exchange(ref _volumeRefreshQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            Interlocked.Exchange(ref _volumeRefreshQueued, 0);

            Mode mode;
            lock (_gate) mode = _currentMode;
            try
            {
                await PublishVolumesForModeAsync(mode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Volume refresh after mode change failed");
            }
        });
    }

    private void OnChatMixChanged(object? sender, ChatMixSetting e)
    {
        try
        {
            PublishChatMix(e);
            bool chatMixEnabled = string.Equals(e.State, "enabled", StringComparison.OrdinalIgnoreCase);
            string balance = TpMappings.ToTpBalance(e.Balance).ToString();
            string chatMixState = chatMixEnabled ? "Enabled" : "Disabled";
            FireSimpleEvent(TpIds.Events.ChatMixChanged, new Dictionary<string, string>
            {
                [TpIds.EventStates.Balance] = balance,
                [TpIds.EventStates.State] = chatMixState,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChatMix change handling failed");
        }
    }

    private void PublishChatMix(ChatMixSetting chatMix)
    {
        bool enabled = string.Equals(chatMix.State, "enabled", StringComparison.OrdinalIgnoreCase);
        Publish(TpIds.States.ChatMixBalance, TpMappings.ToTpBalance(chatMix.Balance).ToString());
        Publish(TpIds.States.ChatMixState, Texts(TpIds.Settings.ChatMixStateText).Of(enabled));
        if (_echoGuard.ShouldEcho(TpIds.Connectors.ChatMix))
            UpdateConnector(TpIds.Connectors.ChatMix, TpMappings.ToTpBalanceConnector(chatMix.Balance));
    }

    // ----------------------------------------------------------------
    // Configs, devices, mixes, monitoring
    // ----------------------------------------------------------------

    private void OnConfigChanged(object? sender, ConfigSelectionChange e)
    {
        try
        {
            Publish(TpIds.States.Config(e.Channel.Key()), e.NewConfigName);
            FireEvent(TpIds.Triggers.Config, e.Channel.Display());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Config change handling failed");
        }
    }

    private async void OnClassicDeviceChanged(object? sender, ClassicDeviceChange e)
    {
        try
        {
            string name = await ResolveDeviceNameAsync(e.NewDeviceId);
            Publish(TpIds.States.ClassicDevice(e.Channel.Key()), name);
            FireEvent(TpIds.Triggers.Device, e.Channel.Display());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classic device change handling failed");
        }
    }

    private async void OnMixDeviceChanged(object? sender, MixDeviceChange e)
    {
        try
        {
            string name = await ResolveDeviceNameAsync(e.NewDeviceId);
            string target = e.Mix == Mix.Personal ? "Personal Mix" : "Stream Mix";
            Publish(e.Mix == Mix.Personal ? TpIds.States.DevicePersonalMix : TpIds.States.DeviceStreamMix, name);
            FireEvent(TpIds.Triggers.Device, target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mix device change handling failed");
        }
    }

    private async void OnMicDeviceChanged(object? sender, MicDeviceChange e)
    {
        try
        {
            string name = await ResolveDeviceNameAsync(e.NewDeviceId);
            Publish(TpIds.States.DeviceStreamerMic, name);
            FireEvent(TpIds.Triggers.Device, "Streamer Mic");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mic device change handling failed");
        }
    }

    /// <summary>Resolves a device id to its friendly name, fresh (ids are volatile across GG updates).</summary>
    private async Task<string> ResolveDeviceNameAsync(string deviceId)
    {
        var devices = await _sonar.Devices.GetAllAsync();
        return devices.FirstOrDefault(d => d.Id == deviceId)?.Name ?? deviceId;
    }

    private void OnMixToggled(object? sender, MixChannelToggle e)
    {
        try
        {
            string mixKey = e.Mix == Mix.Personal ? "personal" : "stream";
            string mixDisplay = e.Mix == Mix.Personal ? "Personal" : "Stream";

            Publish(TpIds.States.Mix(mixKey, e.Channel.Key()), Texts(TpIds.Settings.MixText).Of(e.IsEnabled));
            string combo = $"{e.Channel.Display()} ({mixDisplay})";
            FireEvent(e.IsEnabled ? TpIds.Triggers.MixEnabled : TpIds.Triggers.MixDisabled, combo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mix toggle handling failed");
        }
    }

    private void PublishMixChannels(MixRedirection mix)
    {
        string mixKey = mix.Mix == Mix.Personal ? "personal" : "stream";
        foreach ((Channel channel, bool enabled) in mix.EnabledChannels)
            Publish(TpIds.States.Mix(mixKey, channel.Key()), Texts(TpIds.Settings.MixText).Of(enabled));
    }

    private void OnMonitoringChanged(object? sender, StreamMonitoringChange e)
    {
        try
        {
            Publish(TpIds.States.AudienceMonitoring, Texts(TpIds.Settings.MonitoringText).Of(e.IsEnabled));
            FireEvent(TpIds.Triggers.Monitoring, e.IsEnabled ? "On" : "Off");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Monitoring change handling failed");
        }
    }

    // ----------------------------------------------------------------
    // Audio activity
    // ----------------------------------------------------------------

    private void OnAudioSession(object? sender, DeviceRouting routing)
    {
        try
        {
            if (routing.Channel is not { } channel) return;

            lock (_gate)
                _deviceActivity[routing.DeviceId] = (channel, SessionsOf(routing));

            var app = routing.Sessions.FirstOrDefault(s => !s.IsSystemSound && s.IsActive)
                      ?? routing.Sessions.FirstOrDefault(s => !s.IsSystemSound);
            if (app is not null)
                Publish(TpIds.States.LastAudioApp, app.DisplayName is { Length: > 0 } name ? name : app.ProcessName);

            UpdateChannelActivity(channel, fireEvents: true);
            RecomputeRoutedApps();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio session handling failed");
        }
    }

    private static (string Name, bool Active)[] SessionsOf(DeviceRouting routing) =>
        routing.Sessions
            .Where(s => !s.IsSystemSound)
            .Select(s => (Name: s.DisplayName is { Length: > 0 } name ? name : s.ProcessName, s.IsActive))
            .ToArray();

    /// <summary>
    /// Recomputes the "Routed Apps" state of every channel. Ghost sessions from past
    /// routings are deduplicated: an app that is active somewhere is only listed where
    /// it is active; its inactive leftovers on other channels are ignored.
    /// </summary>
    private void RecomputeRoutedApps()
    {
        var updates = new List<(Channel Channel, string Apps)>();
        lock (_gate)
        {
            var activeSomewhere = _deviceActivity.Values
                .SelectMany(d => d.Sessions.Where(s => s.Active).Select(s => s.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (Channel channel in new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux, Channel.Mic })
            {
                string apps = string.Join(", ", _deviceActivity.Values
                    .Where(d => d.Channel == channel)
                    .SelectMany(d => d.Sessions)
                    .Where(s => s.Active || !activeSomewhere.Contains(s.Name))
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order());

                if (_channelApps.GetValueOrDefault(channel) != apps)
                {
                    _channelApps[channel] = apps;
                    updates.Add((channel, apps));
                }
            }
        }

        foreach ((Channel channel, string apps) in updates)
            Publish(TpIds.States.Apps(channel.Key()), apps);
    }

    /// <summary>
    /// Recomputes a channel's activity (the OR over its devices), and publishes only
    /// when the value actually changed, so ghost session updates never spam users.
    /// </summary>
    private void UpdateChannelActivity(Channel channel, bool fireEvents)
    {
        bool playing;
        bool changed;
        lock (_gate)
        {
            playing = _deviceActivity.Values.Any(d => d.Channel == channel && d.Sessions.Any(s => s.Active));
            changed = !_channelActivity.TryGetValue(channel, out bool previous) || previous != playing;
            _channelActivity[channel] = playing;
        }

        if (!changed && fireEvents) return;

        Publish(TpIds.States.Activity(channel.Key()), Texts(TpIds.Settings.ActivityText).Of(playing));

        if (fireEvents)
            FireEvent(playing ? TpIds.Triggers.ActivityStarted : TpIds.Triggers.ActivityStopped, channel.Display());
    }

    // ----------------------------------------------------------------
    // Touch Portal primitives
    // ----------------------------------------------------------------

    /// <summary>Sends a state update, skipping values identical to the last one sent.</summary>
    private void Publish(string stateId, string value)
    {
        lock (_gate)
        {
            if (_lastPublished.TryGetValue(stateId, out string? last) && last == value) return;
            _lastPublished[stateId] = value;
        }
        _client.StateUpdate(stateId, value);
    }

    /// <summary>Sends a connector update, skipping positions identical to the last one sent.</summary>
    private void UpdateConnector(string connectorKey, int value)
    {
        lock (_gate)
        {
            if (_lastConnectorValue.TryGetValue(connectorKey, out int last) && last == value) return;
            _lastConnectorValue[connectorKey] = value;
        }
        _client.ConnectorUpdate(connectorKey, value);
    }
}