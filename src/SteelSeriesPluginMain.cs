using System.Diagnostics;
using System.Globalization;
using Octokit;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;
using TouchPortalSDK.Messages.Models;
using TPSteelSeriesGG.SteelSeriesAPI;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHTTPHandler;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesJsonParser;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHttpActionHandler;

namespace TPSteelSeriesGG;

public class SteelSeriesPluginMain : ITouchPortalEventHandler
{
    private string version = "1.1.1";
    private string latestReleaseUrl;
    private OnSteelSeriesEventArgs _lastEventArgs;
    
    string _muteStatesNames;
    string _redirectionStatesNames;
    string _audienceMonitoringStatesNames;

    public string PluginId => "steelseries-gg";

    private readonly ITouchPortalClient _client;
    
    public SteelSeriesPluginMain()
    {
        _client = TouchPortalFactory.CreateClient(this);
    }

    public async void Run()
    {
        Console.WriteLine("Sonar WebServer Address: " + GetSonarWebServerAddress());
        new Thread(StartSteelSeriesListener).Start();
        OnSteelSeriesEvent += OnSteelSeriesEventHandler;
        _client.Connect();
        Initialize();
        await CheckGitHubNewerVersion();
    }

    public void OnClosedEvent(string message)
    {
        Environment.Exit(0);
    }
    
