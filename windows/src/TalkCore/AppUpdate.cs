using System.Text.Json.Nodes;

namespace HydayTalk.Core;

/// A newer release published on GitHub, with the Windows zip to download.
public sealed record AppUpdate(string Version, Uri Package, Uri Page)
{
    public static readonly Uri LatestReleaseURL = new("https://api.github.com/repos/mukiwu/hyday-talk/releases/latest");

    /// Reads GitHub's latest-release JSON; null unless it is newer than `current` and carries a Windows zip.
    public static AppUpdate? Parse(string json, string current)
    {
        try
        {
            var release = JsonNode.Parse(json);
            if (release?["draft"]?.GetValue<bool>() == true || release?["prerelease"]?.GetValue<bool>() == true) return null;
            var tag = release?["tag_name"]?.GetValue<string>();
            var page = release?["html_url"]?.GetValue<string>();
            if (tag == null || page == null) return null;
            var version = tag.StartsWith('v') ? tag[1..] : tag;
            if (!IsNewer(version, current)) return null;
            var asset = release!["assets"]?.AsArray().FirstOrDefault(a =>
                a?["name"]?.GetValue<string>() is { } name && name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));
            var url = asset?["browser_download_url"]?.GetValue<string>();
            return url == null ? null : new AppUpdate(version, new Uri(url), new Uri(page));
        }
        catch { return null; }
    }

    /// Compares dotted versions numerically, so 0.1.10 is newer than 0.1.9.
    public static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string text) => text.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var a = Parts(candidate); var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    public static async Task<AppUpdate?> CheckAsync(string current, HttpClient? client = null, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseURL);
        request.Headers.UserAgent.ParseAdd("HydayTalk-Windows/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using var response = await (client ?? APIClient.Shared).SendAsync(request, cancellation);
            return response.IsSuccessStatusCode ? Parse(await response.Content.ReadAsStringAsync(cancellation), current) : null;
        }
        catch (HttpRequestException) { return null; }
    }
}
