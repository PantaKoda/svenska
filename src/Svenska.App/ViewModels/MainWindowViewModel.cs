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

    public string About => $"Svenska {AppUpdater.CurrentVersion} · Data: SALDO, Språkbanken (CC-BY 4.0)";

    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        ShowHome();
    }

    public void ShowHome() => Page = new HomeViewModel(this);

    public void ShowUnit(Unit unit) => Page = new UnitViewModel(this, unit);

    public void ShowExercises(Unit unit) => Page = new PlayerViewModel(this, unit);

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
