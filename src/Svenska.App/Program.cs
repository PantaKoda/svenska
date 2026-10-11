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
            case ["--scene", var svg, var png]:   // scene check: lists the word elements, saves a picture
                return CheckScene(svg, png);
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

    private static int CheckScene(string svgPath, string pngPath)
    {
        using var scene = new SvgScene(svgPath);
        Console.WriteLine($"{Path.GetFileName(svgPath)}: {scene.Width} x {scene.Height}, {scene.Map.Bounds.Count} word elements");
        foreach (var (id, box) in scene.Map.Bounds.OrderBy(b => b.Key))
        {
            var (x, y) = ((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
            Console.WriteLine($"  {id,-12} box {box.Left:0},{box.Top:0} – {box.Right:0},{box.Bottom:0}   click at its centre hits: {scene.HitTest(x, y)}");
        }
        File.WriteAllBytes(pngPath, scene.RenderPng(800, 500, scene.Map.Bounds.Keys.Take(1).Select(id => (id, true))));
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
