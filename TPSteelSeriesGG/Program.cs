namespace TPSteelSeriesGG;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var plugin = new SteelSeriesPluginMain();
        plugin.Run();
    }
}