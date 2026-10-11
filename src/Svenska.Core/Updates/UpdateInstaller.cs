using System.IO.Compression;

namespace Svenska.Core.Updates;

/// <summary>
/// Replaces an installed copy of the app with the contents of a release zip.
/// A running program cannot be overwritten on Windows but its files can be moved, so the
/// old files are moved aside into a folder that <see cref="CleanUp"/> deletes on the next start.
/// User progress lives in the app-data folder and is not touched.
/// </summary>
public static class UpdateInstaller
{
    private const string WorkPrefix = ".update-";
    public const string AppFileName = "Svenska.App";

    /// <summary>
    /// True for an unpacked release (content/ sits beside the program). False when running
    /// from the source tree, where there is nothing to replace.
    /// </summary>
    public static bool IsInstalledCopy(string installDir) =>
        Directory.Exists(Path.Combine(installDir, "content"));

    public static void Install(string zipPath, string installDir)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(installDir, $"{WorkPrefix}new-{id}");
        var old = Path.Combine(installDir, $"{WorkPrefix}old-{id}");

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);
            if (!IsInstalledCopy(staging) || !Directory.EnumerateFiles(staging, AppFileName + "*").Any())
                throw new InvalidDataException("The download does not look like a Svenska release. Nothing was installed.");
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        var moved = new List<(string From, string To)>();
        try
        {
            foreach (var file in FilesToReplace(installDir))
                Move(file, Path.Combine(old, Path.GetRelativePath(installDir, file)), moved);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList())
                Move(file, Path.Combine(installDir, Path.GetRelativePath(staging, file)), moved);
        }
        catch
        {
            // Put everything back, newest move first.
            for (var i = moved.Count - 1; i >= 0; i--)
                File.Move(moved[i].To, moved[i].From);
            TryDelete(staging);
            TryDelete(old);
            throw;
        }

        TryDelete(staging);
        DeleteEmptyDirectories(installDir);

        if (!OperatingSystem.IsWindows() && File.Exists(Path.Combine(installDir, AppFileName)))
            File.SetUnixFileMode(Path.Combine(installDir, AppFileName),
                File.GetUnixFileMode(Path.Combine(installDir, AppFileName))
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>Deletes what an earlier update left behind. Safe to call on every start.</summary>
    public static void CleanUp(string installDir)
    {
        if (!Directory.Exists(installDir))
            return;
        foreach (var dir in Directory.EnumerateDirectories(installDir, WorkPrefix + "*"))
            TryDelete(dir);
    }

    private static List<string> FilesToReplace(string installDir) =>
        Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(installDir, f).StartsWith(WorkPrefix, StringComparison.Ordinal))
            .ToList();

    private static void Move(string from, string to, List<(string, string)> moved)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
        moved.Add((from, to));
    }

    private static void DeleteEmptyDirectories(string installDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(installDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use by the running program; CleanUp removes it on a later start.
        }
    }
}