    public void Initialize()
    {
        //Update Connectors on startup
        foreach (var device in Enum.GetNames(typeof(MixDevices)))
        {
            switch (GetMode())
            {
                case Mode.Classic:
                    _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|mixerchoice={device}", (int)(GetVolume(Enum.Parse<MixDevices>(device)) * 100));
                    break;
                case Mode.Stream:
                    foreach (var mode in Enum.GetNames(typeof(StreamerMode)))
                    {
                        if (mode == "None") continue;
                        
                        var tempMode = mode;
                        if (mode == "Streaming") tempMode = "Stream";
                        _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|streamermode={tempMode}|mixerchoice={device}", (int)(GetVolume(Enum.Parse<MixDevices>(device), Enum.Parse<StreamerMode>(mode)) * 100));
                    }
                    break;
            }
        }
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)((GetChatMixBalance()+1)*50));
        
        //Recreate and Update States on startup
        var virtualDevices = Enum.GetNames(typeof(MixDevices));

            //Remove States
        foreach (var device in virtualDevices)
        {
            _client.RemoveState($"tp_steelseries-gg_volume_{device}");
            _client.RemoveState($"tp_steelseries-gg_mute_state_{device}");
            if (device != "Master")
            {
                _client.RemoveState($"tp_steelseries-gg_configs_{device}");
                _client.RemoveState($"tp_steelseries-gg_redirection_device_{device}");
                _client.RemoveState($"tp_steelseries-gg_monitoring_redirection_state_{device}");
                _client.RemoveState($"tp_steelseries-gg_streaming_redirection_state_{device}");
            }
        }
        
            //Create States
        foreach (var device in virtualDevices) 
        {
            _client.CreateState($"tp_steelseries-gg_volume_{device}", $"{device}", "", "Volume");
            _client.CreateState($"tp_steelseries-gg_mute_state_{device}", $"{device}", "", "Mute state");
            if (device != "Master")
            {
                _client.CreateState($"tp_steelseries-gg_configs_{device}", $"{device}", "", "Configs");
                _client.CreateState($"tp_steelseries-gg_redirection_device_{device}", $"{device}", "", "Redirections Devices");
                _client.CreateState($"tp_steelseries-gg_monitoring_redirection_state_{device}", $"{device}", "", "Monitoring Redirections State");
                _client.CreateState($"tp_steelseries-gg_streaming_redirection_state_{device}", $"{device}", "", "Streaming Redirections State");
            }
        }
        
        // Update States
        _client.StateUpdate("tp_steelseries-gg_mode", GetMode().ToString());
        _client.StateUpdate("tp_steelseries-gg_chatmix_balance", GetChatMixBalance().ToString());
        _client.StateUpdate("tp_steelseries-gg_chatmix_state", GetChatMixState());
        _client.StateUpdate("tp_steelseries-gg_audience_monitoring_state", GetStringAudienceMonitoringState());

        
        foreach (var device in virtualDevices)
        {
            foreach (var mode in Enum.GetNames(typeof(StreamerMode)))
            {
                _client.StateUpdate($"tp_steelseries-gg_volume_{device}", ((int)(GetVolume(Enum.Parse<MixDevices>(device), Enum.Parse<StreamerMode>(mode)) * 100)).ToString());
                _client.StateUpdate($"tp_steelseries-gg_mute_state_{device}", GetMutedState(device, mode));
                if (device != "Master")
                {
                    _client.StateUpdate($"tp_steelseries-gg_configs_{device}", GetSelectedAudioConfiguration(Enum.Parse<MixDevices>(device)).Name);
                    _client.StateUpdate($"tp_steelseries-gg_redirection_device_{device}", GetSelectedRedirectionDevice(Enum.Parse<MixDevices>(device), Enum.Parse<StreamerMode>(mode)).Name);
                    _client.StateUpdate($"tp_steelseries-gg_monitoring_redirection_state_{device}", GetStringRedirectionState(StreamerMode.Monitoring, device));
                    _client.StateUpdate($"tp_steelseries-gg_streaming_redirection_state_{device}", GetStringRedirectionState(StreamerMode.Streaming, device));
                }
            }
        }
    }
    
    public string GetMutedState(string virtualDevice, string streamerMode = "None")
    {
        var state = GetMuted(Enum.Parse<MixDevices>(virtualDevice), Enum.Parse<StreamerMode>(streamerMode)).ToString();
        
        return BooleanToMuteState(state);
    }

    public string GetStringRedirectionState(StreamerMode streamerMode, string virtualDevice)
    {
        var state = GetRedirectionState(streamerMode, Enum.Parse<MixDevices>(virtualDevice)).ToString();

        return BooleanToRedirectionState(state);
    }

    public string GetStringAudienceMonitoringState()
    {
        var state = GetAudienceMonitoringState().ToString();

        return BooleanToAudienceMonitoringState(state);
    }

    public void OnSteelSeriesEventHandler(object sender, OnSteelSeriesEventArgs eventArgs)
    {
        if (eventArgs.Equals(_lastEventArgs))
        {
            return;
        }
        
        _lastEventArgs = eventArgs;
        
        // Console.WriteLine("" + eventArgs.Setting + " " + eventArgs.Mode + " " + eventArgs.StreamerMode + " " + eventArgs.MixDevice + " " + eventArgs.Value);
        
        switch (eventArgs.Setting)
        {
            case "mode":
                _client.StateUpdate("tp_steelseries-gg_mode", ((Mode)Enum.Parse(typeof(Mode), eventArgs.Value, true)).ToString());
                
                foreach (var v_device in Enum.GetNames(typeof(MixDevices)))
                {
                    switch (eventArgs.Value)
                    {
                        case "classic":
                            _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|mixerchoice={v_device}", (int)(GetVolume(Enum.Parse<MixDevices>(v_device)) * 100));
                            break;
                        case "stream":
                            foreach (var mode in Enum.GetNames(typeof(StreamerMode)))
                            {
                                if (mode == "None") continue;
                        
                                var tempMode = mode;
                                if (mode == "Streaming") tempMode = "Stream";
                                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|streamermode={tempMode}|mixerchoice={v_device}", (int)(GetVolume(Enum.Parse<MixDevices>(v_device), Enum.Parse<StreamerMode>(mode)) * 100));
                            }
                            break;
                    }
                    
                    foreach (var mode in Enum.GetNames(typeof(StreamerMode)))
                    {
                        _client.StateUpdate($"tp_steelseries-gg_volume_{v_device}", ((int)(GetVolume(Enum.Parse<MixDevices>(v_device), Enum.Parse<StreamerMode>(mode)) * 100)).ToString());
                        _client.StateUpdate($"tp_steelseries-gg_mute_state_{v_device}", GetMutedState(v_device, mode));
                        if (v_device != "Master")
                        {
                            _client.StateUpdate($"tp_steelseries-gg_redirection_device_{v_device}", GetSelectedRedirectionDevice(Enum.Parse<MixDevices>(v_device), Enum.Parse<StreamerMode>(mode)).Name);
                        }
                    }
                }
                
                break;
            case "volume":
                _client.StateUpdate($"tp_steelseries-gg_volume_{eventArgs.MixDevice}",((int)(float.Parse(eventArgs.Value, CultureInfo.InvariantCulture.NumberFormat) * 100)).ToString());
                switch (eventArgs.Mode)
                {
                    case Mode.Classic:
                        _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|mixerchoice={eventArgs.MixDevice}",
                            (int)(float.Parse(eventArgs.Value, CultureInfo.InvariantCulture.NumberFormat) * 100));
                        break;
                    case Mode.Stream:
                        if (eventArgs.StreamerMode == StreamerMode.Monitoring)
                        {
                            _client.ConnectorUpdate(
                                $"tp_steelseries-gg_stream_set_volume|streamermode=Monitoring|mixerchoice={eventArgs.MixDevice}",
                                (int)(float.Parse(eventArgs.Value, CultureInfo.InvariantCulture.NumberFormat) * 100));
                        }
                        else if(eventArgs.StreamerMode == StreamerMode.Streaming)
                        {
                            _client.ConnectorUpdate(
                                $"tp_steelseries-gg_stream_set_volume|streamermode=Stream|mixerchoice={eventArgs.MixDevice}",
                                (int)(float.Parse(eventArgs.Value, CultureInfo.InvariantCulture.NumberFormat) * 100));
                        }
                        break;
                }
                break;
            case "mute":
                _client.StateUpdate($"tp_steelseries-gg_mute_state_{eventArgs.MixDevice}", BooleanToMuteState(eventArgs.Value));
                break;
            case "config":
                MixDevices device = GetDeviceFromAudioConfiguration(eventArgs.Value);
                string configName = "";
                foreach (var config in AvailableAudioConfigurations(device))
                {
                    if (config.Id == eventArgs.Value)
                    {
                        configName = config.Name;
                    }
                }
                
                _client.StateUpdate($"tp_steelseries-gg_configs_{device}", configName);
                break;
            case "chatmix":
                _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)((float.Parse(eventArgs.Value, CultureInfo.InvariantCulture.NumberFormat) + 1) * 50));
                break;
            case "devices":
                string id = eventArgs.Value.Replace("%7B", "{").Replace("%7D", "}");
                _client.StateUpdate($"tp_steelseries-gg_redirection_device_{eventArgs.MixDevice}", GetRedirectionDeviceFromId(id).Name);
                break;
            case "redirectionState":
                switch (eventArgs.StreamerMode)
                {
                    case StreamerMode.Streaming:
                        _client.StateUpdate($"tp_steelseries-gg_streaming_redirection_state_{eventArgs.MixDevice}", BooleanToRedirectionState(eventArgs.Value));
                        break;
                    case StreamerMode.Monitoring:
                        _client.StateUpdate($"tp_steelseries-gg_monitoring_redirection_state_{eventArgs.MixDevice}", BooleanToRedirectionState(eventArgs.Value));
                        break;
                }
                break;
            case "SteamMonitoring":
                _client.StateUpdate("tp_steelseries-gg_audience_monitoring_state", BooleanToAudienceMonitoringState(eventArgs.Value));
                break;
        }
    }

    public string BooleanToMuteState(string state)
    {
        if (state.ToLower() == "true")
        {
            return _muteStatesNames.Split(",")[0];
        }
        return _muteStatesNames.Split(",")[1];
    }

    public string BooleanToRedirectionState(string state)
    {
        if (state.ToLower() == "true")
        {
            return _redirectionStatesNames.Split(",")[0];
        }
        return _redirectionStatesNames.Split(",")[1];
    }

    public string BooleanToAudienceMonitoringState(string state)
    {
        if (state.ToLower() == "true")
        {
            return _audienceMonitoringStatesNames.Split(",")[0];
        }
        return _audienceMonitoringStatesNames.Split(",")[1];
    }
    
    
    
    public void OnConnecterChangeEvent(ConnectorChangeEvent message)
    {
        string data1, data2;
        switch (message.ConnectorId)
        {
            case "tp_steelseries-gg_classic_set_volume":
                data1 = message["mixerchoice"];
                
                SetVolumeManager(message.Value, data1);
                break;
            case "tp_steelseries-gg_stream_set_volume":
                data1 = message["mixerchoice"];
                data2 = message["streamermode"];
                
                SetVolumeManager(message.Value, data1, data2);
                break;
            case "tp_steelseries-gg_set_chatmix_balance":
                double balance = (message.Value / 100f) * (1 - -1) + -1;
                SetChatMixBalance(balance);
                break;
        }
    }
    
    public void OnActionEvent(ActionEvent message)
    {
        string data1, data2, data3;
        switch (message.ActionId)
        {
            case "tp_steelseries-gg_set_mode":
                data1 = message["modechoice"];
                if (data1 == "Classic") SetMode(Mode.Classic);
                if (data1 == "Stream") SetMode(Mode.Stream);
                break;
            case "tp_steelseries-gg_switch_mode":
                SwitchMode();
                break;
            case "tp_steelseries-gg_set_classic_muted_state":
                data1 = message["mutechoice"];
                data2 = message["mixerchoice"];

                MuteStateManager(data1, data2);
                break;
            case "tp_steelseries-gg_set_stream_muted_state":
                data1 = message["mutechoice"];
                data2 = message["mixerchoice"];
                data3 = message["streamchoice"];
                
                MuteStateManager(data1, data2, data3);
                break;
            case "tp_steelseries-gg_set_config":
                data1 = message["mixerchoice"];
                data2 = message["configchoice"];
                
                SetConfigManager(data1, data2);
                break;
            case "tp_steelseries-gg_set_redirections_device":
                data1 = message["mixerchoice"];
                data2 = message["rdevicechoice"];
                
                SetRedirectionDeviceManager(data1, data2);
                break;
            case "tp_steelseries-gg_set_redirections_state":
                data1 = message["ablechoice"];
                data2 = message["streamermode"];
                data3 = message["mixerchoice"];

                RedirectionStateManager(data1, data2, data3);
                break;
            case "tp_steelseries-gg_set_audience_monitoring_state":
                data1 = message["ablechoice"];

                PublicHearingManager(data1);
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
                    case "mixerchoice":
                        if (message.Value == "Game") _client.ChoiceUpdate("configchoice", GetConfigManager(MixDevices.Game));
                        if (message.Value == "Chat") _client.ChoiceUpdate("configchoice", GetConfigManager(MixDevices.Chat));
                        if (message.Value == "Media") _client.ChoiceUpdate("configchoice", GetConfigManager(MixDevices.Media));
                        if (message.Value == "Aux") _client.ChoiceUpdate("configchoice", GetConfigManager(MixDevices.Aux));
                        if (message.Value == "Micro") _client.ChoiceUpdate("configchoice", GetConfigManager(MixDevices.Micro));
                        break;
                }
                break;
            case "tp_steelseries-gg_set_redirections_device":
                switch (message.ListId)
                {
                    case "mixerchoice":
                        if (message.Value == "Game") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        if (message.Value == "Chat") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        if (message.Value == "Media") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        if (message.Value == "Aux") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        if (message.Value == "Micro") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Input));
                        if (message.Value == "Monitoring") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        if (message.Value == "Stream") _client.ChoiceUpdate("rdevicechoice", GetRedirectionsDevicesManager(DeviceType.Output));
                        break;
                }
                break;
        }
    }
    
    
    
    public void SetVolumeManager(int volumeValue, string mixerChoice, string? streamerMode = null)
    {
        StreamerMode streamMode = StreamerMode.None;
        switch (streamerMode)
        {
            case null: streamMode = StreamerMode.None; break;
            case "Monitoring": streamMode = StreamerMode.Monitoring; break;
            case "Stream": streamMode = StreamerMode.Streaming; break;
        }
        
        MixDevices device = MixDevices.Master;
        switch (mixerChoice)
        {
            case "Master": device = MixDevices.Master; break;
            case "Game": device = MixDevices.Game; break;
            case "Chat": device = MixDevices.Chat; break;
            case "Media": device = MixDevices.Media; break;
            case "Aux": device = MixDevices.Aux; break;
            case "Micro": device = MixDevices.Micro; break;
        }
        
        double vol = volumeValue / 100f;

        if (vol == GetVolume(device, streamMode))
        {
            return;
        }
        
        if (GetMode() == Mode.Classic)
        {
            SetVolume(device, vol);
        }
        else
        {
            SetVolume(device, vol, streamMode);
        }
    }

    public void MuteStateManager(string newState, string mixerChoice, string? streamerMode = null)
    {
        StreamerMode streamMode = StreamerMode.None;
        switch (streamerMode)
        {
            case null: streamMode = StreamerMode.None; break;
            case "Monitoring": streamMode = StreamerMode.Monitoring; break;
            case "Stream": streamMode = StreamerMode.Streaming; break;
        }
        
        MixDevices device = MixDevices.Master;
        switch (mixerChoice)
        {
            case "Master": device = MixDevices.Master; break;
            case "Game": device = MixDevices.Game; break;
            case "Chat": device = MixDevices.Chat; break;
            case "Media": device = MixDevices.Media; break;
            case "Aux": device = MixDevices.Aux; break;
            case "Micro": device = MixDevices.Micro; break;
        }
        
        if (newState == "Toggle mute for")
        {
            if (GetMode() == Mode.Classic)
            {
                if (GetMuted(device) == true)
                {
                    SetMuted(device, false);
                }
                else
                {
                    SetMuted(device, true);
                }
            }
            else
            {
                if (GetMuted(device, streamMode) == true)
                {
                    Console.WriteLine("true");
                    SetMuted(device, false, streamMode);
                }
                else
                {
                    SetMuted(device, true, streamMode);
                }
            }
        }
        else if(newState == "Mute")
        {
            if (GetMode() == Mode.Classic) SetMuted(device, true);
            else SetMuted(device, true, streamMode);
        }
        else
        {
            if (GetMode() == Mode.Classic) SetMuted(device, false);
            else SetMuted(device, false, streamMode);
        }
    }

    public void SetConfigManager(string mixerChoice, string configChoice)
    {
        MixDevices device = MixDevices.Master;
        switch (mixerChoice)
        {
            case "Game": device = MixDevices.Game; break;
            case "Chat": device = MixDevices.Chat; break;
            case "Media": device = MixDevices.Media; break;
            case "Aux": device = MixDevices.Aux; break;
            case "Micro": device = MixDevices.Micro; break;
        }

        string configId = null;
        foreach (var config in AvailableAudioConfigurations(device))
        {
            if (config.Name == configChoice)
            {
                configId = config.Id;
                break;
            }
        }
        SetConfig(configId);
    }

    public void SetRedirectionDeviceManager(string mixerChoice, string redirectionDeviceChoice)
    {
        DeviceType type = DeviceType.Output;
        MixDevices tempDevice = MixDevices.Master;
        StreamerMode tempStreamDevice = StreamerMode.None;
        switch (mixerChoice)
        {
            case "Game": tempDevice = MixDevices.Game; type = DeviceType.Output; break;
            case "Chat": tempDevice = MixDevices.Chat; type = DeviceType.Output; break;
            case "Media": tempDevice = MixDevices.Media; type = DeviceType.Output; break;
            case "Aux": tempDevice = MixDevices.Aux; type = DeviceType.Output; break;
            case "Micro": tempDevice = MixDevices.Micro; type = DeviceType.Input; break;
            case "Monitoring": tempStreamDevice = StreamerMode.Monitoring; type = DeviceType.Output; break;
            case "Stream": tempStreamDevice = StreamerMode.Streaming; type = DeviceType.Output; break;
        }
        
        string deviceId = null;
        foreach (var device in AvailableRedirectionDevices(type))
        {
            if (device.Name == redirectionDeviceChoice)
            {
                deviceId = device.Id;
                break;
            }
        }
        
        if (tempDevice == MixDevices.Master && tempStreamDevice != StreamerMode.None)
        {
            SetRedirectionDevice(tempStreamDevice, deviceId);
        }
        else if (tempDevice != MixDevices.Master && tempStreamDevice == StreamerMode.None)
        {
            SetRedirectionDevice(tempDevice, deviceId);
        }
    }
    
    public void RedirectionStateManager(string newState, string streamerMode, string mixerChoice)
    {
        StreamerMode streamMode = StreamerMode.None;
        switch (streamerMode)
        {
            case "Monitoring": streamMode = StreamerMode.Monitoring; break;
            case "Stream": streamMode = StreamerMode.Streaming; break;
        }
        
        MixDevices device = MixDevices.Master;
        switch (mixerChoice)
        {
            case "Game": device = MixDevices.Game; break;
            case "Chat": device = MixDevices.Chat; break;
            case "Media": device = MixDevices.Media; break;
            case "Aux": device = MixDevices.Aux; break;
            case "Micro": device = MixDevices.Micro; break;
        }
        
        if (newState == "Toggle")
        {
            if (GetRedirectionState(streamMode, device) == true)
            {
                SetRedirectionState(false, streamMode, device);
            }
            else
            {
                SetRedirectionState(true, streamMode, device);
            }
        }
        else if (newState == "Enable")
        {
            SetRedirectionState(true, streamMode, device);
        }
        else if (newState == "Disable")
        {
            SetRedirectionState(false, streamMode, device);
        }
    }

    public void PublicHearingManager(string newState)
    {
        if (newState == "Toggle")
        {
            if (GetAudienceMonitoringState() == true)
            {
                SetAudienceMonitoringState(false);
            }
            else
            {
                SetAudienceMonitoringState(true);
            }
        }
        else if (newState == "Enable")
        {
            SetAudienceMonitoringState(true);
        }
        else if (newState == "Disable")
        {
            SetAudienceMonitoringState(false);
        }
    }

    public string[] GetConfigManager(MixDevices device)
    {
        List<string> listConfig = new();

        foreach (var config in AvailableAudioConfigurations(device))
        {
            listConfig.Add(config.Name);
        }

        return listConfig.ToArray();
    }
    
    public string[] GetRedirectionsDevicesManager(DeviceType deviceType)
    {
        List<string> listDevice = new();

        foreach (var device in AvailableRedirectionDevices(deviceType))
        {
            listDevice.Add(device.Name);
        }

        return listDevice.ToArray();
    }
    
    
    
    public void OnInfoEvent(InfoEvent message)
    {
        foreach (var settings in message.Settings)
        {
            if (settings.Name == "Muted states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _muteStatesNames = "Muted,Unmuted"; }
                else {_muteStatesNames = settings.Value;}
            }
            if (settings.Name == "Redirection states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _redirectionStatesNames = "Enabled,Disabled"; }
                else {_redirectionStatesNames = settings.Value;}
            }
            if (settings.Name == "Audience Monitoring states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _audienceMonitoringStatesNames = "Enabled,Disabled"; }
                else {_audienceMonitoringStatesNames = settings.Value;}
            }
        }
    }
    
    public void OnSettingsEvent(SettingsEvent message)
    {
        foreach (var settings in message.Values)
        {
            if (settings.Name == "Muted states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _muteStatesNames = "Muted,Unmuted"; }
                else {_muteStatesNames = settings.Value;}
            }
            if (settings.Name == "Redirection states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _redirectionStatesNames = "Enabled,Disabled"; }
                else {_redirectionStatesNames = settings.Value;}
            }
            if (settings.Name == "Audience Monitoring states names")
            {
                if (string.IsNullOrEmpty(settings.Value)) { _audienceMonitoringStatesNames = "Enabled,Disabled"; }
                else {_audienceMonitoringStatesNames = settings.Value;}
            }
        }
    }

    public void OnShortConnectorIdNotificationEvent(ShortConnectorIdNotificationEvent message) { throw new NotImplementedException(); }
    public void OnBroadcastEvent(BroadcastEvent message) { throw new NotImplementedException(); }
    public void OnUnhandledEvent(string jsonMessage) { throw new NotImplementedException(); }
    
    public void OnNotificationOptionClickedEvent(NotificationOptionClickedEvent message)
    {
        if (message.OptionId == "steelseries-gg_new_update_dl" + version)
        {
            Console.WriteLine(latestReleaseUrl);
            Process.Start(new ProcessStartInfo
            {
                FileName = latestReleaseUrl,
                UseShellExecute = true
            });
        }
    }
    
    private async Task CheckGitHubNewerVersion()
    {
        var gitClient = new GitHubClient(new ProductHeaderValue("DataNext27"));
        IReadOnlyList<Release> releases = await gitClient.Repository.Release.GetAll("DataNext27", "TouchPortal_SteelSeriesGG");

        latestReleaseUrl = releases[0].HtmlUrl;

        Version latestGitHubVersion = new Version(releases[0].TagName);
        Version localVersion = new Version(version);

        int versionComparison = localVersion.CompareTo(latestGitHubVersion);
        if (versionComparison < 0)
        {
            _client.ShowNotification("steelseries-gg_new_update" + version, "SteelSeries GG Plugin New Update Available",
                "New version: " + latestGitHubVersion +
                "\n\nPlease update to get new features and bug fixes" +
                "\n\nCurrent Installed Version: " + version, new []
                {
                    new NotificationOptions() {Id = "steelseries-gg_new_update_dl" + version, Title = "Go To Download Location"}
                });
        }
    }
}
