using System.Globalization;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesJsonParser;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHTTPHandler;

namespace TPSteelSeriesGG.SteelSeriesAPI;

public class SteelSeriesHttpActionHandler
{
    public static bool isChangingMode = false;
    public static void SetMode(Mode mode)
    {
        isChangingMode = true;
        if (mode != Mode.Classic && mode != Mode.Stream)
        {
            Console.Error.WriteLine("mode does not exist");
            return;
        }

        if (mode == GetMode())
        {
            Console.Error.WriteLine("mode is already set");
            return;
        }
        
        HttpPut("mode/" + mode.ToString().ToLower());
        Thread.Sleep(100);
        isChangingMode = false;
    }

    public static void SwitchMode()
    {
        if (isChangingMode) return;
        if (GetMode() == Mode.Classic) { SetMode(Mode.Stream); return;}
        if (GetMode() == Mode.Stream) { SetMode(Mode.Classic); return;}
    }

    public static void SetVolume(MixDevices device, double volume, StreamerMode stream = StreamerMode.None)
    {
        string vol = volume.ToString("0.00", CultureInfo.InvariantCulture);
        
        switch (GetMode())
        {
            case Mode.Classic:
                switch (device)
                {
                    case MixDevices.Master:
                        HttpPut("volumeSettings/classic/Master/Volume/" + vol);
                        break;
                    case MixDevices.Game:
                        HttpPut("volumeSettings/classic/game/Volume/" + vol);
                        break;
                    case MixDevices.Chat:
                        HttpPut("volumeSettings/classic/chatRender/Volume/" + vol);
                        break;
                    case MixDevices.Micro:
                        HttpPut("volumeSettings/classic/chatCapture/Volume/" + vol);
                        break;
                    case MixDevices.Media:
                        HttpPut("volumeSettings/classic/media/Volume/" + vol);
                        break;
                    case MixDevices.Aux:
                        HttpPut("volumeSettings/classic/aux/Volume/" + vol);
                        break;
                }
                break;
            
            case Mode.Stream:
                // /volumeSettings/streamer/streaming/game/volume/0.6
                switch (stream)
                {
                    case StreamerMode.Streaming:
                        switch (device)
                        {
                            case MixDevices.Master:
                                HttpPut("volumeSettings/streamer/streaming/Master/volume/" + vol);
                                break;
                            case MixDevices.Game:
                                HttpPut("volumeSettings/streamer/streaming/game/volume/" + vol);
                                break;
                            case MixDevices.Chat:
                                HttpPut("volumeSettings/streamer/streaming/chatRender/volume/" + vol);
                                break;
                            case MixDevices.Micro:
                                HttpPut("volumeSettings/streamer/streaming/chatCapture/volume/" + vol);
                                break;
                            case MixDevices.Media:
                                HttpPut("volumeSettings/streamer/streaming/media/volume/" + vol);
                                break;
                            case MixDevices.Aux:
                                HttpPut("volumeSettings/streamer/streaming/aux/volume/" + vol);
                                break;
                        }
                        break;
                    case StreamerMode.Monitoring:
                        switch (device)
                        {
                            case MixDevices.Master:
                                HttpPut("volumeSettings/streamer/monitoring/Master/volume/" + vol);
                                break;
                            case MixDevices.Game:
                                HttpPut("volumeSettings/streamer/monitoring/game/volume/" + vol);
                                break;
                            case MixDevices.Chat:
                                HttpPut("volumeSettings/streamer/monitoring/chatRender/volume/" + vol);
                                break;
                            case MixDevices.Micro:
                                HttpPut("volumeSettings/streamer/monitoring/chatCapture/volume/" + vol);
                                break;
                            case MixDevices.Media:
                                HttpPut("volumeSettings/streamer/monitoring/media/volume/" + vol);
                                break;
                            case MixDevices.Aux:
                                HttpPut("volumeSettings/streamer/monitoring/aux/volume/" + vol);
                                break;
                        }
                        break;
                }
                break;
        }
    }

    public static void SetMuted(MixDevices device, bool muteState, StreamerMode stream = StreamerMode.None)
    {
        switch (GetMode())
        {
            case Mode.Classic:
                switch (device)
                {
                    case MixDevices.Master:
                        HttpPut("volumeSettings/classic/Master/Mute/" + muteState);
                        break;
                    case MixDevices.Game:
                        HttpPut("volumeSettings/classic/game/Mute/" + muteState);
                        break;
                    case MixDevices.Chat:
                        HttpPut("volumeSettings/classic/chatRender/Mute/" + muteState);
                        break;
                    case MixDevices.Micro:
                        HttpPut("volumeSettings/classic/chatCapture/Mute/" + muteState);
                        break;
                    case MixDevices.Media:
                        HttpPut("volumeSettings/classic/media/Mute/" + muteState);
                        break;
                    case MixDevices.Aux:
                        HttpPut("volumeSettings/classic/aux/Mute/" + muteState);
                        break;
                }
                break;
            case Mode.Stream:
                switch (stream)
                {
                    case StreamerMode.Streaming:
                        switch (device)
                        {
                            case MixDevices.Master:
                                HttpPut("volumeSettings/streamer/streaming/Master/isMuted/" + muteState);
                                break;
                            case MixDevices.Game:
                                HttpPut("volumeSettings/streamer/streaming/game/isMuted/" + muteState);
                                break;
                            case MixDevices.Chat:
                                HttpPut("volumeSettings/streamer/streaming/chatRender/isMuted/" + muteState);
                                break;
                            case MixDevices.Micro:
                                HttpPut("volumeSettings/streamer/streaming/chatCapture/isMuted/" + muteState);
                                break;
                            case MixDevices.Media:
                                HttpPut("volumeSettings/streamer/streaming/media/isMuted/" + muteState);
                                break;
                            case MixDevices.Aux:
                                HttpPut("volumeSettings/streamer/streaming/aux/isMuted/" + muteState);
                                break;
                        }
                        break;
                    case StreamerMode.Monitoring:
                        switch (device)
                        {
                            case MixDevices.Master:
                                HttpPut("volumeSettings/streamer/monitoring/Master/isMuted/" + muteState);
                                break;
                            case MixDevices.Game:
                                HttpPut("volumeSettings/streamer/monitoring/game/isMuted/" + muteState);
                                break;
                            case MixDevices.Chat:
                                HttpPut("volumeSettings/streamer/monitoring/chatRender/isMuted/" + muteState);
                                break;
                            case MixDevices.Micro:
                                HttpPut("volumeSettings/streamer/monitoring/chatCapture/isMuted/" + muteState);
                                break;
                            case MixDevices.Media:
                                HttpPut("volumeSettings/streamer/monitoring/media/isMuted/" + muteState);
                                break;
                            case MixDevices.Aux:
                                HttpPut("volumeSettings/streamer/monitoring/aux/isMuted/" + muteState);
                                break;
                        }
                        break;
                }
                break;
        }
    }

