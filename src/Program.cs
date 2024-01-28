using TPSteelSeriesGG.SteelSeriesAPI;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHTTPHandler;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesJsonParser;
using static TPSteelSeriesGG.SteelSeriesAPI.SteelSeriesHttpActionHandler;

namespace TPSteelSeriesGG;
class Program
{
    static void Main(string[] args)
    {
        // Console.WriteLine("Sonar WebServer Address: " + GetSonarWebServerAddress());
        // new Thread(StartSteelSeriesListener).Start();
        // OnSteelSeriesEvent += (sender, eventArgs) =>
        // {
        //     Console.WriteLine("" + eventArgs.Setting + " " + eventArgs.Mode + " " + eventArgs.StreamerMode + " " + eventArgs.MixDevice + " " + eventArgs.Value);
        // };
        var plugin = new SteelSeriesPluginMain();
        plugin.Run();
    }
}