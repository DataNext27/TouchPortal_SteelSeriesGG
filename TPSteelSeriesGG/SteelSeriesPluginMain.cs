using Microsoft.Extensions.Logging;
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

    public SteelSeriesPluginMain()
    {
        _client = TouchPortalFactory.CreateClient(this);
        _sonarManager = new SonarBridge();
    }

    public void Run()
    {
        _client.Connect();
        
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
            _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={device.ToString()}", (int)(_sonarManager.GetVolume(device) * 100f));
            foreach (var channel in Enum.GetValues(typeof(Channel)).Cast<Channel>())
            {
                _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={channel.ToString()}|device={device.ToString()}", (int)(_sonarManager.GetVolume(device, channel) * 100f));
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
            
            case "tp_steelseries-gg_stream_set_volumes":
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
            _client.ConnectorUpdate($"tp_steelseries-gg_classic_set_volume|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
        }
        else
        {
            _client.ConnectorUpdate($"tp_steelseries-gg_stream_set_volume|channel={eventArgs.Channel.ToString()}|device={eventArgs.Device.ToString()}", (int)(eventArgs.Volume * 100f));
        }
    }

    void OnChatMixChangeHandler(object? sender, SonarChatMixEvent eventArgs)
    {
        _client.ConnectorUpdate("tp_steelseries-gg_set_chatmix_balance", (int)(((eventArgs.Balance * 100f)+1)*50));
    }
}