using SteelSeriesAPI.Sonar.Enums;

namespace TPSteelSeriesGG;

/// <summary>
/// Conversions between the library's enums and the Touch Portal vocabulary
/// (state id key segments, and the display values used in entry.tp choices).
/// </summary>
internal static class TpMappings
{
    /// <summary>The id key segment for a channel ("game", "chat"...). Matches TpIds.ChannelKeys.</summary>
    public static string Key(this Channel channel) => channel.ToString().ToLowerInvariant();

    /// <summary>The id key segment for a volume/mute family: "classic" when mix is null.</summary>
    public static string Key(this Mix? mix) => mix switch
    {
        null => "classic",
        Mix.Personal => "personal",
        Mix.Stream => "stream",
        _ => "classic",
    };

    /// <summary>The display value for a channel, as declared in entry.tp choices ("Game", "Chat"...).</summary>
    public static string Display(this Channel channel) => channel.ToString();

    /// <summary>The display value for a mix family ("Classic", "Personal", "Stream").</summary>
    public static string Display(this Mix? mix) => mix switch
    {
        null => "Classic",
        Mix.Personal => "Personal",
        Mix.Stream => "Stream",
        _ => "Classic",
    };

    /// <summary>Parses an entry.tp channel display value ("Game", "Chat"...). Null when unknown.</summary>
    public static Channel? ParseChannel(string? display) => display switch
    {
        "Master" => Channel.Master,
        "Game" => Channel.Game,
        "Chat" => Channel.Chat,
        "Media" => Channel.Media,
        "Aux" => Channel.Aux,
        "Mic" => Channel.Mic,
        _ => null,
    };

    /// <summary>Parses an entry.tp streamer mix display value ("Personal"/"Stream"). Null when unknown.</summary>
    public static Mix? ParseStreamerMix(string? display) => display switch
    {
        "Personal" => Mix.Personal,
        "Stream" => Mix.Stream,
        _ => null,
    };

    /// <summary>Parses an entry.tp mode display value ("Classic"/"Streamer"). Null when unknown.</summary>
    public static Mode? ParseMode(string? display) => display switch
    {
        "Classic" => Mode.Classic,
        "Streamer" => Mode.Streamer,
        _ => null,
    };

    /// <summary>A volume (0..1 double from the library) as the 0-100 integer Touch Portal convention.</summary>
    public static int ToTpVolume(double volume) => (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);

    /// <summary>A chat mix balance (-1..1) as the -100..100 integer shown in the balance state.</summary>
    public static int ToTpBalance(double balance) => (int)Math.Round(Math.Clamp(balance, -1, 1) * 100);

    /// <summary>A chat mix balance (-1..1) as the 0-100 connector position.</summary>
    public static int ToTpBalanceConnector(double balance) => (int)Math.Round((Math.Clamp(balance, -1, 1) + 1) * 50);

    /// <summary>
    /// The fully-qualified connector key used both for connectorUpdate messages and
    /// for the echo guard. Data values must match the entry.tp choices exactly.
    /// </summary>
    public static string ConnectorKey(string connectorId, string? channelDisplay = null, string? mixDisplay = null)
    {
        string key = connectorId;
        if (channelDisplay is not null) key += $"|{TpIds.Data.Channel}={channelDisplay}";
        if (mixDisplay is not null) key += $"|{TpIds.Data.Mix}={mixDisplay}";
        return key;
    }
}