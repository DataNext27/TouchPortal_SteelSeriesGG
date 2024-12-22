using Microsoft.Extensions.Logging;
using SteelSeriesAPI;
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
        // sonarManager.StartListener();
    }

    public void OnClosedEvent(string message)
    {
        _sonarManager.StopListener();
        Environment.Exit(0);
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
                _sonarManager.SetChatMixBalance((message.Value / 100f) * (1 - -1) + -1);
                break;
        }
    }
}