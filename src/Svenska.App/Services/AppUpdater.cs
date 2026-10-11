using System.Reflection;
using System.Runtime.InteropServices;
using Svenska.Core.Updates;

namespace Svenska.App.Services;

public enum UpdateOutcome { UpToDate, Installed, AvailableButNotInstallable, NothingPublished }

public sealed record UpdateResult(UpdateOutcome Outcome, string Message);

/// <summary>Checks GitHub for a newer release and installs it over this copy of the app.</summary>
public static class AppUpdater
{
    public static string InstallDir => AppContext.BaseDirectory;

    public static Version CurrentVersion { get; } = UpdateClient.ParseVersion(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0, 0);

    public static async Task<UpdateResult> UpdateAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        using var http = UpdateClient.CreateHttpClient();
        var client = new UpdateClient(http);

        status?.Report("Checking for updates…");
        var latest = await client.GetLatestAsync(RuntimeInformation.RuntimeIdentifier, ct);
        if (latest == null)
            return new(UpdateOutcome.NothingPublished, "No release has been published yet.");
        if (latest.Version <= CurrentVersion)
            return new(UpdateOutcome.UpToDate, $"You have the latest version ({CurrentVersion}).");

        if (!UpdateInstaller.IsInstalledCopy(InstallDir))
            return new(UpdateOutcome.AvailableButNotInstallable,
                $"Version {latest.Version} is available, but this copy runs from the source tree. Get it at {latest.PageUrl}");
        if (latest.AssetUrl == null)
            return new(UpdateOutcome.AvailableButNotInstallable,
                $"Version {latest.Version} has no download for {RuntimeInformation.RuntimeIdentifier}. See {latest.PageUrl}");

        var tempDir = Path.Combine(Path.GetTempPath(), "svenska-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            var percent = new Progress<double>(p => status?.Report($"Downloading {latest.Version}… {p:P0}"));
            var zip = await client.DownloadAsync(latest, tempDir, percent, ct);
            status?.Report($"Installing {latest.Version}…");
            await Task.Run(() => UpdateInstaller.Install(zip, InstallDir), ct);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        return new(UpdateOutcome.Installed, $"Version {latest.Version} is installed.");
    }
}
