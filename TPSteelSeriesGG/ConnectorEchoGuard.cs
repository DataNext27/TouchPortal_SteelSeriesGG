namespace TPSteelSeriesGG;

/// <summary>
/// Prevents connector echo loops: when the user drags a Touch Portal slider, the plugin
/// writes to Sonar, the polling detects the change, and without this guard the plugin
/// would send connector updates back to Touch Portal while the user is still dragging,
/// flooding it (Touch Portal flags this as error E3081).
/// </summary>
public sealed class ConnectorEchoGuard
{
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, DateTime> _lastTpMove = new();
    private readonly object _gate = new();

    /// <summary>Records that Touch Portal itself just moved this connector.</summary>
    public void NoteTpMove(string connectorKey)
    {
        lock (_gate) _lastTpMove[connectorKey] = DateTime.UtcNow;
    }

    /// <summary>Whether an update for this connector may be echoed back to Touch Portal.</summary>
    public bool ShouldEcho(string connectorKey)
    {
        lock (_gate)
            return !_lastTpMove.TryGetValue(connectorKey, out DateTime last) ||
                   DateTime.UtcNow - last > EchoWindow;
    }
}