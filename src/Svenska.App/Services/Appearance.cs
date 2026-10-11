using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Svenska.App.Services;

/// <summary>An accent colour: the one colour that marks the next thing to do.</summary>
/// <param name="Day">On the light background; dark enough for light text on it.</param>
/// <param name="Evening">On the dark background; light enough for dark text on it.</param>
public sealed record Accent(string Name, Color Day, Color Evening);

/// <summary>
/// Theme (day, evening, or what the system uses) and accent colour. The choice is kept in
/// settings.json beside progress.db. All accents are muted on purpose; see Themes/Palette.axaml.
/// </summary>
public sealed class Appearance
{
    public const string System = "system", Day = "day", Evening = "evening";

    public static readonly Accent[] Accents =
    [
        new("Fjord", Color.Parse("#2F6F73"), Color.Parse("#86BDB8")),   // dusty teal
        new("Bärnsten", Color.Parse("#86651F"), Color.Parse("#D9BC84")), // amber; not red, which means "wrong" here
        new("Ljung", Color.Parse("#5B5A8C"), Color.Parse("#B3B1DD")),   // heather
        new("Mossa", Color.Parse("#55703F"), Color.Parse("#ABC391")),   // moss
    ];

    private sealed record Settings(string Theme, string Accent);

    private readonly string _path;

    public string Theme { get; private set; } = System;
    public Accent Accent { get; private set; } = Accents[0];

    public Appearance(string settingsPath)
    {
        _path = settingsPath;
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) is { } saved)
            {
                Theme = saved.Theme is Day or Evening ? saved.Theme : System;
                Accent = Accents.FirstOrDefault(a => a.Name == saved.Accent) ?? Accents[0];
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken settings file only costs the saved look.
        }
    }

    public void SetTheme(string theme)
    {
        Theme = theme;
        Save();
        Apply();
    }

    public void SetAccent(Accent accent)
    {
        Accent = accent;
        Save();
        Apply();
    }

    /// <summary>Puts the theme and the accent brushes into the running application.</summary>
    public void Apply()
    {
        if (Application.Current is not { } app)
            return;

        app.ActualThemeVariantChanged -= OnVariantChanged;
        app.RequestedThemeVariant = Theme switch
        {
            Day => ThemeVariant.Light,
            Evening => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        app.ActualThemeVariantChanged += OnVariantChanged;
        ApplyAccent(app);
    }

    private void OnVariantChanged(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
            ApplyAccent(app);
    }

    private void ApplyAccent(Application app)
    {
        var evening = app.ActualThemeVariant == ThemeVariant.Dark;
        var accent = evening ? Accent.Evening : Accent.Day;
        var background = Color.Parse(evening ? "#1F1C22" : "#F4F1E6");
        var onAccent = Color.Parse(evening ? "#1F1C22" : "#FBF9F1");

        app.Resources["AccentBrush"] = new SolidColorBrush(accent);
        app.Resources["AccentHoverBrush"] = new SolidColorBrush(Mix(accent, evening ? Colors.White : Colors.Black, 0.12));
        app.Resources["AccentSoftBrush"] = new SolidColorBrush(Mix(background, accent, 0.18));
        app.Resources["OnAccentBrush"] = new SolidColorBrush(onAccent);

        // The Fluent controls (focus frame, check box, selection) follow the accent too.
        app.Resources["SystemAccentColor"] = accent;
        for (var step = 1; step <= 3; step++)
        {
            app.Resources[$"SystemAccentColorDark{step}"] = Mix(accent, Colors.Black, 0.12 * step);
            app.Resources[$"SystemAccentColorLight{step}"] = Mix(accent, Colors.White, 0.12 * step);
        }
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount),
        (byte)(a.G + (b.G - a.G) * amount),
        (byte)(a.B + (b.B - a.B) * amount));

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Settings(Theme, Accent.Name)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The look still applies for this run.
        }
    }
}
