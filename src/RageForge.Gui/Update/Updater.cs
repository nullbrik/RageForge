/* RAGEFORGE - Updater */

using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RageForge.Gui.Update;

public sealed record UpdateInfo(Version Version, string Tag, string PageUrl);

public static class Updater
{
    private const string Repository = "nullbrik/RageForge";
    private const string ReleasesPage = "https://github.com/" + Repository + "/releases";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("RageForge-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var info = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var text = info?.Split('+')[0];
            var v = Version.TryParse(text, out var parsed) ? parsed : assembly?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (!resp.IsSuccessStatusCode)
            return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        if (latest <= CurrentVersion)
            return null;

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        return new UpdateInfo(latest, tag, IsReleasePage(page) ? page! : ReleasesPage);
    }

    public static bool IsReleasePage(string? url) =>
        url is not null && url.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) && Uri.IsWellFormedUriString(url, UriKind.Absolute);

    public static void OpenReleasePage(UpdateInfo info) =>
        Process.Start(new ProcessStartInfo(IsReleasePage(info.PageUrl) ? info.PageUrl : ReleasesPage) { UseShellExecute = true })?.Dispose();
}
