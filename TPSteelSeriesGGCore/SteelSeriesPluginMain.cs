using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;
using SteelSeriesAPI.Sonar.Events;
using SteelSeriesAPI.Sonar.Models;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;
using TouchPortalSDK.Messages.Models;

using Octokit;
using NAudio.CoreAudioApi;
using DataFlow = NAudio.CoreAudioApi.DataFlow;

namespace TPSteelSeriesGGCore;

public class SteelSeriesPluginMain : ITouchPortalEventHandler
{
    private readonly string _version = "2.0.0";
    public string PluginId => "steelseries-gg";
    
    private readonly ITouchPortalClient _client;
    private readonly SonarBridge _sonarManager;
    private NamedPipeClientStream _pipeClient;
    public static StreamWriter pipeWriter;
    
    // Keep track of connectors level
    // Master, Game, Chat, Media, Aux, Mic
    private int[] _connectorsLevel = [-1, -1, -1, -1, -1, -1];
    private int[][] _connectorsLevelStreamer = [[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1]];
    private int _connectorChatMix = -1;

    public SteelSeriesPluginMain()
    {
        _client = TouchPortalFactory.CreateClient(this);
        _sonarManager = new SonarBridge();
        _pipeClient = new NamedPipeClientStream(".", "TP_steelseries-gg_plugin_logging", PipeDirection.InOut);
    }

    public async void Run()
    {
        // Connect to TP
        _client.Connect();
        
        // Create and Connect to Logger Pipe
        Thread pipeMonitor = new Thread(PipeMonitoring);
        pipeMonitor.Start();
        _pipeClient.Connect();
        pipeWriter = new StreamWriter(_pipeClient) { AutoFlush = true };
        
        // Check for new versions
        await CheckNewerVersion();
        
        _sonarManager.WaitUntilSonarStarted();
        Console.WriteLine(new SonarRetriever().WebServerAddress());

        // Listen for Sonar Events
        _sonarManager.StartListener();
        _sonarManager.Events.OnSonarModeChange += OnModeChangeHandler;
        _sonarManager.Events.OnSonarVolumeChange += OnVolumeChangeHandler;
        _sonarManager.Events.OnSonarMuteChange += OnMuteChangeHandler;
        _sonarManager.Events.OnSonarChatMixChange += OnChatMixChangeHandler;
        _sonarManager.Events.OnSonarConfigChange += OnConfigChangeHandler;
        _sonarManager.Events.OnSonarPlaybackDeviceChange += OnPlaybackDeviceChangeHandler;
        _sonarManager.Events.OnSonarRoutedProcessChange += OnRoutedProcessChangeHandler;
        _sonarManager.Events.OnSonarMixChange += OnMixChangeHandler;
        _sonarManager.Events.OnSonarAudienceMonitoringChange += OnAudienceMonitoringChangeHandler;

        InitializeConnectors();
        InitializeStates();
        Console.WriteLine("Initialized!");
    }
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    void InitializeConnectors()
    {
        // Initialize sliders
        foreach (Channel channel in (Channel[]) Enum.GetValues(typeof(Channel)))
        {
            // Classic
            var level = (int)(_sonarManager.VolumeSettings.GetVolume(channel) * 100f);
            if (_connectorsLevel[(int)channel] != level)
            {
                _connectorsLevel[(int) channel] = level;
                _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|channel={channel.ToString().ToLowerFirstUpper()}", level);
            }
            
            // Streamer
            foreach (Mix mix in (Mix[]) Enum.GetValues(typeof(Mix)))
            {
                level = (int)(_sonarManager.VolumeSettings.GetVolume(channel, mix) * 100f);
                if (_connectorsLevelStreamer[(int)channel][(int)mix] != level)
                {
                    _connectorsLevelStreamer[(int) channel][(int) mix] = level;
                    _client.ConnectorUpdate($"tp_steelseries-gg_streamer_set_volume|mix={mix.ToString().ToLowerFirstUpper()}|channel={channel.ToString().ToLowerFirstUpper()}", level);
                }
            }
        }
        
        var chatMixBalance = (int)(((_sonarManager.ChatMix.GetBalance() * 100f) + 1) * 50);
        if (_connectorChatMix != chatMixBalance)
        {
            _connectorChatMix = chatMixBalance;
            _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", chatMixBalance);
        }
    }

