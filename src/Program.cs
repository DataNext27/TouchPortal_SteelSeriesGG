using System.Security.Principal;

namespace TPSteelSeriesGG;
class Program
{
    static void Main(string[] args)
    {
        if (!IsRunAsAdmin())
        {
            Console.WriteLine("[SteelSeries GG] Touch Portal must be run as an administrator!");
            Environment.Exit(1);
        }
        else
        {
            var plugin = new SteelSeriesPluginMain();
            plugin.Run();
        }
    }

    private static bool IsRunAsAdmin()
    {
        WindowsIdentity id = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(id);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}