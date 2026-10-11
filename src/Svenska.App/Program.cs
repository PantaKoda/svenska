using System.Diagnostics;
using Avalonia;
using Svenska.App.Services;
using Svenska.Core.Updates;

namespace Svenska.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        UpdateInstaller.CleanUp(AppUpdater.InstallDir);

        switch (args)
        {
            case ["--version"]:
                Console.WriteLine(AppUpdater.CurrentVersion);
                return 0;
            case ["--play", var file]:      // audio check without the UI
                return PlayAndWait(file);
            case ["--update"]:              // same as the Update button, without the UI
                return UpdateFromConsole();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int PlayAndWait(string file)
    {
        using var player = new OpenAlAudioPlayer();
        var clock = Stopwatch.StartNew();
        player.Play(file);
        while (player.IsPlaying)
            Thread.Sleep(20);
        Console.WriteLine($"Played {Path.GetFileName(file)} in {clock.Elapsed.TotalSeconds:0.00} s");
        return 0;
    }

    private static int UpdateFromConsole()
    {
        try
        {
            var result = AppUpdater.UpdateAsync().GetAwaiter().GetResult();
            Console.WriteLine(result.Message);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Update failed: {e.Message}");
            return 1;
        }
    }
}