    void InitializeStates()
    {
        // Events states
        _client.RemoveState("tp_steelseries-gg_state_last_updated_volume");
        _client.CreateState("tp_steelseries-gg_state_last_updated_volume", "Last Used Slider", "", "SteelSeries GG Sonar");
        _client.RemoveState("tp_steelseries-gg_state_last_updated_mute");
        _client.CreateState("tp_steelseries-gg_state_last_updated_mute", "Last Muted Device", "", "SteelSeries GG Sonar");
        _client.RemoveState("tp_steelseries-gg_state_last_updated_unmute");
        _client.CreateState("tp_steelseries-gg_state_last_updated_unmute", "Last Unmuted Device", "", "SteelSeries GG Sonar");
        _client.RemoveState("tp_steelseries-gg_state_last_updated_config");
        _client.CreateState("tp_steelseries-gg_state_last_updated_config", "Last Updated Config", "", "SteelSeries GG Sonar");
        _client.RemoveState("tp_steelseries-gg_state_last_updated_redirection_state");
        _client.CreateState("tp_steelseries-gg_state_last_updated_redirection_state", "Last Updated Redirection State", "", "SteelSeries GG Sonar");
        _client.RemoveState("tp_steelseries-gg_state_last_updated_redirection_device");
        _client.CreateState("tp_steelseries-gg_state_last_updated_redirection_device", "Last Updated Redirection Device", "", "SteelSeries GG Sonar");

        // Display States
        _client.StateUpdate("tp_steelseries-gg_state_mode", _sonarManager.Mode.Get().ToString());
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_state", _sonarManager.ChatMix.GetState() ? "Enabled" : "Disabled");
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_balance", _sonarManager.ChatMix.GetBalance().ToString(CultureInfo.InvariantCulture));
        _client.StateUpdate("tp_steelseries-gg_state_audience_monitoring", _sonarManager.AudienceMonitoring.GetState() ? "Enabled" : "Disabled");
        
        foreach (var channel in (Channel[]) Enum.GetValues(typeof(Channel)))
        {
            _client.StateUpdate($"tp_steelseries-gg_state_volume_{channel.ToString().ToLower()}", _connectorsLevel[(int) channel].ToString());
            _client.StateUpdate($"tp_steelseries-gg_state_mute_{channel.ToString().ToLower()}", _sonarManager.VolumeSettings.GetMute(channel) ? "Muted" : "Unmuted");

            if (channel != Channel.MASTER)
            {
                _client.StateUpdate($"tp_steelseries-gg_state_config_{channel.ToString().ToLower()}", _sonarManager.Configurations.GetSelectedAudioConfiguration(channel).Name);
                _client.StateUpdate($"tp_steelseries-gg_state_playback_device_{channel.ToString().ToLower()}", _sonarManager.PlaybackDevices.GetPlaybackDevice(channel).Name);
            }
            
            foreach (var mix in (Mix[]) Enum.GetValues(typeof(Mix)))
            {
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{mix.ToString().ToLower()}_{channel.ToString().ToLower()}", _connectorsLevelStreamer[(int) channel][(int) mix].ToString());
                _client.StateUpdate($"tp_steelseries-gg_state_mute_{mix.ToString().ToLower()}_{channel.ToString().ToLower()}", _sonarManager.VolumeSettings.GetMute(channel, mix) ? "Muted" : "Unmuted");
                _client.StateUpdate($"tp_steelseries-gg_state_playback_device_{mix.ToString().ToLower()}", _sonarManager.PlaybackDevices.GetPlaybackDevice(mix).Name);
                if (channel != Channel.MASTER)
                {   
                    _client.StateUpdate($"tp_steelseries-gg_state_mix_{mix.ToString().ToLower()}_{channel.ToString().ToLower()}", _sonarManager.Mix.GetState(channel, mix) ? "Enabled" : "Disabled");
                }
            }
        }

        // Change Mic Redirection Device state depending on the mode
        if (_sonarManager.Mode.Get() == Mode.CLASSIC)_client.StateUpdate("tp_steelseries-gg_state_playback_device_mic", _sonarManager.PlaybackDevices.GetPlaybackDevice(Channel.MIC).Name);
        else _client.StateUpdate("tp_steelseries-gg_state_playback_device_mic", _sonarManager.PlaybackDevices.GetPlaybackDevice(Channel.MIC, Mode.STREAMER).Name);
        
        // Audience Monitoring State to prevent multiple call from the event
        _audienceMonitoringLastState = _sonarManager.AudienceMonitoring.GetState();
    }

