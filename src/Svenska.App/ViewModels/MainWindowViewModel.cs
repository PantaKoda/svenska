using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.App.Services;
using Svenska.Core.Content;

namespace Svenska.App.ViewModels;

/// <summary>The window: one page at a time (home, unit, exercises) and the footer with Update.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private string _updateStatus = "";

    public AppServices Services { get; }

    public ReferenceViewModel Reference { get; }

    public string About => $"Svenska {AppUpdater.CurrentVersion} · Data: SALDO, Språkbanken (CC-BY 4.0)";

    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        Reference = new ReferenceViewModel(services);
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
        () => ShowUnit(unit)));

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
