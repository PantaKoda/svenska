using CommunityToolkit.Mvvm.Input;
using Svenska.Core.Content;

namespace Svenska.App.ViewModels;

public sealed record UnitRow(string Number, string Title, string Task, IRelayCommand Open);

/// <summary>Home: the units, in course order.</summary>
public sealed class HomeViewModel
{
    public IReadOnlyList<UnitRow> Units { get; }
    public string Status { get; }

    public HomeViewModel(MainWindowViewModel main)
    {
        var services = main.Services;
        Units = services.Course.Units
            .Select(unit => new UnitRow(unit.Id.ToUpperInvariant(), unit.Title, unit.Task,
                new RelayCommand(() => main.ShowUnit(unit))))
            .ToList();

        Status = services.ContentDir == null ? "No content folder found."
            : services.ContentErrors > 0 ? $"The content has {services.ContentErrors} errors. Run Svenska.Validator."
            : Units.Count == 0 ? "There are no units yet."
            : "";
    }
}