    void OnClosedEvent(string message)
    {
        // Exit the app on TP Close
        pipeWriter.Close();
        _pipeClient.Close();
        _sonarManager.StopListener();
        Environment.Exit(0);
    }
    
    public void OnActionEvent(ActionEvent message)
    {
        switch (message.ActionId)
        {
            // Switch mode
            case "tp_steelseries-gg_switch_mode":
                if (_sonarManager.Mode.Get() == Mode.CLASSIC) _sonarManager.Mode.Set(Mode.STREAMER);
                else _sonarManager.Mode.Set(Mode.CLASSIC);
                Console.WriteLine("Switched mode.");
                break;
            
            // Set Specific mode
            case "tp_steelseries-gg_set_mode":
                _sonarManager.Mode.Set((Mode)Enum.Parse(typeof(Mode), message["mode"], true));
                Console.WriteLine("Mode set to " + message["mode"]);
                break;
            
            // Toggle/Mute/Unmute a classic device
            case "tp_steelseries-gg_set_classic_mute":
                if (message["action"] == "Toggle") _sonarManager.VolumeSettings.SetMute(!_sonarManager.VolumeSettings.GetMute((Channel)Enum.Parse(typeof(Channel), message["device"], true)), (Channel)Enum.Parse(typeof(Channel), message["device"], true));
                else if (message["action"] == "Mute") _sonarManager.VolumeSettings.SetMute(true, (Channel)Enum.Parse(typeof(Channel), message["device"], true));
                else _sonarManager.VolumeSettings.SetMute(false, (Channel)Enum.Parse(typeof(Channel), message["device"], true));
                Console.WriteLine(message["action"]+"d " + message["device"]);
                break;
            
            // Toggle/Mute/Unmute a streamer device
            case "tp_steelseries-gg_set_streamer_mute":
                if (message["action"] == "Toggle") _sonarManager.VolumeSettings.SetMute(!_sonarManager.VolumeSettings.GetMute((Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true)), (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                else if (message["action"] == "Mute") _sonarManager.VolumeSettings.SetMute(true, (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                else _sonarManager.VolumeSettings.SetMute(false, (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                Console.WriteLine(message["action"]+"d streamer mute on " + message["device"] + ", " + message["channel"]);
                break;
            
            // Change config of a Sonar device
            case "tp_steelseries-gg_set_config":
                _sonarManager.Configurations.SetConfigByName((Channel)Enum.Parse(typeof(Channel), message["device"], true), message["config"]);
                Console.WriteLine("Changed " + message["device"] + " config to " + message["config"] );
                break;
            
            // Change Redirection Device of a classic Sonar Device
            case "tp_steelseries-gg_set_classic_redirections_devices":
                _sonarManager.PlaybackDevices.SetPlaybackDevice(message["device"] != "Mic" ? _sonarManager.PlaybackDevices.GetOutputPlaybackDevices().First(device => device.Name == message["redirectionDevice"]).Id : _sonarManager.PlaybackDevices.GetInputPlaybackDevices().First(device => device.Name == message["redirectionDevice"]).Id, (Channel)Enum.Parse(typeof(Channel), message["device"], true));
                Console.WriteLine("Changed " + message["device"] + " classic mode redirection device to " + message["redirectionDevice"] );
                break;
            
            // Change Redirection Device of a streamer Sonar Device/Channel
            case "tp_steelseries-gg_set_streamer_redirections_devices":
                if(message["device-channel"] != "Mic") _sonarManager.PlaybackDevices.SetPlaybackDevice(_sonarManager.PlaybackDevices.GetOutputPlaybackDevices().First(device => device.Name == message["redirectionDevice"]).Id, (Mix)Enum.Parse(typeof(Mix), message["device-channel"], true));
                else _sonarManager.PlaybackDevices.SetPlaybackDevice(_sonarManager.PlaybackDevices.GetInputPlaybackDevices().First(device => device.Name == message["redirectionDevice"]).Id, (Channel)Enum.Parse(typeof(Channel), message["device-channel"], true));
                Console.WriteLine("Changed " + message["device-channel"] + " streamer mode redirection device to " + message["redirectionDevice"] );
                break;
            
            // Toggle/Enable/Disable streamer redirection states
            case "tp_steelseries-gg_set_redirections_states":
                if(message["action"] == "Toggle") _sonarManager.Mix.SetState(!_sonarManager.Mix.GetState((Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true)), (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                else if (message["action"] == "Enable") _sonarManager.Mix.SetState(true, (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                else _sonarManager.Mix.SetState(false, (Channel)Enum.Parse(typeof(Channel), message["device"], true), (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                Console.WriteLine(message["action"]+"d redirection state on " + message["device"] + ", " + message["channel"]);
                break;
            
            // Toggle/Enable/Disable streamer Audience Monitoring
            case "tp_steelseries-gg_set_audience_monitoring":
                if (message["action"] == "Toggle") _sonarManager.AudienceMonitoring.SetState(!_sonarManager.AudienceMonitoring.GetState());
                else if (message["action"] == "Enable") _sonarManager.AudienceMonitoring.SetState(true);
                else _sonarManager.AudienceMonitoring.SetState(false);
                Console.WriteLine(message["action"] +"d audience monitoring");
                break;
            
            // Route current window audio to a specific Sonar Device
            case "tp_steelseries_route_active_process":
                // Get active window
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return;
                GetWindowThreadProcessId(hWnd, out uint activeWindowProcessId);
                if (activeWindowProcessId == 0) return;
                Console.WriteLine($"Active window process ID: {activeWindowProcessId}");

                // Get all processes associated with the same executable
                var processName = Process.GetProcessById((int)activeWindowProcessId).ProcessName;
                var relatedProcesses = Process.GetProcessesByName(processName).Select(p => p.Id).ToList();
                if (relatedProcesses.Count == 0) return;

                // Enumerate audio sessions
                var deviceEnumerator = new MMDeviceEnumerator();
                var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = defaultDevice.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    uint sessionProcessId = session.GetProcessID;

                    if (relatedProcesses.Contains((int)sessionProcessId))
                    {
                        Console.WriteLine($"Routed pID {sessionProcessId} to {message["device"]}.");
                        _sonarManager.RoutedProcesses.RouteProcessToChannel((int)sessionProcessId,(Channel)Enum.Parse(typeof(Channel), message["device"], true));
                    }
                }
                break;
        }
    }
    
    public void OnConnecterChangeEvent(ConnectorChangeEvent message)
    {
        switch (message.ConnectorId)
        {
            // Set classic volumes with sliders
            case "tp_steelseries-gg_classic_set_volume":
                _sonarManager.VolumeSettings.SetVolume(message.Value / 100f, (Channel)Enum.Parse(typeof(Channel), message["device"], true)); 
                break;
            
            // Set streamer volumes with sliders
            case "tp_steelseries-gg_stream_set_volume":
                _sonarManager.VolumeSettings.SetVolume(message.Value / 100f,
                    (Channel)Enum.Parse(typeof(Channel), message["device"], true),
                    (Mix)Enum.Parse(typeof(Mix), message["channel"], true));
                break;
            
            // Change ChatMix balance if possible
            case "tp_steelseries-gg_set_chatmix_balance":
                if (_sonarManager.ChatMix.GetState()) { _sonarManager.ChatMix.SetBalance((message.Value / 100f) * (1 - -1) + -1); }
                else
                {
                    Thread.Sleep(500); // Prevent error E3081
                    _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", 50);
                    Console.WriteLine("Could not change ChatMix balance");
                }
                break;
        }
    }
    
    public void OnListChangedEvent(ListChangeEvent message)
    {
        switch (message.ActionId)
        {
            // List configs depending on the Sonar Device
            case "tp_steelseries-gg_set_config":
                switch (message.ListId)
                {
                    case "device":
                        _client.ChoiceUpdate("config", _sonarManager.Configurations.GetAudioConfigurations((Channel)Enum.Parse(typeof(Channel), message.Value, true)).Select(config => config.Name).ToArray());
                        break;  
                }
                break;
            
            // List devices depending on the data flow of the Sonar Device (Input/Output)
            case "tp_steelseries-gg_set_classic_redirections_devices":
                switch (message.ListId)
                {
                    case "device":
                        if (message.Value != "Mic") _client.ChoiceUpdate("redirectionDevice", _sonarManager.PlaybackDevices.GetOutputPlaybackDevices().Select(device => device.Name).ToArray());
                        else _client.ChoiceUpdate("redirectionDevice", _sonarManager.PlaybackDevices.GetInputPlaybackDevices().Select(device => device.Name).ToArray());
                        break;
                }
                break;
            
            // List devices depending on the data flow of the Sonar Device (Input/Output)
            case "tp_steelseries-gg_set_streamer_redirections_devices":
                switch (message.ListId)
                {
                    case "device-channel":
                        if (message.Value != "Mic") _client.ChoiceUpdate("redirectionDevice", _sonarManager.PlaybackDevices.GetOutputPlaybackDevices().Select(device => device.Name).ToArray());
                        else _client.ChoiceUpdate("redirectionDevice", _sonarManager.PlaybackDevices.GetInputPlaybackDevices().Select(device => device.Name).ToArray());
                        break;
                }
                break;
        }
    }

    public void OnBroadcastEvent(BroadcastEvent message)
    {
        // Reinitialize Connectors when page change
        // Prevent some connectors not being updated
        InitializeConnectors();
    }

    void TriggerEvent(string valueStateId, string value = "")
    {
        // In case the state value is the same, we remove it and re-add it
        // to make sure the event triggers
        _client.StateUpdate(valueStateId, "");
        Thread.Sleep(50);
        _client.StateUpdate(valueStateId, value);
    }

    void OnModeChangeHandler(object? sender, SonarModeEvent eventArgs)
    {
        Console.WriteLine("Mode changed.");
        Thread.Sleep(150);
        InitializeConnectors();
        InitializeStates();
        _client.TriggerEvent("tp_steelseries-gg_event_on_mode");
    }
    
    void OnVolumeChangeHandler(object? sender, SonarVolumeEvent eventArgs)
    {
        // Update Connectors
        if (eventArgs.Mode == Mode.CLASSIC)
        {
            Console.WriteLine(eventArgs.Channel + " volume changed, updating connectors/sliders...");
            if ((eventArgs.Channel == Channel.MASTER || eventArgs.Volume > _sonarManager.VolumeSettings.GetVolume(Channel.MASTER)) && eventArgs.Channel != Channel.MIC)
            {
                foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
                {
                    if (eventArgs.Channel == Channel.MIC) continue;
                    var level = (int)(_sonarManager.VolumeSettings.GetVolume(channel) * 100f);
                    if (_connectorsLevel[(int) channel] == level) continue;
                    _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|channel={channel.ToString()}", level);
                    _connectorsLevel[(int)channel] = level;
                    _client.StateUpdate($"tp_steelseries-gg_state_volume_{channel.ToString().ToLower()}", level.ToString());
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={eventArgs.Channel.ToString()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevel[(int)eventArgs.Channel] = (int)(eventArgs.Volume * 100f);
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{eventArgs.Channel.ToString().ToLower()}", ((int)(eventArgs.Volume * 100f)).ToString());
            }
            TriggerEvent("tp_steelseries-gg_state_last_updated_volume", eventArgs.Channel.ToString());
        }
        else
        {
            Console.WriteLine(eventArgs.Channel + ", " + eventArgs.Mix + " volume changed, updating connectors/sliders...");
            if ((eventArgs.Channel == Channel.MASTER || eventArgs.Volume > _sonarManager.VolumeSettings.GetVolume(Channel.MASTER, (Mix)eventArgs.Mix!)) && eventArgs.Channel != Channel.MIC)
            {
                foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
                {
                    if (eventArgs.Channel == Channel.MIC) continue;
                    foreach (var mix in Enum.GetValues(typeof(Mix)).Cast<Mix>())
                    {
                        var level = (int)(_sonarManager.VolumeSettings.GetVolume(channel, mix) * 100f);
                        if (_connectorsLevelStreamer[(int) channel][(int) mix] == level) continue;
                        _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|mix={mix.ToString().ToLowerFirstUpper()}|channel={channel.ToString().ToLowerFirstUpper()}", level);
                        _connectorsLevelStreamer[(int) channel][(int) mix] = level;
                        _client.StateUpdate($"tp_steelseries-gg_state_volume_{mix.ToString().ToLower()}_{channel.ToString().ToLower()}", level.ToString());
                    }
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|mix={eventArgs.Mix.ToString()!.ToLowerFirstUpper()}|channel={eventArgs.Channel.ToString().ToLowerFirstUpper()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevelStreamer[(int) eventArgs.Channel][(int) eventArgs.Mix!] = (int)(eventArgs.Volume * 100f);
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{eventArgs.Mix.ToString()!.ToLower()}_{eventArgs.Channel.ToString().ToLower()}", ((int)(eventArgs.Volume * 100f)).ToString());
            }
            TriggerEvent("tp_steelseries-gg_state_last_updated_volume", $"{eventArgs.Mix.ToString()} - {eventArgs.Channel.ToString()}");
        }
    }

    bool _chatMixHadEvent = false;
    void OnChatMixChangeHandler(object? sender, SonarChatMixEvent eventArgs)
    {
        if (!_chatMixHadEvent)
        {
            _chatMixHadEvent = true;
            Console.WriteLine("ChatMix balance changed");
            _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((eventArgs.Balance * 100f) + 1) * 50));
            _client.StateUpdate("tp_steelseries-gg_state_chatmix_balance", eventArgs.Balance.ToString(CultureInfo.InvariantCulture));
            _client.TriggerEvent("tp_steelseries-gg_event_on_chatmix_balance");
        }
        else _chatMixHadEvent = false;
    }

    void OnMuteChangeHandler(object? sender, SonarMuteEvent eventArgs)
    {
        Console.WriteLine((eventArgs.Muted ? "Muted " : "Unmuted ") + eventArgs.Channel + " " + eventArgs.Mix);
        _client.StateUpdate($"tp_steelseries-gg_state_mute_{(eventArgs.Mix.HasValue ? $"{eventArgs.Mix.ToString()!.ToLower()}_{eventArgs.Channel.ToString().ToLower()}" : eventArgs.Channel.ToString().ToLower())}", eventArgs.Muted ? "Muted" : "Unmuted");
        TriggerEvent($"tp_steelseries-gg_state_last_updated_{(eventArgs.Muted ? "mute" : "unmute")}", eventArgs.Mix.HasValue ? $"{eventArgs.Mix.ToString()} - {eventArgs.Channel.ToString()}" : eventArgs.Channel.ToString());
    }

    void OnConfigChangeHandler(object? sender, SonarConfigEvent eventArgs)
    {
        SonarAudioConfiguration newConfig = _sonarManager.Configurations.GetAudioConfiguration(eventArgs.ConfigId);
        Console.WriteLine("Changed " + newConfig.AssociatedChannel + " config to " + _sonarManager.Configurations.GetSelectedAudioConfiguration(newConfig.AssociatedChannel).Name);
        _client.StateUpdate($"tp_steelseries-gg_state_config_{newConfig.AssociatedChannel.ToString().ToLower()}", _sonarManager.Configurations.GetSelectedAudioConfiguration(newConfig.AssociatedChannel).Name);
        TriggerEvent("tp_steelseries-gg_state_last_updated_config", newConfig.AssociatedChannel.ToString());
    }

    void OnPlaybackDeviceChangeHandler(object? sender, SonarPlaybackDeviceEvent eventArgs)
    {
        Console.WriteLine(eventArgs.Channel + " " + eventArgs.Mix +" Playback device changed");
        _client.StateUpdate($"tp_steelseries-gg_state_playback_device_{eventArgs.Channel.ToString().ToLower()}{eventArgs.Mix.ToString().ToLower()}", _sonarManager.PlaybackDevices.GetPlaybackDevice(eventArgs.PlaybackDeviceId).Name);
        Thread.Sleep(100);
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_state", _sonarManager.ChatMix.GetState() ? "Enabled" : "Disabled");
        TriggerEvent("tp_steelseries-gg_state_last_updated_playback_device", eventArgs.Channel == Channel.MIC ? eventArgs.Mode + " - Mic" : eventArgs.Mix.HasValue ? eventArgs.Mix.ToString() : eventArgs.Channel.ToString());
    }

    void OnRoutedProcessChangeHandler(object? sender, SonarRoutedProcessEvent eventArgs)
    {
        Console.WriteLine("Process " + eventArgs.ProcessId + " routed to " + eventArgs.NewChannel);
    }

    void OnMixChangeHandler(object? sender, SonarMixEvent eventArgs)
    {
        Console.WriteLine("Mix " + eventArgs.Channel + ", " + eventArgs.Mix + " " + (eventArgs.NewState ? "Enabled" : "Disabled"));
        _client.StateUpdate($"tp_steelseries-gg_state_mix_{eventArgs.Mix.ToString().ToLower()}_{eventArgs.Channel.ToString().ToLower()}", eventArgs.NewState ? "Enabled" : "Disabled");
        TriggerEvent("tp_steelseries-gg_state_last_updated_mix", eventArgs.Mix + " - " + eventArgs.Channel);
    }

    bool _audienceMonitoringLastState = false;
    void OnAudienceMonitoringChangeHandler(object? sender, SonarAudienceMonitoringEvent eventArgs)
    {
        if (_audienceMonitoringLastState != eventArgs.NewState)
        {
            _audienceMonitoringLastState = eventArgs.NewState;
            Console.WriteLine("Adience monitoring " + (eventArgs.NewState ? "Enabled" : "Disabled"));
            _client.StateUpdate("tp_steelseries-gg_state_audience_monitoring",
                eventArgs.NewState ? "Enabled" : "Disabled");
            _client.TriggerEvent("tp_steelseries-gg_event_on_audience_monitoring");
        }
    }

    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message)
    {
        // Open latest version download page on notification click
        if (message.OptionId == "tp_steelseries-gg_new_update_dl")
        {
            Console.WriteLine("Opening: https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest");
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest",
                UseShellExecute = true
            });
        }
    }

    async Task CheckNewerVersion()
    {
        var client = new GitHubClient(new ProductHeaderValue("DataNext27"));
        Release latestRelease = await client.Repository.Release.GetLatest("DataNext27", "TouchPortal_SteelSeriesGG");
        Version latestVersion = new Version(latestRelease.TagName);
        Version currentVersion = new Version(_version);
        
        int versionCompare = latestVersion.CompareTo(currentVersion);
        if (versionCompare > 0)
        {
            Console.WriteLine("A new update is available!");
            // Send notification
            _client.ShowNotification(
                "tp_steelseries-gg_new_update_" + latestVersion + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                "SteelSeries GG Plugin New Update Available",
                "Current Installed version: " + currentVersion + 
                "\nNew version: " + latestVersion + 
                "\n\nPlease update to get new features and bug fixes!",
                new[]
                {
                    new NotificationOptions() {Id = "tp_steelseries-gg_new_update_dl", Title = "Go To Download Location"}
                });
        }
        else if (versionCompare < 0)
        {
            Console.WriteLine("You are using a pre-release version!");
        }
        else
        {
            Console.WriteLine("Up to date!");
        }
    }
    
    private void PipeMonitoring()
    {
        // Create the pipe
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),   
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),   
            PipeAccessRights.FullControl, AccessControlType.Allow));
        var _monitoringPipeServer = NamedPipeServerStreamAcl.Create("TP_steelseries-gg_plugin_monitoring", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.None, 0, 0, pipeSecurity);
        _monitoringPipeServer.WaitForConnection();
        var reader = new StreamReader(_monitoringPipeServer);
        // Continuously check that the pipe is alive 
        try
        {
            while (true)
            {
                string log = reader.ReadLine();
                if (log == null)
                {
                    // Server died so we kill this app
                    break;
                }
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Logger died: {ex.Message}");
        }
        pipeWriter.Close();
        _pipeClient.Close();
        _sonarManager.StopListener();
        Environment.Exit(0);
    }
}

public static class StringExtensions
{
    public static string ToLowerFirstUpper(this string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return str;
        str = str.ToLower();
        return char.ToUpper(str[0]) + str.Substring(1);
    }
}