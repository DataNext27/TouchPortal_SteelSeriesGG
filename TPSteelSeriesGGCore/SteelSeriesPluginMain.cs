using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SteelSeriesAPI;
using SteelSeriesAPI.Events;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;
using TouchPortalSDK.Messages.Models;

using Octokit;
using NAudio.CoreAudioApi;
using SteelSeriesAPI.Sonar.Models;

namespace TPSteelSeriesGGCore;

public class SteelSeriesPluginMain : ITouchPortalEventHandler
{
    private readonly string _version = "2.0.0";
    public string PluginId => "steelseries-gg";

    private readonly ITouchPortalClient _client;
    private NamedPipeClientStream _pipeClient;
    private StreamWriter _writer;
    private readonly SonarBridge _sonarManager;
    
    // Keep track of connectors level
    // Master, Game, Chat, Media, Aux, Mic
    private int[] _connectorsLevel = [-1, -1, -1, -1, -1, -1];
    private int[][] _connectorsLevelStreamer = [[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1]];

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    public SteelSeriesPluginMain()
    {
        _client = TouchPortalFactory.CreateClient(this);
        _pipeClient = new NamedPipeClientStream(".", "TP_steelseries-gg_plugin_logging", PipeDirection.InOut);
        _sonarManager = new SonarBridge();
    }

    public async void Run()
    {
        // Connect to TP
        _client.Connect();
        
        // Create and Connect to Logger Pipe
        Thread pipeMonitor = new Thread(PipeMonitoring);
        pipeMonitor.Start();
        _pipeClient.Connect();
        _writer = new StreamWriter(_pipeClient) { AutoFlush = true };
        
        // Check for new versions
        await CheckNewerVersion();
        _sonarManager.WaitUntilSonarStarted();
        Log(new SonarRetriever().WebServerAddress());
        
        // Listen for Sonar Events
        _sonarManager.StartListener();
        _sonarManager.SonarEventManager.OnSonarModeChange += OnModeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarVolumeChange += OnVolumeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarChatMixChange += OnChatMixChangeHandler;
        _sonarManager.SonarEventManager.OnSonarMuteChange += OnMuteChangeHandler;
        _sonarManager.SonarEventManager.OnSonarConfigChange += OnConfigChangeHandler;
        _sonarManager.SonarEventManager.OnSonarRedirectionDeviceChange += OnRedirectionDeviceChangeHandler;
        _sonarManager.SonarEventManager.OnSonarRedirectionStateChange += OnRedirectionStateChangeHandler;
        _sonarManager.SonarEventManager.OnSonarAudienceMonitoringChange += OnAudienceMonitoringChangeHandler;

        InitializeConnectors();
        InitializeStates();
        Log("Initialized!");
    }

