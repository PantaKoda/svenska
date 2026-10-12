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

    /// <summary>The colour sets: the calm one with a free accent, and the Swedish one.</summary>
    public const string Calm = "calm", Sverige = "sverige";

    /// <summary>
    /// The Swedish colours, light ("Björk") and dark ("Vinternatt"): flag blue for the next
    /// thing to do and for en-words, flag yellow for progress and for ett-words, both
    /// softened, on birch white or a winter night. Resource key, light colour, dark colour.
    /// </summary>
    private static readonly (string Key, string Light, string Dark)[] SverigeColours =
    [
        ("AppBackgroundBrush", "#F5F3EE", "#111922"),
        ("SurfaceBrush", "#FBFAF7", "#18222D"),
        ("SurfaceAltBrush", "#ECE9E1", "#223040"),
        ("LineBrush", "#D6D2C7", "#33414F"),
        ("TextBrush", "#1B2733", "#E9E6DF"),
        ("MutedBrush", "#55616D", "#A4AEB8"),
        ("GoodBrush", "#2D6A4C", "#7CC49E"),
        ("GoodSoftBrush", "#DCEBE2", "#1C3329"),
        ("BadBrush", "#8E2A1E", "#E8806C"),
        ("BadSoftBrush", "#F3DFDA", "#3D2420"),
        ("AccentBrush", "#1A5E8C", "#7BB3DE"),
        ("OnAccentBrush", "#FBFAF7", "#0E1620"),
        ("AccentSoftBrush", "#DDE8F0", "#1E3447"),
        ("ProgressBrush", "#F2C230", "#E9C04A"),
        ("EnWordBrush", "#1A5E8C", "#8DBFE6"),
        ("EttWordBrush", "#7F5A00", "#EBC75A"),
    ];

    /// <summary>The keys that Palette.axaml defines itself; in the calm set they are left to it.</summary>
    private static readonly string[] PaletteKeys =
    [
        "AppBackgroundBrush", "SurfaceBrush", "SurfaceAltBrush", "LineBrush", "TextBrush", "MutedBrush",
        "GoodBrush", "GoodSoftBrush", "BadBrush", "BadSoftBrush",
    ];

    public static readonly Accent[] Accents =
    [
        new("Fjord", Color.Parse("#2F6F73"), Color.Parse("#86BDB8")),   // dusty teal
        new("Bärnsten", Color.Parse("#86651F"), Color.Parse("#D9BC84")), // amber; not red, which means "wrong" here
        new("Ljung", Color.Parse("#5B5A8C"), Color.Parse("#B3B1DD")),   // heather
        new("Mossa", Color.Parse("#55703F"), Color.Parse("#ABC391")),   // moss
    ];

    private sealed record Settings(string Theme, string Accent, string? Palette = null);

    private readonly string _path;

    public string Theme { get; private set; } = System;
    public Accent Accent { get; private set; } = Accents[0];
    public string Palette { get; private set; } = Calm;

    public Appearance(string settingsPath)
    {
        _path = settingsPath;
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) is { } saved)
            {
                Theme = saved.Theme is Day or Evening ? saved.Theme : System;
                Accent = Accents.FirstOrDefault(a => a.Name == saved.Accent) ?? Accents[0];
                Palette = saved.Palette == Sverige ? Sverige : Calm;
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

    public void SetPalette(string palette)
    {
        Palette = palette == Sverige ? Sverige : Calm;
        Save();
        Apply();
    }

    /// <summary>Choosing an accent goes back to the calm set: the Swedish set has its own blue.</summary>
    public void SetAccent(Accent accent)
    {
        Accent = accent;
        Palette = Calm;
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
        if (Palette == Sverige)
        {
            foreach (var (key, light, dark) in SverigeColours)
                app.Resources[key] = new SolidColorBrush(Color.Parse(evening ? dark : light));
            var blue = Color.Parse(evening ? "#7BB3DE" : "#1A5E8C");
            app.Resources["AccentHoverBrush"] = new SolidColorBrush(Mix(blue, evening ? Colors.White : Colors.Black, 0.12));
            SetSystemAccent(app, blue);
            return;
        }

        // The calm set: its surfaces come from Palette.axaml again.
        foreach (var key in PaletteKeys)
            app.Resources.Remove(key);
        var accent = evening ? Accent.Evening : Accent.Day;
        var background = Color.Parse(evening ? "#1F1C22" : "#F4F1E6");
        var onAccent = Color.Parse(evening ? "#1F1C22" : "#FBF9F1");

        app.Resources["AccentBrush"] = new SolidColorBrush(accent);
        app.Resources["AccentHoverBrush"] = new SolidColorBrush(Mix(accent, evening ? Colors.White : Colors.Black, 0.12));
        app.Resources["AccentSoftBrush"] = new SolidColorBrush(Mix(background, accent, 0.18));
        app.Resources["OnAccentBrush"] = new SolidColorBrush(onAccent);
        app.Resources["ProgressBrush"] = new SolidColorBrush(accent);
        // No colour for en and ett here: the words keep the text colour.
        var text = new SolidColorBrush(Color.Parse(evening ? "#EDE6D8" : "#2B2A33"));
        app.Resources["EnWordBrush"] = text;
        app.Resources["EttWordBrush"] = text;
        SetSystemAccent(app, accent);
    }

    /// <summary>The Fluent controls (focus frame, check box, selection) follow the accent too.</summary>
    private static void SetSystemAccent(Application app, Color accent)
    {
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
            File.WriteAllText(_path, JsonSerializer.Serialize(new Settings(Theme, Accent.Name, Palette)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The look still applies for this run.
        }
    }
}
