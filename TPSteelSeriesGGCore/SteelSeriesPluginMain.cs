using System.Runtime.InteropServices;
using System.Diagnostics;
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

namespace TPSteelSeriesGGCore;

public class SteelSeriesPluginMain : ITouchPortalEventHandler
{
    private readonly string _version = "2.0.0";
    public string PluginId => "steelseries-gg";

    private readonly ITouchPortalClient _client;
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
        _sonarManager = new SonarBridge();
    }

    public async void Run()
    {
        _client.Connect();
        await CheckNewerVersion();
        _sonarManager.WaitUntilSonarStarted();
        Console.WriteLine(new SonarRetriever().WebServerAddress());
        
        _sonarManager.StartListener();
        _sonarManager.SonarEventManager.OnSonarModeChange += OnModeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarVolumeChange += OnVolumeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarChatMixChange += OnChatMixChangeHandler;
        
        InitializeConnectors();
        Console.WriteLine("Initialized!");
    }

    void InitializeConnectors()
    {
        // Initialize sliders
        foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
        {
            var level = (int)(_sonarManager.GetVolume(device) * 100f);
            _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={device.ToString()}", level);
            _connectorsLevel[(int) device] = level;
            
            foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
            {
                level = (int)(_sonarManager.GetVolume(device, channel) * 100f);
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={channel.ToString()}|device={device.ToString()}", level);
                _connectorsLevelStreamer[(int) device][(int) channel] = level;
            }
        }
        
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((_sonarManager.GetChatMixBalance() * 100f)+1)*50));
    }
    
    public void OnClosedEvent(string message)
    {
        _sonarManager.StopListener();
        Environment.Exit(0);
    }

    public void OnActionEvent(ActionEvent message)
    {
        switch (message.ActionId)
        {
            case "tp_steelseries-gg_switch_mode":
                if (_sonarManager.GetMode() == Mode.Classic) _sonarManager.SetMode(Mode.Streamer);
                else _sonarManager.SetMode(Mode.Classic);
                Console.WriteLine("Switched mode.");
                break;
            
            case "tp_steelseries-gg_set_mode":
                _sonarManager.SetMode((Mode)Enum.Parse(typeof(Mode), message["mode"], true));
                Console.WriteLine("Mode set to " + message["mode"]);
                break;
            
            case "tp_steelseries-gg_set_classic_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true));
                Console.WriteLine(message["action"]+"d classic mute on " + message["device"]);
                break;
            
            case "tp_steelseries-gg_set_streamer_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                Console.WriteLine(message["action"]+"d streamer mute on " + message["device"] + ", " + message["channel"]);
                break;
            
            case "tp_steelseries-gg_set_config":
                _sonarManager.SetConfig((Device)Enum.Parse(typeof(Device), message["device"], true), message["config"]);
                Console.WriteLine("Changed " + message["device"] + " config to " + message["config"] );
                break;
            
            case "tp_steelseries-gg_set_classic_redirections_devices":
                _sonarManager.SetClassicRedirectionDevice(message["device"] != "Mic" ? _sonarManager.GetRedirectionDevices(Direction.Output).First(device => device.Name == message["redirectionDevice"]).Id : _sonarManager.GetRedirectionDevices(Direction.Input).First(device => device.Name == message["redirectionDevice"]).Id, (Device)Enum.Parse(typeof(Device), message["device"], true));
                Console.WriteLine("Changed " + message["device"] + " classic mode redirection device to " + message["redirectionDevice"] );
                break;
            
            case "tp_steelseries-gg_set_streamer_redirections_devices":
                if(message["device-channel"] != "Mic") _sonarManager.SetStreamRedirectionDevice(_sonarManager.GetRedirectionDevices(Direction.Output).First(device => device.Name == message["redirectionDevice"]).Id, (Channel)Enum.Parse(typeof(Channel), message["device-channel"], true));
                else _sonarManager.SetStreamRedirectionDevice(_sonarManager.GetRedirectionDevices(Direction.Input).First(device => device.Name == message["redirectionDevice"]).Id, (Device)Enum.Parse(typeof(Device), message["device-channel"], true));
                Console.WriteLine("Changed " + message["device-channel"] + " streamer mode redirection device to " + message["redirectionDevice"] );
                break;
            
            case "tp_steelseries-gg_set_redirections_states":
                if(message["action"] == "Toggle") _sonarManager.SetRedirectionState(!_sonarManager.GetRedirectionState((Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else if (message["action"] == "Enable") _sonarManager.SetRedirectionState(true, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else _sonarManager.SetRedirectionState(false, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                Console.WriteLine(message["action"]+"d redirection state on " + message["device"] + ", " + message["channel"]);
                break;
            
            case "tp_steelseries-gg_set_audience_monitoring":
                if (message["action"] == "Toggle") _sonarManager.SetAudienceMonitoringState(!_sonarManager.GetAudienceMonitoringState());
                else if (message["action"] == "Enable") _sonarManager.SetAudienceMonitoringState(true);
                else _sonarManager.SetAudienceMonitoringState(false);
                Console.WriteLine(message["action"] +"d audience monitoring");
                break;
            
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
            case "tp_steelseries-gg_classic_set_volume":
                _sonarManager.SetVolume(message.Value / 100f, (Device)Enum.Parse(typeof(Device), message["device"], true)); 
                break;
            
            case "tp_steelseries-gg_stream_set_volume":
                _sonarManager.SetVolume(message.Value / 100f,
                    (Device)Enum.Parse(typeof(Device), message["device"], true),
                    (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                break;
            
            case "tp_steelseries-gg_set_chatmix_balance":
                if (_sonarManager.GetChatMixState()) { _sonarManager.SetChatMixBalance((message.Value / 100f) * (1 - -1) + -1); }
                else
                {
                    Thread.Sleep(500);
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
            case "tp_steelseries-gg_set_config":
                switch (message.ListId)
                {
                    case "device":
                        _client.ChoiceUpdate("config", _sonarManager.GetAudioConfigurations((Device)Enum.Parse(typeof(Device), message.Value, true)).Select(config => config.Name).ToArray());
                        break;  
                }
                break;
            
            case "tp_steelseries-gg_set_classic_redirections_devices":
                switch (message.ListId)
                {
                    case "device":
                        if (message.Value != "Mic") _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Output).Select(device => device.Name).ToArray());
                        else _client.ChoiceUpdate("redirectionDevice", _sonarManager.GetRedirectionDevices(Direction.Input).Select(device => device.Name).ToArray());
                        break;
                }
                break;
            
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
        InitializeConnectors();
    }

    void OnModeChangeHandler(object? sender, SonarModeEvent eventArgs)
    {
        InitializeConnectors();
    }
    
    void OnVolumeChangeHandler(object? sender, SonarVolumeEvent eventArgs)
    {
        // Update Connectors
        if (eventArgs.Mode == Mode.Classic)
        {
            Console.WriteLine(eventArgs.Device + " volume changed, updating connectors/sliders...");
            if ((eventArgs.Device == Device.Master || eventArgs.Volume > _sonarManager.GetVolume(Device.Master)) && eventArgs.Device != Device.Mic)
            {
                foreach (var device in Enum.GetValues(typeof(Device)).Cast<Device>())
                {
                    if (eventArgs.Device == Device.Mic) continue;
                    var level = (int)(_sonarManager.GetVolume(device) * 100f);
                    if (_connectorsLevel[(int) device] == level) continue;
                    _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={device.ToString()}", level);
                    _connectorsLevel[(int)device] = level;
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevel[(int)eventArgs.Device] = (int)(eventArgs.Volume * 100f);
            }
        }
        else
        {
            Console.WriteLine(eventArgs.Device + ", " + eventArgs.Channel + " volume changed, updating connectors/sliders...");
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
                    }
                }
            }
            else
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={eventArgs.Channel.ToString()}|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
                _connectorsLevelStreamer[(int) eventArgs.Device][(int) eventArgs.Channel!] = (int)(eventArgs.Volume * 100f);
            }
        }
    }

    void OnChatMixChangeHandler(object? sender, SonarChatMixEvent eventArgs)
    {
        Console.WriteLine("ChatMix balance changed, updating connectors...");
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((eventArgs.Balance * 100f)+1)*50));
    }

    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message)
    {
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
}