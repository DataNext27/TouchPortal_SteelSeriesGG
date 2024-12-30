using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using TouchPortalSDK;
using TouchPortalSDK.Interfaces;

namespace TPSteelSeriesGG;

public class PluginLogger : ITouchPortalEventHandler
{
    public string PluginId => "steelseries-gg";
    private readonly ITouchPortalClient _client;
    
    private Process _coreProcess;
    private NamedPipeServerStream _pipeServer;
    private NamedPipeClientStream _monitoringPipeClient;
    
    public PluginLogger()
    {
        _client = TouchPortalFactory.CreateClient(this);
        _coreProcess = new Process();
        
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),   
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),   
            PipeAccessRights.FullControl, AccessControlType.Allow));
        
        _pipeServer = NamedPipeServerStreamAcl.Create("TP_steelseries-gg_plugin_logging", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.None, 0, 0, pipeSecurity);
        _monitoringPipeClient = new NamedPipeClientStream(".", "TP_steelseries-gg_plugin_monitoring", PipeDirection.InOut);
    }

    public void Run()
    {
        _client.Connect();
        
        // Open core process
        _coreProcess.StartInfo.FileName = "TPSteelSeriesGGCore.exe";
        _coreProcess.StartInfo.Arguments = "";
        _coreProcess.StartInfo.UseShellExecute = true;
        _coreProcess.StartInfo.CreateNoWindow = true;
        _coreProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        _coreProcess.StartInfo.Verb = "runas";
        _coreProcess.Start();
        
        Console.WriteLine("Logger Initialized!");
        
        _pipeServer.WaitForConnection();
        Console.WriteLine("Core connected");
        
        _monitoringPipeClient.Connect();
        Console.WriteLine("Connected to core");
        
        var reader = new StreamReader(_pipeServer);

        try
        {
            while (true)
            {
                string log = reader.ReadLine();
                if (log == null)
                {
                    Console.WriteLine("Client disconnected.");
                    break;
                }

                Console.WriteLine($"Core: {log}");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Pipe closed by client: {ex.Message}");
        }
        _coreProcess.Kill();
        reader.Close();
        _pipeServer.Close();
        Environment.Exit(0);
    }
    
    public void OnClosedEvent(string message)
    {
        _coreProcess.Kill();
        _pipeServer.Close();
        Environment.Exit(0);
    }
}