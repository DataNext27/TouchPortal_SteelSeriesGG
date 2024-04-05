using System.Text.Json;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHTTPHandler;

namespace TPSteelSeriesGG.SteelSeriesAPI;

public class SteelSeriesJsonParser
{
    private static readonly HttpClient HttpClient = new();
    
    public static Mode GetMode()
    {
        if (GetSonarWebServerAddress() == null)
        {
            return Mode.Classic;
        }
        
        string strMode = HttpClient.GetStringAsync(GetSonarWebServerAddress() + "mode").Result;
        strMode = strMode.Remove(0, 1);
        strMode = strMode.Remove(strMode.Length-1, 1);

        Mode mode = (Mode)Enum.Parse(typeof(Mode), strMode, true);

        return mode;
    }

    public static SonarVolumeSettings GetVolumeSettings(MixDevices device, StreamerMode stream = StreamerMode.None)
    {
        if (GetSonarWebServerAddress() == null)
        {
            return null;
        }
        
        JsonDocument volumeSettings;
        JsonElement master;
        JsonElement devices;
        
        double volume;
        bool muted;
        switch (GetMode())
        {
            case Mode.Classic:
                // volumeSettings/classic
                volumeSettings = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "volumeSettings/classic").Result);
                master = volumeSettings.RootElement.GetProperty("masters");
                devices = volumeSettings.RootElement.GetProperty("devices");

                switch (device) // device = game, chatRender, chatCapture, media, aux
                {
                    case MixDevices.Master: // "masters"
                        master = master.GetProperty("classic");
                        volume = master.GetProperty("volume").GetDouble();
                        muted = master.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    
                    case MixDevices.Game: // "game"
                        JsonElement game = devices.GetProperty("game").GetProperty("classic");
                        volume = game.GetProperty("volume").GetDouble();
                        muted = game.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    
                    case MixDevices.Chat: // "chatRender"
                        JsonElement chat = devices.GetProperty("chatRender").GetProperty("classic");
                        volume = chat.GetProperty("volume").GetDouble();
                        muted = chat.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    
                    case MixDevices.Micro: // "chatCapture"
                        JsonElement mic = devices.GetProperty("chatCapture").GetProperty("classic");
                        volume = mic.GetProperty("volume").GetDouble();
                        muted = mic.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    
                    case MixDevices.Media: // "media"
                        JsonElement media = devices.GetProperty("media").GetProperty("classic");
                        volume = media.GetProperty("volume").GetDouble();
                        muted = media.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    
                    case MixDevices.Aux: // "aux"
                        JsonElement aux = devices.GetProperty("aux").GetProperty("classic");
                        volume = aux.GetProperty("volume").GetDouble();
                        muted = aux.GetProperty("muted").GetBoolean();
                        return new SonarVolumeSettings(volume, muted);
                    default:
                        return new SonarVolumeSettings(0, false);
                }
            