    void InitializeConnectors()
    {
        // Initialize sliders
        foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
        {
            // Classic
            var level = (int)(_sonarManager.GetVolume(device) * 100f);
            _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={device.ToString()}", level);
            _connectorsLevel[(int) device] = level;
            
            // Streamer
            foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
            {
                level = (int)(_sonarManager.GetVolume(device, channel) * 100f);
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={channel.ToString()}|device={device.ToString()}", level);
                _connectorsLevelStreamer[(int) device][(int) channel] = level;
            }
        }
        
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((_sonarManager.GetChatMixBalance() * 100f)+1)*50));
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

        // Display States
        _client.StateUpdate("tp_steelseries-gg_state_mode", _sonarManager.GetMode().ToString());
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_state", _sonarManager.GetChatMixState() ? "Enabled" : "Disabled");
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_balance", _sonarManager.GetChatMixBalance().ToString(CultureInfo.InvariantCulture));
        _client.StateUpdate("tp_steelseries-gg_state_audience_monitoring", _sonarManager.GetAudienceMonitoringState() ? "Enabled" : "Disabled");
        foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
        {
            _client.StateUpdate($"tp_steelseries-gg_state_volume_{device.ToString().ToLower()}", _connectorsLevel[(int) device].ToString());
            _client.StateUpdate($"tp_steelseries-gg_state_mute_{device.ToString().ToLower()}", _sonarManager.GetMute(device) ? "Muted" : "Unmuted");

            if (device != Device.Master)
            {
                _client.StateUpdate($"tp_steelseries-gg_state_config_{device.ToString().ToLower()}", _sonarManager.GetSelectedAudioConfiguration(device).Name);
                _client.StateUpdate($"tp_steelseries-gg_state_redirection_device_{device.ToString().ToLower()}", _sonarManager.GetClassicRedirectionDevice(device).Name);
            }
            
            foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
            {
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{channel.ToString().ToLower()}_{device.ToString().ToLower()}", _connectorsLevelStreamer[(int) device][(int) channel].ToString());
                _client.StateUpdate($"tp_steelseries-gg_state_mute_{channel.ToString().ToLower()}_{device.ToString().ToLower()}", _sonarManager.GetMute(device, channel) ? "Muted" : "Unmuted");
                _client.StateUpdate($"tp_steelseries-gg_state_redirection_device_{channel.ToString().ToLower()}", _sonarManager.GetStreamRedirectionDevice(channel).Name);
                if (device != Device.Master)
                {   
                    _client.StateUpdate($"tp_steelseries-gg_state_redirection_state_{channel.ToString().ToLower()}_{device.ToString().ToLower()}", _sonarManager.GetRedirectionState(device, channel) ? "Enabled" : "Disabled");
                }
            }
        }

        // Change Mic Redirection Device state depending on the mode
        if (_sonarManager.GetMode() == Mode.Classic)_client.StateUpdate("tp_steelseries-gg_state_redirection_device_mic", _sonarManager.GetClassicRedirectionDevice(Device.Mic).Name);
        else _client.StateUpdate("tp_steelseries-gg_state_redirection_device_mic", _sonarManager.GetStreamRedirectionDevice(Device.Mic).Name);
    }
    
    public void OnClosedEvent(string message)
    {
        // Exit the app on TP Close
        _writer.Close();
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
                if (_sonarManager.GetMode() == Mode.Classic) _sonarManager.SetMode(Mode.Streamer);
                else _sonarManager.SetMode(Mode.Classic);
                Log("Switched mode.");
                break;
            
            // Set Specific mode
            case "tp_steelseries-gg_set_mode":
                _sonarManager.SetMode((Mode)Enum.Parse(typeof(Mode), message["mode"], true));
                Log("Mode set to " + message["mode"]);
                break;
            
            // Toggle/Mute/Unmute a classic device
            case "tp_steelseries-gg_set_classic_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true));
                Log(message["action"]+"d " + message["device"]);
                break;
            
            // Toggle/Mute/Unmute a streamer device
            case "tp_steelseries-gg_set_streamer_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                Log(message["action"]+"d streamer mute on " + message["device"] + ", " + message["channel"]);
                break;
            
            // Change config of a Sonar device
            case "tp_steelseries-gg_set_config":
                _sonarManager.SetConfig((Device)Enum.Parse(typeof(Device), message["device"], true), message["config"]);
                Log("Changed " + message["device"] + " config to " + message["config"] );
                break;
            
            // Change Redirection Device of a classic Sonar Device
            case "tp_steelseries-gg_set_classic_redirections_devices":
                _sonarManager.SetClassicRedirectionDevice(message["device"] != "Mic" ? _sonarManager.GetRedirectionDevices(Direction.Output).First(device => device.Name == message["redirectionDevice"]).Id : _sonarManager.GetRedirectionDevices(Direction.Input).First(device => device.Name == message["redirectionDevice"]).Id, (Device)Enum.Parse(typeof(Device), message["device"], true));
                Log("Changed " + message["device"] + " classic mode redirection device to " + message["redirectionDevice"] );
                break;
            
            // Change Redirection Device of a streamer Sonar Device/Channel
            case "tp_steelseries-gg_set_streamer_redirections_devices":
                if(message["device-channel"] != "Mic") _sonarManager.SetStreamRedirectionDevice(_sonarManager.GetRedirectionDevices(Direction.Output).First(device => device.Name == message["redirectionDevice"]).Id, (Channel)Enum.Parse(typeof(Channel), message["device-channel"], true));
                else _sonarManager.SetStreamRedirectionDevice(_sonarManager.GetRedirectionDevices(Direction.Input).First(device => device.Name == message["redirectionDevice"]).Id, (Device)Enum.Parse(typeof(Device), message["device-channel"], true));
                Log("Changed " + message["device-channel"] + " streamer mode redirection device to " + message["redirectionDevice"] );
                break;
            
            // Toggle/Enable/Disable streamer redirection states
            case "tp_steelseries-gg_set_redirections_states":
                if(message["action"] == "Toggle") _sonarManager.SetRedirectionState(!_sonarManager.GetRedirectionState((Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else if (message["action"] == "Enable") _sonarManager.SetRedirectionState(true, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else _sonarManager.SetRedirectionState(false, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                Log(message["action"]+"d redirection state on " + message["device"] + ", " + message["channel"]);
                break;
            
            // Toggle/Enable/Disable streamer Audience Monitoring
            case "tp_steelseries-gg_set_audience_monitoring":
                if (message["action"] == "Toggle") _sonarManager.SetAudienceMonitoringState(!_sonarManager.GetAudienceMonitoringState());
                else if (message["action"] == "Enable") _sonarManager.SetAudienceMonitoringState(true);
                else _sonarManager.SetAudienceMonitoringState(false);
                Log(message["action"] +"d audience monitoring");
                break;
            
            // Route current window audio to a specific Sonar Device
            case "tp_steelseries_route_active_process":
                // Get active window
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return;
                GetWindowThreadProcessId(hWnd, out uint activeWindowProcessId);
                if (activeWindowProcessId == 0) return;
                Log($"Active window process ID: {activeWindowProcessId}");

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
                        Log($"Routed pID {sessionProcessId} to {message["device"]}.");
                        _sonarManager.SetProcessToDeviceRouting((int)sessionProcessId,(Device)Enum.Parse(typeof(Device), message["device"], true));
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
                _sonarManager.SetVolume(message.Value / 100f, (Device)Enum.Parse(typeof(Device), message["device"], true)); 
                break;
            
            // Set streamer volumes with sliders
            case "tp_steelseries-gg_stream_set_volume":
                _sonarManager.SetVolume(message.Value / 100f,
                    (Device)Enum.Parse(typeof(Device), message["device"], true),
                    (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                break;
            
            // Change ChatMix balance if possible
            case "tp_steelseries-gg_set_chatmix_balance":
                if (_sonarManager.GetChatMixState()) { _sonarManager.SetChatMixBalance((message.Value / 100f) * (1 - -1) + -1); }
                else
                {
                    Thread.Sleep(500); // Prevent error E3081
                    _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", 50);
                    Log("Could not change ChatMix balance");
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
                        _client.ChoiceUpdate("config", _sonarManager.GetAudioConfigurations((Device)Enum.Parse(typeof(Device), message.Value, true)).Select(config => config.Name).ToArray());
                        break;  
                }
                break;
            
            // List devices depending on the data flow of the Sonar Device (Input/Output)
            case "tp_steelseries-gg_set_classic_redirections_devices":
                switch (message.ListId)
                {
                    case "device":
                        if (message.Value != "Mic") _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Output).Select(device => device.Name).ToArray());
                        else _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Input).Select(device => device.Name).ToArray());
                        break;
                }
                break;
            
            // List devices depending on the data flow of the Sonar Device (Input/Output)
            case "tp_steelseries-gg_set_streamer_redirections_devices":
                switch (message.ListId)
                {
                    case "device-channel":
                        if (message.Value != "Mic") _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Output).Select(device => device.Name).ToArray());
                        else _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Input).Select(device => device.Name).ToArray());
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
        Log("Mode changed.");
        Thread.Sleep(150);
        InitializeConnectors();
        InitializeStates();
        _client.TriggerEvent("tp_steelseries-gg_event_on_mode");
    }
    
    void OnVolumeChangeHandler(object? sender, SonarVolumeEvent eventArgs)
    {
        // Update Connectors
        if (eventArgs.Mode == Mode.Classic)
        {
            Log(eventArgs.Device + " volume changed, updating connectors/sliders...");
            if ((eventArgs.Device == Device.Master || eventArgs.Volume > _sonarManager.GetVolume(Device.Master)) && eventArgs.Device != Device.Mic)
            {
                foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
                {
                    if (eventArgs.Device == Device.Mic) continue;
                    var level = (int)(_sonarManager.GetVolume(device) * 100f);
                    if (_connectorsLevel[(int) device] == level) continue;
                    _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={device.ToString()}", level);
                    _connectorsLevel[(int)device] = level;
                    _client.StateUpdate($"tp_steelseries-gg_state_volume_{device.ToString().ToLower()}", level.ToString());
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevel[(int)eventArgs.Device] = (int)(eventArgs.Volume * 100f);
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{eventArgs.Device.ToString().ToLower()}", ((int)(eventArgs.Volume * 100f)).ToString());
            }
            TriggerEvent("tp_steelseries-gg_state_last_updated_volume", eventArgs.Device.ToString());
        }
        else
        {
            Log(eventArgs.Device + ", " + eventArgs.Channel + " volume changed, updating connectors/sliders...");
            if ((eventArgs.Device == Device.Master || eventArgs.Volume > _sonarManager.GetVolume(Device.Master, (Channel)eventArgs.Channel!)) && eventArgs.Device != Device.Mic)
            {
                foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
                {
                    if (eventArgs.Device == Device.Mic) continue;
                    foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
                    {
                        var level = (int)(_sonarManager.GetVolume(device, channel) * 100f);
                        if (_connectorsLevelStreamer[(int) device][(int) channel] == level) continue;
                        _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={channel.ToString()}|device={device.ToString()}", level);
                        _connectorsLevelStreamer[(int) device][(int) channel] = level;
                        _client.StateUpdate($"tp_steelseries-gg_state_volume_{channel.ToString().ToLower()}_{device.ToString().ToLower()}", level.ToString());
                    }
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={eventArgs.Channel.ToString()}|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevelStreamer[(int) eventArgs.Device][(int) eventArgs.Channel!] = (int)(eventArgs.Volume * 100f);
                _client.StateUpdate($"tp_steelseries-gg_state_volume_{eventArgs.Channel.ToString()!.ToLower()}_{eventArgs.Device.ToString().ToLower()}", ((int)(eventArgs.Volume * 100f)).ToString());
            }
            TriggerEvent("tp_steelseries-gg_state_last_updated_volume", $"{eventArgs.Channel.ToString()} - {eventArgs.Device.ToString()}");
        }
    }

    bool _chatMixHadEvent = false;
    void OnChatMixChangeHandler(object? sender, SonarChatMixEvent eventArgs)
    {
        if (!_chatMixHadEvent)
        {
            _chatMixHadEvent = true;
            Log("ChatMix balance changed");
            _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((eventArgs.Balance * 100f) + 1) * 50));
            _client.StateUpdate("tp_steelseries-gg_state_chatmix_balance", eventArgs.Balance.ToString(CultureInfo.InvariantCulture));
            _client.TriggerEvent("tp_steelseries-gg_event_on_chatmix_balance");
        }
        else _chatMixHadEvent = false;
    }

    void OnMuteChangeHandler(object? sender, SonarMuteEvent eventArgs)
    {
        Log((eventArgs.Muted ? "Muted " : "Unmuted ") + eventArgs.Device + " " + eventArgs.Channel);
        _client.StateUpdate($"tp_steelseries-gg_state_mute_{(eventArgs.Channel.HasValue ? $"{eventArgs.Channel.ToString()!.ToLower()}_{eventArgs.Device.ToString().ToLower()}" : eventArgs.Device.ToString().ToLower())}", eventArgs.Muted ? "Muted" : "Unmuted");
        TriggerEvent($"tp_steelseries-gg_state_last_updated_{(eventArgs.Muted ? "mute" : "unmute")}", eventArgs.Channel.HasValue ? $"{eventArgs.Channel.ToString()} - {eventArgs.Device.ToString()}" : eventArgs.Device.ToString());
    }

    void OnConfigChangeHandler(object? sender, SonarConfigEvent eventArgs)
    {
        SonarAudioConfiguration newConfig = _sonarManager.GetAudioConfiguration(eventArgs.ConfigId);
        Log("Changed " + newConfig.AssociatedDevice + " config to " + _sonarManager.GetSelectedAudioConfiguration(newConfig.AssociatedDevice).Name);
        _client.StateUpdate($"tp_steelseries-gg_state_config_{newConfig.AssociatedDevice.ToString().ToLower()}", _sonarManager.GetSelectedAudioConfiguration(newConfig.AssociatedDevice).Name);
        TriggerEvent("tp_steelseries-gg_state_last_updated_config", newConfig.AssociatedDevice.ToString());
    }

    void OnRedirectionDeviceChangeHandler(object? sender, SonarRedirectionDeviceEvent eventArgs)
    {
        Log(eventArgs.Device + " " + eventArgs.Channel +" Redirection device changed");
        _client.StateUpdate($"tp_steelseries-gg_state_redirection_device_{eventArgs.Device.ToString().ToLower()}{eventArgs.Channel.ToString().ToLower()}", _sonarManager.GetRedirectionDeviceFromId(eventArgs.RedirectionDeviceId).Name);
        Thread.Sleep(100);
        _client.StateUpdate("tp_steelseries-gg_state_chatmix_state", _sonarManager.GetChatMixState() ? "Enabled" : "Disabled");
    }

    void OnRedirectionStateChangeHandler(object? sender, SonarRedirectionStateEvent eventArgs)
    {
        Log("Redirection State " + eventArgs.Device + ", " + eventArgs.Channel + " " + (eventArgs.State ? "Enabled" : "Disabled"));
        _client.StateUpdate($"tp_steelseries-gg_state_redirection_state_{eventArgs.Channel.ToString().ToLower()}_{eventArgs.Device.ToString().ToLower()}", eventArgs.State ? "Enabled" : "Disabled");
    }

    void OnAudienceMonitoringChangeHandler(object? sender, SonarAudienceMonitoringEvent eventArgs)
    {
        Log("Adience monitoring " + (eventArgs.AudienceMonitoringState ? "Enabled" : "Disabled"));
        _client.StateUpdate("tp_steelseries-gg_state_audience_monitoring", eventArgs.AudienceMonitoringState ? "Enabled" : "Disabled");
    }

    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message)
    {
        // Open latest version download page on notification click
        if (message.OptionId == "tp_steelseries-gg_new_update_dl")
        {
            Log("Opening: https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest");
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
            Log("A new update is available!");
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
            Log("You are using a pre-release version!");
        }
        else
        {
            Log("Up to date!");
        }
    }

    private void Log(string log)
    {
        _writer.WriteLine(log);
        Console.WriteLine(log);
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
            Log($"Logger died: {ex.Message}");
        }
        _writer.Close();
        _pipeClient.Close();
        _sonarManager.StopListener();
        Environment.Exit(0);
    }
}