namespace TPSteelSeriesGG;

/// <summary>
/// Central registry of every Touch Portal identifier used by this plugin.
/// Must stay in sync with entry.tp: the EntryTpContractTests suite enforces it.
/// Same discipline as SonarRoutes in the library: no TP id string lives anywhere else.
/// </summary>
public static class TpIds
{
    public const string Plugin = "steelseries-gg";

    private const string P = "tp_steelseries-gg_";

    /// <summary>The TP key segments for channels, in Sonar order.</summary>
    public static readonly string[] ChannelKeys = ["master", "game", "chat", "media", "aux", "mic"];

    /// <summary>Channels that carry configs, devices, mixes and audio activity (no Master).</summary>
    public static readonly string[] RoutedChannelKeys = ["game", "chat", "media", "aux", "mic"];

    /// <summary>The volume/mute state families.</summary>
    public static readonly string[] MixKeys = ["classic", "personal", "stream"];

    /// <summary>The streamer mixes.</summary>
    public static readonly string[] StreamerMixKeys = ["personal", "stream"];

    public static class Actions
    {
        public const string SwitchMode = P + "switch_mode";
        public const string SetMode = P + "set_mode";
        public const string SetClassicMute = P + "set_classic_mute";
        public const string SetStreamerMute = P + "set_streamer_mute";
        public const string SetConfig = P + "set_config";
        public const string SetClassicDevice = P + "set_classic_device";
        public const string SetStreamerDevice = P + "set_streamer_device";
        public const string SetMix = P + "set_mix";
        public const string SetAudienceMonitoring = P + "set_audience_monitoring";
        public const string RouteActiveWindow = P + "route_active_window";
        public const string RouteApp = P + "route_app";
    }

    /// <summary>Data field ids used inside actions and connectors.</summary>
    public static class Data
    {
        public const string Action = "action";
        public const string Channel = "channel";
        public const string Mix = "mix";
        public const string Mode = "mode";
        public const string Config = "config";
        public const string Device = "device";
        public const string Target = "target";
        public const string App = "app";
    }

    public static class Connectors
    {
        public const string VolumeClassic = P + "connector_volume_classic";
        public const string VolumeStreamer = P + "connector_volume_streamer";
        public const string ChatMix = P + "connector_chatmix";
    }

    public static class Events
    {
        public const string VolumeChanged = P + "event_volume_changed";
        public const string ChannelMuted = P + "event_channel_muted";
        public const string ChannelUnmuted = P + "event_channel_unmuted";
        public const string ModeChanged = P + "event_mode_changed";
        public const string ChatMixChanged = P + "event_chatmix_changed";
        public const string ConfigChanged = P + "event_config_changed";
        public const string DeviceChanged = P + "event_device_changed";
        public const string MixChannelEnabled = P + "event_mix_channel_enabled";
        public const string MixChannelDisabled = P + "event_mix_channel_disabled";
        public const string ActivityStarted = P + "event_activity_started";
        public const string ActivityStopped = P + "event_activity_stopped";
        public const string MonitoringChanged = P + "event_monitoring_changed";
        public const string ConnectionChanged = P + "event_connection_changed";
    }

    /// <summary>
    /// Local state ids carried by the triggerEvent-fired events (ChatMix).
    /// Dropdown events cannot carry local states.
    /// </summary>
    public static class EventStates
    {
        public const string Balance = "balance";
        public const string State = "state";
    }

    /// <summary>
    /// Event trigger helper states: the event walker writes matching choice values
    /// through them to fire the dropdown events. Internal, never displayed.
    /// </summary>
    public static class Triggers
    {
        public const string Volume = P + "state_trigger_volume";
        public const string Muted = P + "state_trigger_muted";
        public const string Unmuted = P + "state_trigger_unmuted";
        public const string Mode = P + "state_trigger_mode";
        public const string Config = P + "state_trigger_config";
        public const string Device = P + "state_trigger_device";
        public const string MixEnabled = P + "state_trigger_mix_enabled";
        public const string MixDisabled = P + "state_trigger_mix_disabled";
        public const string ActivityStarted = P + "state_trigger_activity_started";
        public const string ActivityStopped = P + "state_trigger_activity_stopped";
        public const string Monitoring = P + "state_trigger_monitoring";
        public const string Connection = P + "state_trigger_connection";
    }

    public static class States
    {
        public const string Mode = P + "state_mode";
        public const string ChatMixBalance = P + "state_chatmix_balance";
        public const string ChatMixState = P + "state_chatmix_state";
        public const string AudienceMonitoring = P + "state_audience_monitoring";
        public const string Connection = P + "state_connection";
        public const string DevicePersonalMix = P + "state_device_personal_mix";
        public const string DeviceStreamMix = P + "state_device_stream_mix";
        public const string DeviceStreamerMic = P + "state_device_streamer_mic";
        public const string LastAudioApp = P + "state_last_audio_app";

        /// <summary>Volume state id. mixKey: classic/personal/stream; channelKey: master..mic.</summary>
        public static string Volume(string mixKey, string channelKey) => $"{P}state_volume_{mixKey}_{channelKey}";

        /// <summary>Mute state id. Same keys as <see cref="Volume"/>.</summary>
        public static string Mute(string mixKey, string channelKey) => $"{P}state_mute_{mixKey}_{channelKey}";

        /// <summary>Selected config state id. channelKey: game/chat/media/aux/mic.</summary>
        public static string Config(string channelKey) => $"{P}state_config_{channelKey}";

