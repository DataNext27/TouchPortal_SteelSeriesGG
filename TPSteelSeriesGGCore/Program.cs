using System.Security.Principal;
using System.Text;

namespace TPSteelSeriesGGCore;

class Program
{
    public static bool _isUnhandledExceptionHandled = false;
    static void Main(string[] args)
    {
        var fileWriter = new StreamWriter("log.txt", true) { AutoFlush = true };
        var consoleWriter = Console.Out;

        var multiWriterOut = new MultiTextWriter(consoleWriter, fileWriter);
        var multiWriterError = new MultiTextWriter(consoleWriter, fileWriter, true);

        Console.SetOut(multiWriterOut);
        Console.SetError(multiWriterError);
        
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            _isUnhandledExceptionHandled = true;
            
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                Console.Error.WriteLine($"Unhandled exception: {ex}");
            }
            else
            {
                Console.Error.WriteLine("Unhandled exception occurred.");
            }
            multiWriterError.Flush();
        };

        if (!IsRunAsAdmin())
        {
            Console.WriteLine("SteelSeries plugin must be run as an administrator!");
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

class MultiTextWriter : TextWriter
{
    private readonly TextWriter _consoleWriter;
    private readonly TextWriter _fileWriter;
    private readonly bool _isError;

    public MultiTextWriter(TextWriter consoleWriter, TextWriter fileWriter, bool isError = false)
    {
        _consoleWriter = consoleWriter;
        _fileWriter = fileWriter;
        _isError = isError;
    }

    public override Encoding Encoding => _consoleWriter.Encoding;

    private void WriteWithTimestamp(string value)
    {
        if (_isError)
        {
            string timestampedValue = $"[SteelSeries GG] [{DateTime.Now:HH:mm:ss}] [Error] {value}";
            if (Program._isUnhandledExceptionHandled)
            {
                _fileWriter.WriteLine(timestampedValue);
                Program._isUnhandledExceptionHandled = false;
            }
            else
            {
                _consoleWriter.WriteLine(timestampedValue);
                _fileWriter.WriteLine(timestampedValue);
            }
        }
        else
        {
            string timestampedValue = $"[SteelSeries GG] [{DateTime.Now:HH:mm:ss}] [Info] {value}";
            _consoleWriter.Write(timestampedValue);
            _fileWriter.Write(timestampedValue);
        }
        _fileWriter.Flush();
    }

    public override void Write(char value)
    {
        WriteWithTimestamp(value.ToString());
    }

    public override void Write(string value)
    {
        WriteWithTimestamp(value);
    }

    public override void WriteLine(string value)
    {
        WriteWithTimestamp(value + Environment.NewLine);
    }

    public override void Flush()
    {
        _consoleWriter.Flush();
        _fileWriter.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _consoleWriter?.Dispose();
            _fileWriter?.Dispose();
        }
        base.Dispose(disposing);
    }
}