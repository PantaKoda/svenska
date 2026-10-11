using System.Security.Cryptography;
using System.Text.Json;

namespace Svenska.Core.Updates;

/// <summary>A published release and the download that fits this machine (null if there is none).</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? AssetName, string? AssetUrl, string? ChecksumsUrl);

/// <summary>
/// Finds and downloads the latest release from GitHub. This is the only network access in
/// the app and it runs only when the user asks for an update.
/// </summary>
public sealed class UpdateClient(HttpClient http, string repository = UpdateClient.Repository)
{
    public const string Repository = "PantaKoda/svenska";
    public const string ChecksumsFile = "SHA256SUMS.txt";

    private string DownloadPrefix => $"https://github.com/{repository}/releases/download/";

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Svenska-App");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>"v1.2.3", "1.2.3-beta.1" and "1.2.3+commit" all give 1.2.3. Null if it is not a version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var version) && version.Build >= 0
            ? new Version(version.Major, version.Minor, version.Build)
            : null;
    }

    /// <summary>The latest published release, or null when nothing has been released yet.</summary>
    /// <param name="rid">Runtime identifier of this build, e.g. win-x64; picks the matching zip.</param>
    public async Task<ReleaseInfo?> GetLatestAsync(string rid, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag) ?? throw new InvalidDataException($"Release tag '{tag}' is not a version.");

        string? assetName = null, assetUrl = null, checksumsUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
                continue;
            if (name == ChecksumsFile)
                checksumsUrl = url;
            else if (name.EndsWith($"-{rid}.zip", StringComparison.OrdinalIgnoreCase))
                (assetName, assetUrl) = (name, url);
        }
        return new ReleaseInfo(version, tag, root.GetProperty("html_url").GetString() ?? "", assetName, assetUrl, checksumsUrl);
    }

    /// <summary>
    /// Downloads the release zip into <paramref name="targetDir"/> and checks it against the
    /// release's SHA-256 list. Returns the path of the zip.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string targetDir, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (release.AssetUrl == null || release.AssetName == null)
            throw new InvalidOperationException($"Release {release.Tag} has no download for this system.");
        if (release.ChecksumsUrl == null)
            throw new InvalidOperationException($"Release {release.Tag} has no {ChecksumsFile}.");

        var expected = FindChecksum(await http.GetStringAsync(release.ChecksumsUrl, ct), release.AssetName)
            ?? throw new InvalidDataException($"{ChecksumsFile} does not list {release.AssetName}.");

        Directory.CreateDirectory(targetDir);
        var zipPath = Path.Combine(targetDir, Path.GetFileName(release.AssetName));
        using (var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(zipPath);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0)
                    progress?.Report((double)done / total.Value);
            }
        }

        await using var zip = File.OpenRead(zipPath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(zip, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            zip.Close();
            File.Delete(zipPath);
            throw new InvalidDataException("The download is damaged (checksum does not match). Nothing was installed.");
        }
        return zipPath;
    }

    /// <summary>Reads a `sha256sum` list: one "&lt;hash&gt;  &lt;file name&gt;" per line.</summary>
    private static string? FindChecksum(string checksums, string fileName)
    {
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName)
                return parts[0];
        }
        return null;
    }
}