            case Mode.Stream:
                // volumeSettings/streamer
                volumeSettings = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "volumeSettings/streamer").Result);
                master = volumeSettings.RootElement.GetProperty("masters");
                devices = volumeSettings.RootElement.GetProperty("devices");

                switch (stream)
                {
                    case StreamerMode.Streaming:
                        switch (device)
                        {
                            case MixDevices.Master:
                                master = master.GetProperty("stream").GetProperty("streaming");
                                volume = master.GetProperty("volume").GetDouble();
                                muted = master.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Game:
                                JsonElement game = devices.GetProperty("game").GetProperty("stream").GetProperty("streaming");
                                volume = game.GetProperty("volume").GetDouble();
                                muted = game.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Chat:
                                JsonElement chat = devices.GetProperty("chatRender").GetProperty("stream").GetProperty("streaming");
                                volume = chat.GetProperty("volume").GetDouble();
                                muted = chat.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Micro:
                                JsonElement mic = devices.GetProperty("chatCapture").GetProperty("stream").GetProperty("streaming");
                                volume = mic.GetProperty("volume").GetDouble();
                                muted = mic.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Media:
                                JsonElement media = devices.GetProperty("media").GetProperty("stream").GetProperty("streaming");
                                volume = media.GetProperty("volume").GetDouble();
                                muted = media.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Aux:
                                JsonElement aux = devices.GetProperty("aux").GetProperty("stream").GetProperty("streaming");
                                volume = aux.GetProperty("volume").GetDouble();
                                muted = aux.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            default:
                                return new SonarVolumeSettings(0, false);
                        }
                    case StreamerMode.Monitoring:
                        switch (device)
                        {
                            case MixDevices.Master:
                                master = master.GetProperty("stream").GetProperty("monitoring");
                                volume = master.GetProperty("volume").GetDouble();
                                muted = master.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Game:
                                JsonElement game = devices.GetProperty("game").GetProperty("stream").GetProperty("monitoring");
                                volume = game.GetProperty("volume").GetDouble();
                                muted = game.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Chat:
                                JsonElement chat = devices.GetProperty("chatRender").GetProperty("stream").GetProperty("monitoring");
                                volume = chat.GetProperty("volume").GetDouble();
                                muted = chat.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Micro:
                                JsonElement mic = devices.GetProperty("chatCapture").GetProperty("stream").GetProperty("monitoring");
                                volume = mic.GetProperty("volume").GetDouble();
                                muted = mic.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Media:
                                JsonElement media = devices.GetProperty("media").GetProperty("stream").GetProperty("monitoring");
                                volume = media.GetProperty("volume").GetDouble();
                                muted = media.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            
                            case MixDevices.Aux:
                                JsonElement aux = devices.GetProperty("aux").GetProperty("stream").GetProperty("monitoring");
                                volume = aux.GetProperty("volume").GetDouble();
                                muted = aux.GetProperty("muted").GetBoolean();
                                return new SonarVolumeSettings(volume, muted);
                            default:
                                return new SonarVolumeSettings(0, false);
                        }
                    default:
                        return new SonarVolumeSettings(0, false);
                }
            default:
                return new SonarVolumeSettings(0, false);
        }
        return new SonarVolumeSettings(0, false);
    }

    public static double GetVolume(MixDevices device, StreamerMode stream = StreamerMode.None)
    {
        return GetVolumeSettings(device, stream).Volume;
    }

    public static bool GetMuted(MixDevices device, StreamerMode stream = StreamerMode.None)
    {
        return GetVolumeSettings(device, stream).MuteState;
    }
    
    // Get all Configs available for x virtual device ordered alphabetically
    public static IEnumerable<SonarAudioConfiguration> AvailableAudioConfigurations(MixDevices device) =>
        GetAudioConfigurations(device).OrderBy(s => s.Name);

    // Get all Configs available for x virtual device
    public static IEnumerable<SonarAudioConfiguration> GetAudioConfigurations(MixDevices device) 
    {
        if (GetSonarWebServerAddress() == null)
        {
            yield return null;
        }
        
        JsonDocument configs = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "configs").Result);

        string tempDevice = device.ToString().ToLower();
        if (tempDevice == "chat") tempDevice = "chatrender";
        if (tempDevice == "micro") tempDevice = "chatcapture";

        foreach (var element in configs.RootElement.EnumerateArray())
        {
            if (element.GetProperty("virtualAudioDevice").GetString()?.ToLower() == tempDevice)
            {
                string? id = element.GetProperty("id").GetString();
                string? name = element.GetProperty("name").GetString();
                yield return new SonarAudioConfiguration(id, name);
            }
        }
    }

    // Get the selected config for x virtual device
    public static SonarAudioConfiguration GetSelectedAudioConfiguration(MixDevices device) 
    {
        if (GetSonarWebServerAddress() == null)
        {
            return null;
        }
        
        JsonDocument selectedConfigs = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "configs/selected").Result);
        
        string tempDevice = device.ToString().ToLower();
        if (tempDevice == "chat") tempDevice = "chatrender";
        if (tempDevice == "micro") tempDevice = "chatcapture";

        foreach (var element in selectedConfigs.RootElement.EnumerateArray())
        {
            if (element.GetProperty("virtualAudioDevice").GetString()?.ToLower() == tempDevice)
            {
                string? id = element.GetProperty("id").GetString();
                string? name = element.GetProperty("name").GetString();
                return new SonarAudioConfiguration(id, name);
            }
        }

        return null;
    }

    public static MixDevices GetDeviceFromAudioConfiguration(string configId)
    {
        JsonDocument configs = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "configs").Result);
        string? device = "";
        
        foreach (var element in configs.RootElement.EnumerateArray())
        {
            if (element.GetProperty("id").GetString() == configId)
            {
                device = element.GetProperty("virtualAudioDevice").GetString();
                break;
            }
        }

        if (device == "chatRender") device = "chat";
        if (device == "chatCapture") device = "micro";

        return (MixDevices)Enum.Parse(typeof(MixDevices), device, true);
    }

    public static double GetChatMixBalance()
    {
        if (GetSonarWebServerAddress() == null)
        {
            return 0;
        }
        
        JsonDocument chatMix = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "chatMix").Result);

        double balance = chatMix.RootElement.GetProperty("balance").GetDouble();

        return balance;
    }

    public static string? GetChatMixState()
    {
        if (GetSonarWebServerAddress() == null)
        {
            return null;
        }
        
        JsonDocument chatMix = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "chatMix").Result);
        
        string? state = chatMix.RootElement.GetProperty("state").GetString();

        return state;
    }
    
    public static IEnumerable<RedirectionDevice> AvailableRedirectionDevices(DeviceType type) =>
        GetRedirectionDevices(type).OrderBy(s => s.Name);

    public static IEnumerable<RedirectionDevice> GetRedirectionDevices(DeviceType type)
    {
        if (GetSonarWebServerAddress() == null)
        {
            yield return null;
        }
        
        JsonDocument audioDevices = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "audioDevices").Result);

        switch (type)
        {
            case DeviceType.Output:
                foreach (var element in audioDevices.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("role").GetString() == "none")
                    {
                        if (element.GetProperty("dataFlow").GetString() == "render")
                        {
                            string? name = element.GetProperty("friendlyName").GetString();
                            string id = element.GetProperty("id").GetString();
                            yield return new RedirectionDevice(name, id);
                        }
                    }
                }
                break;
            case DeviceType.Input:
                foreach (var element in audioDevices.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("role").GetString() == "none")
                    {
                        if (element.GetProperty("dataFlow").GetString() == "capture")
                        {
                            string? name = element.GetProperty("friendlyName").GetString();
                            string id = element.GetProperty("id").GetString();
                            yield return new RedirectionDevice(name, id);
                        }
                    }
                }
                break;
        }
    }
    
    /// <summary>
    /// Return the name and id of a redirection device
    /// </summary>
    /// <param name="device">The device you want the redirection device</param>
    /// <returns>RedirectionDevice(Name,Id)</returns>
    /// <exception cref="Exception"></exception>
    public static RedirectionDevice GetSelectedRedirectionDevice(MixDevices device)
    {
        if (GetSonarWebServerAddress() == null)
        {
            return null;
        }

        if (device == MixDevices.Master) throw new Exception("Can't retrieve Master redirection device");

        switch (GetMode())
        {
            case Mode.Classic:
                string strDevice = null;
                DeviceType type = DeviceType.Output;
                switch (device)
                {
                    case MixDevices.Game: strDevice = "game"; type = DeviceType.Output; break;
                    case MixDevices.Chat: strDevice = "chat"; type = DeviceType.Output; break;
                    case MixDevices.Micro: strDevice = "mic"; type = DeviceType.Input; break;
                    case MixDevices.Media: strDevice = "media"; type = DeviceType.Output; break;
                    case MixDevices.Aux: strDevice = "aux"; type = DeviceType.Output; break;
                }
                
                JsonDocument classicRedirections = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "classicRedirections").Result);

                foreach (var element in classicRedirections.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("id").GetString() == strDevice)
                    {
                        string id = element.GetProperty("deviceId").GetString();
                        string name = null;
                        foreach (var rDevice in AvailableRedirectionDevices(type))
                        {
                            if (rDevice.Id == id)
                            {
                                name = rDevice.Name;
                            }
                        }

                        return new RedirectionDevice(name, id);
                    }
                }
                break;
            case Mode.Stream:
                JsonDocument streamRedirections = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "streamRedirections").Result);
                foreach (var element in streamRedirections.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("streamRedirectionId").GetString() == "mic")
                    {
                        string id = element.GetProperty("deviceId").GetString();
                        string name = null;
                        foreach (var rDevice in AvailableRedirectionDevices(DeviceType.Input))
                        {
                            if (rDevice.Id == id)
                            {
                                name = rDevice.Name;
                            }
                        }

                        return new RedirectionDevice(name, id);
                    }
                }
                break;
        }
        return new RedirectionDevice("error", "error");
    }
    
    /// <summary>
    /// Return the monitoring or streaming redirection device
    /// </summary>
    /// <param name="stream">The stream redirection you want the redirection device</param>
    /// <returns>RedirectionDevice(Name,Id)</returns>
    public static RedirectionDevice GetSelectedRedirectionDevice(StreamerMode stream)
    {
        if (GetSonarWebServerAddress() == null)
        {
            return null;
        }
        
        JsonDocument streamRedirections = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "streamRedirections").Result);
        
        switch (stream)
        {
            case StreamerMode.Streaming:
                foreach (var element in streamRedirections.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("streamRedirectionId").GetString() == "streaming")
                    {
                        string id = element.GetProperty("deviceId").GetString();
                        string name = null;
                        foreach (var rDevice in AvailableRedirectionDevices(DeviceType.Output))
                        {
                            if (rDevice.Id == id)
                            {
                                name = rDevice.Name;
                            }
                        }

                        return new RedirectionDevice(name, id);
                    }
                }
                break;
            case StreamerMode.Monitoring:
                foreach (var element in streamRedirections.RootElement.EnumerateArray())
                {
                    if (element.GetProperty("streamRedirectionId").GetString() == "monitoring")
                    {
                        string id = element.GetProperty("deviceId").GetString();
                        string name = null;
                        foreach (var rDevice in AvailableRedirectionDevices(DeviceType.Output))
                        {
                            if (rDevice.Id == id)
                            {
                                name = rDevice.Name;
                            }
                        }

                        return new RedirectionDevice(name, id);
                    }
                }
                break;
        }
        return new RedirectionDevice("error", "error");
    }

    public static RedirectionDevice GetRedirectionDeviceFromId(string deviceId)
    {
        JsonDocument audioDevices = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "audioDevices").Result);

        foreach (var element in audioDevices.RootElement.EnumerateArray())
        {
            if (element.GetProperty("id").ToString() == deviceId)
            {
                string? name = element.GetProperty("friendlyName").GetString();
                return new RedirectionDevice(name, deviceId);
            }
        }
        return new RedirectionDevice("error", "error");
    }

    public static bool GetRedirectionState(StreamerMode streamerMode, MixDevices device)
    {
        JsonDocument streamRedirections = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "streamRedirections").Result);
        
        string tempDevice = device.ToString().ToLower();
        if (tempDevice == "chat") tempDevice = "chatRender";
        if (tempDevice == "micro") tempDevice = "chatCapture";

        foreach (var element in streamRedirections.RootElement.EnumerateArray())
        {
            if (element.GetProperty("streamRedirectionId").ToString() == streamerMode.ToString().ToLower())
            {
                foreach (var redirection in element.GetProperty("status").EnumerateArray())
                {
                    if (redirection.GetProperty("role").ToString() == tempDevice)
                    {
                        return redirection.GetProperty("isEnabled").GetBoolean();
                    }
                }
            }
        }

        return false;
    }
    
    public static bool GetAudienceMonitoringState()
    {
        JsonDocument streamMonitoring = JsonDocument.Parse(HttpClient.GetStringAsync(GetSonarWebServerAddress() + "streamRedirections/isStreamMonitoringEnabled").Result);

        var a = streamMonitoring.RootElement.GetBoolean();
        return a;
    }
}