    public static void SetConfig(string? configId)
    {
        if (string.IsNullOrEmpty(configId)) return;
        
        HttpPut("configs/" + configId + "/select");
    }

    public static void SetChatMixBalance(double balance)
    {
        if (balance < -1 || balance > 1) { throw new ArgumentOutOfRangeException(nameof(balance), "chatmix balance has to be between -1 and 1"); }

        if (GetChatMixState() == "differentDeviceSelected")
        {
            return;
        }

        if (balance == GetChatMixBalance())
        {
            return;
        }
        
        HttpPut("chatMix?balance=" + balance.ToString("0.00", CultureInfo.InvariantCulture));
    }

    public static void SetRedirectionDevice(MixDevices virtualDevice, string deviceId)
    {
        // Request URI: /classicRedirections/chat/deviceId/%7B0.0.0.00000000%7D.%7B1bc466a5-13e1-485a-8002-14a302a6cc35%7D
        switch (GetMode())
        {
            case Mode.Classic:
                switch (virtualDevice)
                {
                    case MixDevices.Game:
                        HttpPut("classicRedirections/game/deviceId/" + deviceId);
                        break;
                    case MixDevices.Chat:
                        HttpPut("classicRedirections/chat/deviceId/" + deviceId);
                        break;
                    case MixDevices.Micro:
                        HttpPut("classicRedirections/mic/deviceId/" + deviceId);
                        break;
                    case MixDevices.Media:
                        HttpPut("classicRedirections/media/deviceId/" + deviceId);
                        break;
                    case MixDevices.Aux:
                        HttpPut("classicRedirections/aux/deviceId/" + deviceId);
                        break;
                }
                break;
            case Mode.Stream:
                // Request URI: /streamRedirections/streaming/deviceId/%7B0.0.0.00000000%7D.%7B1bc466a5-13e1-485a-8002-14a302a6cc35%7D
                switch (virtualDevice)
                {
                    case MixDevices.Micro:
                        HttpPut("streamRedirections/mic/deviceId/" + deviceId);
                        break;
                }
                break;
        }
    }

    public static void SetRedirectionDevice(StreamerMode virtualDevice, string deviceId)
    {
        switch (virtualDevice)
        {
            case StreamerMode.Streaming:
                HttpPut("streamRedirections/streaming/deviceId/" + deviceId);
                break;
            case StreamerMode.Monitoring:
                HttpPut("streamRedirections/monitoring/deviceId/" + deviceId);
                break;
        }
    }

    public static void SetRedirectionState(bool state, StreamerMode streamerMode, MixDevices mixerChoice)
    {
        switch (streamerMode)
        {
            case StreamerMode.Streaming:
                switch (mixerChoice)
                {
                    // streamRedirections/streaming/redirections/game/isEnabled/true
                    case MixDevices.Game:
                        HttpPut("streamRedirections/streaming/redirections/game/isEnabled/" + state);
                        break;
                    case MixDevices.Chat:
                        HttpPut("streamRedirections/streaming/redirections/chatRender/isEnabled/" + state);
                        break;
                    case MixDevices.Media:
                        HttpPut("streamRedirections/streaming/redirections/media/isEnabled/" + state);
                        break;
                    case MixDevices.Aux:
                        HttpPut("streamRedirections/streaming/redirections/aux/isEnabled/" + state);
                        break;
                    case MixDevices.Micro:
                        HttpPut("streamRedirections/streaming/redirections/chatCapture/isEnabled/" + state);
                        break;
                }
                break;
            case StreamerMode.Monitoring:
                switch (mixerChoice)
                {
                    // streamRedirections/monitoring/redirections/game/isEnabled/true
                    case MixDevices.Game:
                        HttpPut("streamRedirections/monitoring/redirections/game/isEnabled/" + state);
                        break;
                    case MixDevices.Chat:
                        HttpPut("streamRedirections/monitoring/redirections/chatRender/isEnabled/" + state);
                        break;
                    case MixDevices.Media:
                        HttpPut("streamRedirections/monitoring/redirections/media/isEnabled/" + state);
                        break;
                    case MixDevices.Aux:
                        HttpPut("streamRedirections/monitoring/redirections/aux/isEnabled/" + state);
                        break;
                    case MixDevices.Micro:
                        HttpPut("streamRedirections/monitoring/redirections/chatCapture/isEnabled/" + state);
                        break;
                }
                break;
        }
    }

    public static void SetAudienceMonitoringState(bool state)
    {
        if (GetMode() != Mode.Stream) return;
        
        HttpPut("streamRedirections/isStreamMonitoringEnabled/" + state);
    }
}