        /// <summary>Classic redirection device state id. channelKey: game/chat/media/aux/mic.</summary>
        public static string ClassicDevice(string channelKey) => $"{P}state_device_classic_{channelKey}";

        /// <summary>Mix enablement state id. mixKey: personal/stream; channelKey: game..mic.</summary>
        public static string Mix(string mixKey, string channelKey) => $"{P}state_mix_{mixKey}_{channelKey}";

        /// <summary>Audio activity state id. channelKey: game/chat/media/aux/mic.</summary>
        public static string Activity(string channelKey) => $"{P}state_activity_{channelKey}";
    }

    public static class Settings
    {
        public const string MuteText = "Mute States Text";
        public const string MixText = "Mix States Text";
        public const string ChatMixStateText = "ChatMix State Text";
        public const string MonitoringText = "Audience Monitoring Text";
        public const string ActivityText = "Audio Activity Text";
        public const string ConnectionText = "Connection Text";
    }

    /// <summary>Every event trigger helper state id.</summary>
    public static IReadOnlyList<string> AllTriggerStateIds { get; } =
    [
        Triggers.Volume, Triggers.Muted, Triggers.Unmuted, Triggers.Mode,
        Triggers.Config, Triggers.Device, Triggers.MixEnabled, Triggers.MixDisabled,
        Triggers.ActivityStarted, Triggers.ActivityStopped, Triggers.Monitoring, Triggers.Connection,
    ];

    /// <summary>Every state id the plugin publishes. The contract test checks this equals entry.tp exactly.</summary>
    public static IReadOnlyList<string> AllStateIds { get; } = BuildAllStateIds();

    /// <summary>Every action id. Contract-tested against entry.tp.</summary>
    public static IReadOnlyList<string> AllActionIds { get; } =
    [
        Actions.SwitchMode, Actions.SetMode, Actions.SetClassicMute, Actions.SetStreamerMute,
        Actions.SetConfig, Actions.SetClassicDevice, Actions.SetStreamerDevice, Actions.SetMix,
        Actions.SetAudienceMonitoring, Actions.RouteActiveWindow, Actions.RouteApp,
    ];

    /// <summary>Every event id. Contract-tested against entry.tp.</summary>
    public static IReadOnlyList<string> AllEventIds { get; } =
    [
        Events.VolumeChanged, Events.ChannelMuted, Events.ChannelUnmuted, Events.ModeChanged,
        Events.ChatMixChanged, Events.ConfigChanged, Events.DeviceChanged, Events.MixChannelEnabled,
        Events.MixChannelDisabled, Events.ActivityStarted, Events.ActivityStopped,
        Events.MonitoringChanged, Events.ConnectionChanged,
    ];

    /// <summary>Every connector id. Contract-tested against entry.tp.</summary>
    public static IReadOnlyList<string> AllConnectorIds { get; } =
        [Connectors.VolumeClassic, Connectors.VolumeStreamer, Connectors.ChatMix];

    /// <summary>Every setting name. Contract-tested against entry.tp.</summary>
    public static IReadOnlyList<string> AllSettingNames { get; } =
    [
        Settings.MuteText, Settings.MixText, Settings.ChatMixStateText,
        Settings.MonitoringText, Settings.ActivityText, Settings.ConnectionText,
    ];

    /// <summary>The data ids each action must declare in entry.tp. Contract-tested.</summary>
    public static IReadOnlyDictionary<string, string[]> ActionDataIds { get; } = new Dictionary<string, string[]>
    {
        [Actions.SwitchMode] = [],
        [Actions.SetMode] = [Data.Mode],
        [Actions.SetClassicMute] = [Data.Action, Data.Channel],
        [Actions.SetStreamerMute] = [Data.Action, Data.Channel, Data.Mix],
        [Actions.SetConfig] = [Data.Channel, Data.Config],
        [Actions.SetClassicDevice] = [Data.Channel, Data.Device],
        [Actions.SetStreamerDevice] = [Data.Target, Data.Device],
        [Actions.SetMix] = [Data.Action, Data.Channel, Data.Mix],
        [Actions.SetAudienceMonitoring] = [Data.Action],
        [Actions.RouteActiveWindow] = [Data.Channel],
        [Actions.RouteApp] = [Data.App, Data.Channel],
    };

    private static List<string> BuildAllStateIds()
    {
        var ids = new List<string>();

        foreach (string mix in MixKeys)
        foreach (string channel in ChannelKeys)
            ids.Add(States.Volume(mix, channel));

        foreach (string mix in MixKeys)
        foreach (string channel in ChannelKeys)
            ids.Add(States.Mute(mix, channel));

        ids.AddRange([States.Mode, States.ChatMixBalance, States.ChatMixState, States.AudienceMonitoring, States.Connection]);

        foreach (string channel in RoutedChannelKeys) ids.Add(States.Config(channel));
        foreach (string channel in RoutedChannelKeys) ids.Add(States.ClassicDevice(channel));
        ids.AddRange([States.DevicePersonalMix, States.DeviceStreamMix, States.DeviceStreamerMic]);

        foreach (string mix in StreamerMixKeys)
        foreach (string channel in RoutedChannelKeys)
            ids.Add(States.Mix(mix, channel));

        foreach (string channel in RoutedChannelKeys) ids.Add(States.Activity(channel));
        ids.Add(States.LastAudioApp);

        ids.AddRange(AllTriggerStateIds);

        return ids;
    }
}