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

    /// <summary>A volume (0..1 double from the library) as the 0-100 integer Touch Portal convention.</summary>
    public static int ToTpVolume(double volume) => (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);

    /// <summary>A chat mix balance (-1..1) as the -100..100 integer shown in the balance state.</summary>
    public static int ToTpBalance(double balance) => (int)Math.Round(Math.Clamp(balance, -1, 1) * 100);

    /// <summary>A chat mix balance (-1..1) as the 0-100 connector position.</summary>
    public static int ToTpBalanceConnector(double balance) => (int)Math.Round((Math.Clamp(balance, -1, 1) + 1) * 50);
}