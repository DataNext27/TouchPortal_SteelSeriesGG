using System.Diagnostics;
using System.Text.Json;
using System.Text;
using PacketDotNet;
using SharpPcap;

namespace TPSteelSeriesGG.SteelSeriesAPI;

public class SteelSeriesHTTPHandler
{
    
    private static readonly HttpClient HttpClient = new();
    public static event EventHandler<OnSteelSeriesEventArgs> OnSteelSeriesEvent = delegate{  };
    
    public static string GetggEncryptedAddress()
    {
        try
        {
            JsonDocument coreProps = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"SteelSeries\GG\coreProps.json")));
            string ggEncryptedAddress = coreProps.RootElement.GetProperty("ggEncryptedAddress").ToString();
            return ggEncryptedAddress;
        }
        catch
        {
            Console.WriteLine("Error: Could not find coreProps.json");
            throw;
        }
    }

    public static bool IsSteelSeriesGGRunning()
    {
        Process[] processes = Process.GetProcessesByName("SteelSeriesSonar");
        return processes.Length > 0;
    }

    public static string GetSonarWebServerAddress()
    {
        if (!IsSteelSeriesGGRunning())
        {
            Console.WriteLine("Sonar not running, retrying in 1 sec");
            Thread.Sleep(1000);
            GetSonarWebServerAddress();
        }
        
        HttpClientHandler clientHandler = new HttpClientHandler();
        clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };
        HttpClient httpClient = new(clientHandler);
        try
        {
            JsonDocument subApps = JsonDocument.Parse(httpClient.GetStringAsync("https://" + GetggEncryptedAddress() + "/subApps").Result);
            JsonElement sonarElement = subApps.RootElement.GetProperty("subApps").GetProperty("sonar");

            if (sonarElement.GetProperty("isEnabled").ToString() != "True") return null!;
            if (sonarElement.GetProperty("isReady").ToString() != "True") return null!;
            if (sonarElement.GetProperty("isRunning").ToString() != "True") return null!;

            string sonarWebServerAddress = sonarElement.GetProperty("metadata").GetProperty("webServerAddress") + "/";
            return sonarWebServerAddress;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static void HttpPut(string targetedHttp)
    {
        string sonarWebServerAddress = GetSonarWebServerAddress();
        if (sonarWebServerAddress == null)
        {
            Thread.Sleep(1000);
            HttpPut(targetedHttp);
        }
        
        HttpResponseMessage? httpResponseMessage = HttpClient.PutAsync(sonarWebServerAddress + targetedHttp, null)
            .GetAwaiter().GetResult();
        httpResponseMessage.EnsureSuccessStatusCode();
    }

    //public static event EventHandler OnSteelSeriesEvent;
    
    public static void StartSteelSeriesListener()
    {
        var targetPort = new Uri(GetSonarWebServerAddress()).Port;
        
        // Obtenir la liste des interfaces réseau disponibles
        var devices = CaptureDeviceList.Instance;

        if (devices.Count == 0)
        {
            Console.WriteLine("Aucune interface réseau trouvée.");
            return;
        }

        // Recherchez l'interface de bouclage (Loopback) parmi les interfaces disponibles
        var loopbackDevice = devices.FirstOrDefault(device => device.Name.Contains("Loopback"));

        if (loopbackDevice == null)
        {
            Console.WriteLine("Aucune interface de bouclage (Loopback) trouvée.");
            return;
        }
        Console.WriteLine("Loopback device name :" + loopbackDevice.Name);
        Console.WriteLine("Loopback device name :" + loopbackDevice.Description);

        // Ouvrir l'interface de bouclage pour la capture
        loopbackDevice.OnPacketArrival += (object s, PacketCapture e) =>
        {
            var rawPacket = e.GetPacket();
            if (rawPacket.Data.Length <= 0)
            {
                return;
            }
            
            Packet packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var ipPacket = packet.Extract<IPPacket>();
            var tcpPacket = ipPacket.Extract<TcpPacket>();
            if (ipPacket != null)
            {
                if (tcpPacket != null)
                {
                    if (tcpPacket.DestinationPort == targetPort || tcpPacket.SourcePort == targetPort)
                    {
                        string httpData = Encoding.UTF8.GetString(tcpPacket.PayloadData);
                        List<string> putData = new List<string>(httpData.Split("\n"));
                        foreach (var line in putData)
                        {
                            if (line.Contains("PUT"))
                            {
                                Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss")} [HTTP Packet] {ipPacket.SourceAddress}:{tcpPacket.SourcePort} -> {ipPacket.DestinationAddress}:{tcpPacket.DestinationPort}");
                                Console.WriteLine(line);
                                string path = line.Split(' ')[1];
                                SteelSeriesEventManager(path);
                            }
                        }
                    }
                }
            }
        };

        loopbackDevice.OnCaptureStopped += (sender, e) =>
        {
            loopbackDevice.Close();
        };

        // Commencer la capture sur l'interface de bouclage
        loopbackDevice.Open(DeviceModes.Promiscuous);
        loopbackDevice.Capture();

        Console.WriteLine($"En attente de paquets HTTP sur le port {targetPort} de l'interface de bouclage.");
    }
    
    static void SteelSeriesEventManager(string path)
    {
        OnSteelSeriesEventArgs args = new OnSteelSeriesEventArgs();
        string[] subs = path.Split("/");

        if (subs.Length < 2) return;
        
        switch (subs[1])
        {
            // case mode
            case "mode":
                args.Setting = "mode";
                switch (subs[2])
                {
                    case "classic":
                        args.Mode = Mode.Classic;
                        args.Value = "classic";
                        break;
                    case "stream":
                        args.Mode = Mode.Stream;
                        args.Value = "stream";
                        break;
                }
                break;
            
            // case volumesettings
            case "volumeSettings":
                switch (subs[2])
                {
                    //value = subs[5]
                    case "classic":
                        args.Mode = Mode.Classic;
                        args.StreamerMode = StreamerMode.None;
                        switch (subs[4])
                        {
                            //case Volume
                            case "Volume":
                                args.Setting = "volume";
                                switch (subs[3])
                                {
                                    case "Master":
                                        args.MixDevice = MixDevices.Master;
                                        args.Value = subs[5];
                                        break;
                                    case "game":
                                        args.MixDevice = MixDevices.Game;
                                        args.Value = subs[5];
                                        break;
                                    case "chatRender":
                                        args.MixDevice = MixDevices.Chat;
                                        args.Value = subs[5];
                                        break;
                                    case "chatCapture":
                                        args.MixDevice = MixDevices.Micro;
                                        args.Value = subs[5];
                                        break;
                                    case "media":
                                        args.MixDevice = MixDevices.Media;
                                        args.Value = subs[5];
                                        break;
                                    case "aux":
                                        args.MixDevice = MixDevices.Aux;
                                        args.Value = subs[5];
                                        break;
                                }
                                break;
                            //case Mute
                            case "Mute":
                                args.Setting = "mute";
                                switch (subs[3])
                                {
                                    case "Master":
                                        args.MixDevice = MixDevices.Master;
                                        args.Value = subs[5];
                                        break;
                                    case "game":
                                        args.MixDevice = MixDevices.Game;
                                        args.Value = subs[5];
                                        break;
                                    case "chatRender":
                                        args.MixDevice = MixDevices.Chat;
                                        args.Value = subs[5];
                                        break;
                                    case "chatCapture":
                                        args.MixDevice = MixDevices.Micro;
                                        args.Value = subs[5];
                                        break;
                                    case "media":
                                        args.MixDevice = MixDevices.Media;
                                        args.Value = subs[5];
                                        break;
                                    case "aux":
                                        args.MixDevice = MixDevices.Aux;
                                        args.Value = subs[5];
                                        break;
                                }
                                break;
                        }
                        break;
                    //value = subs[6]
                    case "streamer":
                        args.Mode = Mode.Stream;
                        switch (subs[5])
                        {
                            //case Volume
                            case "volume":
                                args.Setting = "volume";
                                switch (subs[3])
                                {
                                    case "streaming":
                                        args.StreamerMode = StreamerMode.Streaming;
                                        switch (subs[4])
                                        {
                                            case "Master":
                                                args.MixDevice = MixDevices.Master;
                                                args.Value = subs[6];
                                                break;
                                            case "game":
                                                args.MixDevice = MixDevices.Game;
                                                args.Value = subs[6];
                                                break;
                                            case "chatRender":
                                                args.MixDevice = MixDevices.Chat;
                                                args.Value = subs[6];
                                                break;
                                            case "chatCapture":
                                                args.MixDevice = MixDevices.Micro;
                                                args.Value = subs[6];
                                                break;
                                            case "media":
                                                args.MixDevice = MixDevices.Media;
                                                args.Value = subs[6];
                                                break;
                                            case "aux":
                                                args.MixDevice = MixDevices.Aux;
                                                args.Value = subs[6];
                                                break;
                                        }
                                        break;
                                    case "monitoring":
                                        args.StreamerMode = StreamerMode.Monitoring;
                                        switch (subs[4])
                                        {
                                            case "Master":
                                                args.MixDevice = MixDevices.Master;
                                                args.Value = subs[6];
                                                break;
                                            case "game":
                                                args.MixDevice = MixDevices.Game;
                                                args.Value = subs[6];
                                                break;
                                            case "chatRender":
                                                args.MixDevice = MixDevices.Chat;
                                                args.Value = subs[6];
                                                break;
                                            case "chatCapture":
                                                args.MixDevice = MixDevices.Micro;
                                                args.Value = subs[6];
                                                break;
                                            case "media":
                                                args.MixDevice = MixDevices.Media;
                                                args.Value = subs[6];
                                                break;
                                            case "aux":
                                                args.MixDevice = MixDevices.Aux;
                                                args.Value = subs[6];
                                                break;
                                        }
                                        break;
                                }
                                break;
                            //case Mute
                            case "isMuted":
                                args.Setting = "mute";
                                switch (subs[3])
                                {
                                    case "streaming":
                                        args.StreamerMode = StreamerMode.Streaming;
                                        switch (subs[4])
                                        {
                                            case "Master":
                                                args.MixDevice = MixDevices.Master;
                                                args.Value = subs[6];
                                                break;
                                            case "game":
                                                args.MixDevice = MixDevices.Game;
                                                args.Value = subs[6];
                                                break;
                                            case "chatRender":
                                                args.MixDevice = MixDevices.Chat;
                                                args.Value = subs[6];
                                                break;
                                            case "chatCapture":
                                                args.MixDevice = MixDevices.Micro;
                                                args.Value = subs[6];
                                                break;
                                            case "media":
                                                args.MixDevice = MixDevices.Media;
                                                args.Value = subs[6];
                                                break;
                                            case "aux":
                                                args.MixDevice = MixDevices.Aux;
                                                args.Value = subs[6];
                                                break;
                                        }
                                        break;
                                    case "monitoring":
                                        args.StreamerMode = StreamerMode.Monitoring;
                                        switch (subs[4])
                                        {
                                            case "Master":
                                                args.MixDevice = MixDevices.Master;
                                                args.Value = subs[6];
                                                break;
                                            case "game":
                                                args.MixDevice = MixDevices.Game;
                                                args.Value = subs[6];
                                                break;
                                            case "chatRender":
                                                args.MixDevice = MixDevices.Chat;
                                                args.Value = subs[6];
                                                break;
                                            case "chatCapture":
                                                args.MixDevice = MixDevices.Micro;
                                                args.Value = subs[6];
                                                break;
                                            case "media":
                                                args.MixDevice = MixDevices.Media;
                                                args.Value = subs[6];
                                                break;
                                            case "aux":
                                                args.MixDevice = MixDevices.Aux;
                                                args.Value = subs[6];
                                                break;
                                        }
                                        break;
                                }
                                break;
                        }
                        break;
                }
                break;
            
            // case Configs
            case "configs":
                if (subs.Length < 3)
                {
                    Console.WriteLine("This could be a bug or an error but it seems i handled it (config)");
                    break;
                }
                else
                {
                    args.Setting = "config";
                    args.Value = subs[2];
                    break;
                }
            
            // case chatmix
            default:
                if (subs[1].StartsWith("chatMix"))
                {
                    args.Setting = "chatmix";
                    args.Value = subs[1].Split("=")[1];
                    //value = subs[1].Split("=")[1]
                }
                else
                {
                    Console.WriteLine("This could be a bug or an error but it seems i handled it (other)");
                }
                break;
            
            // case devices
            case "classicRedirections":
                args.Setting = "devices";
                args.Mode = Mode.Classic;
                //value = subs[4]
                if (subs[3]=="deviceId")
                {
                    switch (subs[2])
                    {
                        case "game":
                            args.MixDevice = MixDevices.Game;
                            args.Value = subs[4];
                            break;
                        case "chat":
                            args.MixDevice = MixDevices.Chat;
                            args.Value = subs[4];
                            break;
                        case "mic":
                            args.MixDevice = MixDevices.Micro;
                            args.Value = subs[4];
                            break;
                        case "media":
                            args.MixDevice = MixDevices.Media;
                            args.Value = subs[4];
                            break;
                        case "aux":
                            args.MixDevice = MixDevices.Aux;
                            args.Value = subs[4];
                            break;
                    }
                }
                break;
            
            case "streamRedirections":
                args.Mode = Mode.Stream;
                //if (subs[2] == "isStreamMonitoringEnabled") break;
                // /streamRedirections/isStreamMonitoringEnabled/false
                if (subs[2] == "isStreamMonitoringEnabled")
                {
                    args.Setting = "audienceMonitoring";
                    args.Value = subs[3];
                    break;
                }
                
                switch (subs[3])
                {
                    // value = subs[4]
                    // streamRedirections/$streamerMode$/deviceId/$device$
                    case "deviceId":
                        args.Setting = "devices";
                        switch (subs[2])
                        {
                            case "mic":
                                args.MixDevice = MixDevices.Micro;
                                args.Value = subs[4];
                                break;
                            case "streaming":
                                args.StreamerMode = StreamerMode.Streaming;
                                args.Value = subs[4];
                                break;
                            case "monitoring":
                                args.StreamerMode = StreamerMode.Monitoring;
                                args.Value = subs[4];
                                break;
                        }
                        break;
                    case "redirections":
                        args.Setting = "redirectionState";
                        switch (subs[2])
                        {
                            case "streaming":
                                args.StreamerMode = StreamerMode.Streaming;
                                switch (subs[4])
                                {
                                    case "game":
                                        args.MixDevice = MixDevices.Game;
                                        args.Value = subs[6];
                                        break;
                                    case "chatRender":
                                        args.MixDevice = MixDevices.Chat;
                                        args.Value = subs[6];
                                        break;
                                    case "media":
                                        args.MixDevice = MixDevices.Media;
                                        args.Value = subs[6];
                                        break;
                                    case "aux":
                                        args.MixDevice = MixDevices.Aux;
                                        args.Value = subs[6];
                                        break;
                                    case "chatCapture":
                                        args.MixDevice = MixDevices.Micro;
                                        args.Value = subs[6];
                                        break;
                                }
                                break;
                            case "monitoring":
                                args.StreamerMode = StreamerMode.Monitoring;
                                switch (subs[4])
                                {
                                    case "game":
                                        args.MixDevice = MixDevices.Game;
                                        args.Value = subs[6];
                                        break;
                                    case "chatRender":
                                        args.MixDevice = MixDevices.Chat;
                                        args.Value = subs[6];
                                        break;
                                    case "media":
                                        args.MixDevice = MixDevices.Media;
                                        args.Value = subs[6];
                                        break;
                                    case "aux":
                                        args.MixDevice = MixDevices.Aux;
                                        args.Value = subs[6];
                                        break;
                                    case "chatCapture":
                                        args.MixDevice = MixDevices.Micro;
                                        args.Value = subs[6];
                                        break;
                                }
                                break;
                        }
                        break;
                }
                break;
        }
        
        //Setting       Mode                MixDevices      Value           StreamerMode
        // mode         Classic/Stream                      Mode
        // volume       Classic/Stream      MixDevices      Vol             StreamerMode
        // mute         Classic/Stream      MixDevices      True/False      StreamerMode
        // config                                           Config          
        // chatmix                                          balance         
        // devices      Classic/Stream      MixDevices      Device          #StreamerMode
        // redirectionState Stream          MixDevices      True/False      StreamerMode
        // audienceMonitoring Stream                           True/False
        
        OnSteelSeriesEvent(null, args);
    }
}

public class OnSteelSeriesEventArgs : EventArgs
{
    public override bool Equals(object obj)
    {
        if (obj is OnSteelSeriesEventArgs other)
        {
            return Setting == other.Setting && Mode == other.Mode && MixDevice == other.MixDevice &&
                   Value == other.Value && StreamerMode == other.StreamerMode;
        }

        return false;
    }
    
    public string Setting { get; set; }
    public Mode? Mode { get; set; }
    public MixDevices? MixDevice { get; set; }
    public string Value { get; set; }
    public StreamerMode? StreamerMode { get; set; }
}