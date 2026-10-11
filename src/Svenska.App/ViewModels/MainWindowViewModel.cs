using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.App.Services;
using Svenska.Core.Content;

namespace Svenska.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _updateStatus = "";

    public string About => $"Svenska {AppUpdater.CurrentVersion} · Data: SALDO, Språkbanken (CC-BY 4.0)";

    public static MainWindowViewModel Load()
    {
        var contentDir = ContentLoader.FindContentDir(AppContext.BaseDirectory);
        if (contentDir == null)
            return new MainWindowViewModel { Status = "No content folder found." };

        var (course, issues) = ContentLoader.Load(contentDir);
        var errors = issues.Count(i => i.Severity == Severity.Error);
        return new MainWindowViewModel
        {
            Status = errors > 0
                ? $"The content has {errors} errors. Run Svenska.Validator."
                : $"{course.Units.Count} units, {course.Packs.Count} theme packs, {course.Grammar.Count} grammar points loaded.",
        };
    }

    /// <summary>Gets the latest published release, installs it and restarts the app.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        try
        {
            var result = await AppUpdater.UpdateAsync(new Progress<string>(s => UpdateStatus = s));
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
