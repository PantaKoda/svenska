using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.App.Services;
using Svenska.Core.Content;
using Svenska.Core.Exercises;

namespace Svenska.App.ViewModels;

/// <summary>A choice in the appearance menu: an accent colour, shown in its day shade.</summary>
public sealed record AccentRow(string Name, IBrush Swatch, IRelayCommand Choose);

/// <summary>The window: a header, one page at a time (home, unit, exercises), the reference panel.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private string _updateStatus = "";

    public AppServices Services { get; }

    public ReferenceViewModel Reference { get; }

    public string About => $"Svenska {AppUpdater.CurrentVersion}  ·  Data: SALDO, Språkbanken (CC-BY 4.0)";

    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        Reference = new ReferenceViewModel(services);
        ShowHome();
    }

    /// <summary>The accent colours of the appearance menu.</summary>
    public IReadOnlyList<AccentRow> Accents => [.. Appearance.Accents.Select(accent =>
        new AccentRow(accent.Name, new SolidColorBrush(accent.Day), new RelayCommand(() => Services.Appearance.SetAccent(accent))))];

    /// <summary>"system", "day" or "evening".</summary>
    [RelayCommand]
    private void SetTheme(string theme) => Services.Appearance.SetTheme(theme);

    /// <summary>"calm" or "sverige".</summary>
    [RelayCommand]
    private void SetPalette(string palette) => Services.Appearance.SetPalette(palette);

    [RelayCommand]
    private void Home()
    {
        Services.StopAudio();
        ShowHome();
    }

    public void ShowHome() => Page = new HomeViewModel(this);

    public void ShowUnit(Unit unit)
    {
        Reference.SetLevel(unit.Level);
        Page = new UnitViewModel(this, unit);
    }

    public void ShowExercises(Unit unit) => Page = new PlayerViewModel(this, new PlayerSession(
        unit.Title,
        Services.Progress.UnitSession(unit, Random.Shared),
        unit.Names,
        (item, correct) => Services.Progress.RecordUnitAnswer(unit, item, correct),
        () => ShowUnit(unit),
        Glosses: unit.Glosses));

    /// <summary>The unit's final task: a scripted dialogue, one reply of the learner at a time.</summary>
    public void ShowFinalTask(Unit unit)
    {
        if (unit.FinalTask == null)
            return;
        var steps = DialogueTask.Steps(unit.FinalTask).ToDictionary(s => s.Item, ReferenceEqualityComparer.Instance as IEqualityComparer<ExerciseItem>);
        Page = new PlayerViewModel(this, new PlayerSession(
            $"{unit.Title} · final task",
            [.. steps.Keys],
            unit.Names,
            (item, correct) => Services.Progress.RecordFinalAnswer(unit, steps[item], correct),
            () => ShowUnit(unit),
            item => steps[item].Heard,
            unit.Glosses));
    }

    public void ShowTheme(ThemePack pack) => Page = new ThemeViewModel(this, pack);

    public void ShowThemeExercises(ThemePack pack, bool includeNewWords) => Page = new PlayerViewModel(this, new PlayerSession(
        pack.Title,
        Services.Progress.ThemeSession(pack, includeNewWords, Random.Shared),
        [],
        (item, correct) => Services.Progress.RecordThemeAnswer(pack, item, correct),
        () => ShowTheme(pack)));

    /// <summary>The words and missed items that are due.</summary>
    public void ShowReview()
    {
        var entries = Services.Progress.ReviewSession();
        var keys = new Dictionary<ExerciseItem, string>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
            keys[entry.Item] = entry.Key;
        Page = new PlayerViewModel(this, new PlayerSession(
            "Review",
            [.. entries.Select(e => e.Item)],
            Services.Course.Units.SelectMany(u => u.Names).ToHashSet(),
            (item, correct) => Services.Progress.RecordReviewAnswer(keys[item], correct),
            ShowHome));
    }

    /// <summary>Gets the latest published release, installs it and restarts the app.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        try
        {
            var result = await AppUpdater.UpdateAsync(new Progress<string>(s => UpdateStatus = s), Services.ReleaseFiles);
            UpdateStatus = result.Message;
            if (result.Outcome != UpdateOutcome.Installed)
                return;

            UpdateStatus = result.Message + " Restarting…";
            await Task.Delay(800);
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception e)
        {
            UpdateStatus = $"Update failed: {e.Message}";
        }
    }
}
