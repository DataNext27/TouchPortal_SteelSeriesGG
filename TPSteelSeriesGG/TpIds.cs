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
        public const string MuteChanged = P + "event_mute_changed";
        public const string ModeChanged = P + "event_mode_changed";
        public const string ChatMixChanged = P + "event_chatmix_changed";
        public const string ConfigChanged = P + "event_config_changed";
        public const string DeviceChanged = P + "event_device_changed";
        public const string MixToggled = P + "event_mix_toggled";
        public const string MonitoringChanged = P + "event_monitoring_changed";
        public const string ActivityChanged = P + "event_activity_changed";
        public const string ConnectionChanged = P + "event_connection_changed";
    }

    /// <summary>Local state ids sent along triggerEvent messages.</summary>
    public static class EventStates
    {
        public const string Channel = "channel";
        public const string Mix = "mix";
        public const string Volume = "volume";
        public const string Muted = "muted";
        public const string PreviousMode = "previous_mode";
        public const string NewMode = "new_mode";
        public const string Balance = "balance";
        public const string State = "state";
        public const string PreviousConfig = "previous_config";
        public const string Config = "config";
        public const string Target = "target";
        public const string Device = "device";
        public const string Enabled = "enabled";
        public const string Activity = "activity";
        public const string App = "app";
        public const string Connected = "connected";
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
        Events.VolumeChanged, Events.MuteChanged, Events.ModeChanged, Events.ChatMixChanged,
        Events.ConfigChanged, Events.DeviceChanged, Events.MixToggled, Events.MonitoringChanged,
        Events.ActivityChanged, Events.ConnectionChanged,
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

        return ids;
    }
}