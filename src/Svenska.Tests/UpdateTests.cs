using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Svenska.Core.Updates;

namespace Svenska.Tests;

public class UpdateTests
{
    private const string Base = "https://github.com/PantaKoda/svenska/releases/download/v1.2.0/";

    private const string LatestJson = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/PantaKoda/svenska/releases/tag/v1.2.0",
          "assets": [
            { "name": "Svenska-1.2.0-win-x64.zip",   "browser_download_url": "BASESvenska-1.2.0-win-x64.zip" },
            { "name": "Svenska-1.2.0-linux-x64.zip", "browser_download_url": "BASESvenska-1.2.0-linux-x64.zip" },
            { "name": "Evil-1.2.0-osx-x64.zip",      "browser_download_url": "https://example.com/Evil-1.2.0-osx-x64.zip" },
            { "name": "SHA256SUMS.txt",              "browser_download_url": "BASESHA256SUMS.txt" }
          ]
        }
        """;

    /// <summary>Answers requests from a table of URL → body; anything else is 404.</summary>
    private sealed class FakeGitHub(Dictionary<string, byte[]> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static UpdateClient Client(Dictionary<string, byte[]> pages) => new(new HttpClient(new FakeGitHub(pages)));

    private static Dictionary<string, byte[]> Release(byte[] zip, string? checksum = null) => new()
    {
        ["https://api.github.com/repos/PantaKoda/svenska/releases/latest"] =
            System.Text.Encoding.UTF8.GetBytes(LatestJson.Replace("BASE", Base)),
        [Base + "Svenska-1.2.0-win-x64.zip"] = zip,
        [Base + "SHA256SUMS.txt"] = System.Text.Encoding.UTF8.GetBytes(
            $"{checksum ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant()}  Svenska-1.2.0-win-x64.zip\n"),
    };

    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create))
        {
            foreach (var (path, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        return memory.ToArray();
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("0.0.0-dev", "0.0.0")]
    [InlineData("1.2.3+4f5a", "1.2.3")]
    public void ParseVersion_reads_tags_and_assembly_versions(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateClient.ParseVersion(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    public void ParseVersion_rejects_what_is_not_a_version(string text)
    {
        Assert.Null(UpdateClient.ParseVersion(text));
    }

    [Fact]
    public async Task Latest_release_picks_the_download_for_this_system()
    {
        var release = await Client(Release([])).GetLatestAsync("linux-x64");

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("Svenska-1.2.0-linux-x64.zip", release.AssetName);
        Assert.Equal(Base + "SHA256SUMS.txt", release.ChecksumsUrl);
        Assert.True(release.Version > UpdateClient.ParseVersion("1.1.9"));
    }

    [Fact]
    public async Task Latest_release_ignores_downloads_outside_the_repository()
    {
        var release = await Client(Release([])).GetLatestAsync("osx-x64");

        Assert.Null(release!.AssetUrl);
    }

    [Fact]
    public async Task No_published_release_gives_null()
    {
        Assert.Null(await Client([]).GetLatestAsync("win-x64"));
    }

    [Fact]
    public async Task Download_is_saved_when_the_checksum_matches()
    {
        using var dir = new TempDir();
        var zip = Zip(("content/levels.yaml", "A1:"));
        var client = Client(Release(zip));

        var path = await client.DownloadAsync((await client.GetLatestAsync("win-x64"))!, dir.Path);

        Assert.Equal(zip, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Download_with_a_wrong_checksum_is_refused_and_deleted()
    {
        using var dir = new TempDir();
        var client = Client(Release(Zip(("a.txt", "a")), checksum: new string('0', 64)));
        var release = (await client.GetLatestAsync("win-x64"))!;

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, dir.Path));

        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Install_replaces_every_file_and_removes_what_the_new_version_dropped()
    {
        using var dir = new TempDir();
        var install = Path.Combine(dir.Path, "app");
        dir.Write("app/Svenska.App.exe", "old program");
        dir.Write("app/content/levels.yaml", "old");
        dir.Write("app/content/units/a1/99-removed.yaml", "gone in the new version");
        var zip = dir.Write("new.zip", "");
        File.WriteAllBytes(zip, Zip(("Svenska.App.exe", "new program"), ("content/levels.yaml", "new"),
            ("content/units/a1/01-jag.yaml", "added")));

        UpdateInstaller.Install(zip, install);

        Assert.Equal("new program", File.ReadAllText(Path.Combine(install, "Svenska.App.exe")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "content", "levels.yaml")));
        Assert.True(File.Exists(Path.Combine(install, "content", "units", "a1", "01-jag.yaml")));
        Assert.False(File.Exists(Path.Combine(install, "content", "units", "a1", "99-removed.yaml")));

        UpdateInstaller.CleanUp(install);
        Assert.Empty(Directory.GetDirectories(install, ".update-*"));
    }

    [Fact]
    public void Install_keeps_the_old_version_when_the_zip_is_not_a_release()
    {
        using var dir = new TempDir();
        var install = Path.Combine(dir.Path, "app");
        dir.Write("app/Svenska.App.exe", "old program");
        dir.Write("app/content/levels.yaml", "old");
        var zip = dir.Write("new.zip", "");
        File.WriteAllBytes(zip, Zip(("readme.txt", "not the app")));

        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Install(zip, install));

        Assert.Equal("old program", File.ReadAllText(Path.Combine(install, "Svenska.App.exe")));
        Assert.Equal(2, Directory.GetFiles(install, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Install_puts_everything_back_when_a_file_is_locked()
    {
        if (!OperatingSystem.IsWindows())
            return; // an open file can be moved on Linux

        using var dir = new TempDir();
        var install = Path.Combine(dir.Path, "app");
        dir.Write("app/Svenska.App.exe", "old program");
        dir.Write("app/content/levels.yaml", "old");
        var locked = dir.Write("app/data/saldo.db", "in use");
        var zip = dir.Write("new.zip", "");
        File.WriteAllBytes(zip, Zip(("Svenska.App.exe", "new program"), ("content/levels.yaml", "new")));

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => UpdateInstaller.Install(zip, install));

        Assert.Equal("old program", File.ReadAllText(Path.Combine(install, "Svenska.App.exe")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "content", "levels.yaml")));
    }
}
