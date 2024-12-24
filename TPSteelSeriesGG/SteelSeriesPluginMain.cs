using SteelSeriesAPI;
using SteelSeriesAPI.Events;
using SteelSeriesAPI.Sonar.Enums;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;
using TouchPortalSDK.Messages.Events;

namespace TPSteelSeriesGG;

public class SteelSeriesPluginMain : ITouchPortalEventHandler
{
    public string PluginId => "steelseries-gg";

    private readonly ITouchPortalClient _client;

    private readonly SonarBridge _sonarManager;
    
    // Keep track of connectors level
    // Master, Game, Chat, Media, Aux, Mic
    private int[] _connectorsLevel = [-1, -1, -1, -1, -1, -1];
    private int[][] _connectorsLevelStreamer = [[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1],[-1,-1]];

    public SteelSeriesPluginMain()
    {
        _client = TouchPortalFactory.CreateClient(this);
        _sonarManager = new SonarBridge();
    }

    public void Run()
    {
        _client.Connect();
        _sonarManager.WaitUntilSonarStarted();
        
        _sonarManager.StartListener();
        _sonarManager.SonarEventManager.OnSonarModeChange += OnModeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarVolumeChange += OnVolumeChangeHandler;
        _sonarManager.SonarEventManager.OnSonarChatMixChange += OnChatMixChangeHandler;
        
        InitializeConnectors();
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
                break;
            case "tp_steelseries-gg_set_mode":
                _sonarManager.SetMode((Mode)Enum.Parse(typeof(Mode), message["mode"], true));
                break;
            case "tp_steelseries-gg_set_classic_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true));
                break;
            case "tp_steelseries-gg_set_streamer_mute":
                if (message["action"] == "Toggle") _sonarManager.SetMute(!_sonarManager.GetMute((Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true)), (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else if (message["action"] == "Mute") _sonarManager.SetMute(true, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                else _sonarManager.SetMute(false, (Device)Enum.Parse(typeof(Device), message["device"], true), (Channel)Enum.Parse(typeof(Channel), message["channel"], true));
                break;
            case "tp_steelseries-gg_set_config":
                _sonarManager.SetConfig((Device)Enum.Parse(typeof(Device), message["device"], true), message["config"]);
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
        }
    }

    void OnModeChangeHandler(object? sender, SonarModeEvent eventArgs)
    {
        InitializeConnectors();
    }
    
    void OnVolumeChangeHandler(object? sender, SonarVolumeEvent eventArgs)
    {
        if (eventArgs.Mode == Mode.Classic)
        {
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
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((eventArgs.Balance * 100f)+1)*50));
    }
}