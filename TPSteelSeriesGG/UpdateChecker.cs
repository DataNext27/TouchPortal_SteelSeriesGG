using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TPSteelSeriesGG;

/// <summary>What the update check found: a release newer than the running plugin.</summary>
/// <param name="Latest">The latest released version.</param>
/// <param name="TagName">The release tag as published (e.g. "v2.1.0").</param>
/// <param name="Url">The release page to send the user to.</param>
public sealed record UpdateInfo(Version Latest, string TagName, string Url);

/// <summary>
/// Checks GitHub for a newer plugin release. Uses the /releases/latest endpoint, which
/// only ever returns full releases: drafts and pre-releases never trigger a notification
/// (and a repo with only pre-releases returns 404, treated as "no update").
/// Failures are silent by design; an update check must never hurt the plugin.
/// </summary>
public sealed class UpdateChecker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/DataNext27/TouchPortal_SteelSeriesGG/releases/latest";

    private readonly ILogger _logger;

    public UpdateChecker(ILogger logger) => _logger = logger;

    /// <summary>
    /// The version of the running plugin, from the csproj Version property, prerelease
    /// suffix included ("2.1.0-alpha.1"). Read from the informational version because the
    /// plain assembly version silently drops the suffix (an alpha would look final).
    /// Build metadata (+sha) is stripped for display and comparison.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            string? info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                return (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);
            int meta = info.IndexOf('+');
            return meta >= 0 ? info[..meta] : info;
        }
    }

    /// <summary>Returns the newer release if one exists, null otherwise. Never throws.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // GitHub rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TPSteelSeriesGG", CurrentVersion.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.GetAsync(LatestReleaseApi, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Update check: GitHub answered {Status}", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            string? tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;

            if (ParseVersion(tag) is not { } latest || url is null)
            {
                _logger.LogDebug("Update check: could not parse release tag {Tag}", tag);
                return null;
            }

            if (!IsNewer(tag!, CurrentVersion))
            {
                _logger.LogInformation("Plugin is up to date ({Current}, latest release {Latest})",
                    CurrentVersion, tag);
                return null;
            }

            _logger.LogInformation("Update available: {Latest} (running {Current})", tag, CurrentVersion);
            return new UpdateInfo(latest, tag!, url);
        }
        catch (Exception ex)
        {
            // Offline, DNS, rate limit, JSON surprise... none of it is the user's problem.
            _logger.LogDebug(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Parses a release tag ("v2.1.0", "2.1.0", "v2.1.0-beta.1") into its numeric version.
    /// Pre-release and build suffixes are ignored; returns null for anything unparseable.
    /// </summary>
    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0) s = s[..cut];

        return Version.TryParse(s, out var v) && v.Build >= 0 ? v : null;
    }

    /// <summary>
    /// True when <paramref name="remoteTag"/> is strictly newer than <paramref name="currentVersion"/>,
    /// semver style: numbers compare first, and at equal numbers a prerelease is older than
    /// the release ("2.1.0-alpha.1" &lt; "2.1.0"), so alpha users get notified of the final.
    /// Unparseable input is never "newer".
    /// </summary>
    internal static bool IsNewer(string remoteTag, string currentVersion)
    {
        if (ParseVersion(remoteTag) is not { } remote || ParseVersion(currentVersion) is not { } current)
            return false;

        if (remote != current)
            return remote > current;

        bool remoteIsPrerelease = HasPrereleaseSuffix(remoteTag);
        bool currentIsPrerelease = HasPrereleaseSuffix(currentVersion);
        return currentIsPrerelease && !remoteIsPrerelease;
    }

    /// <summary>True when the version text carries a prerelease suffix ("-alpha.1", "-rc.2"...).</summary>
    internal static bool HasPrereleaseSuffix(string version)
    {
        int meta = version.IndexOf('+');
        string s = meta >= 0 ? version[..meta] : version;
        return s.Contains('-');
    }
}
