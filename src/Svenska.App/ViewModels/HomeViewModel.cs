using CommunityToolkit.Mvvm.Input;

namespace Svenska.App.ViewModels;

/// <param name="Percent">0–100: the share of the unit's items answered right at least once.</param>
/// <param name="IsNext">The first unit that is not finished: the one place the accent goes.</param>
public sealed record UnitRow(string Number, string Title, string Task, string ProgressText, double Percent, bool IsNext, IRelayCommand Open);

public sealed record LevelGroup(string Level, IReadOnlyList<UnitRow> Units);

/// <summary>The companion chapters of one course book: extra practice, apart from the task units.</summary>
public sealed record BookGroup(string Book, IReadOnlyList<UnitRow> Chapters);

public sealed record ThemeRow(string Title, string Detail, IRelayCommand Open);

/// <summary>Home: the units by level with their progress, the review queue and the themes.</summary>
public sealed class HomeViewModel
{
    public IReadOnlyList<LevelGroup> Levels { get; }
    public IReadOnlyList<BookGroup> Books { get; }
    public IReadOnlyList<ThemeRow> Themes { get; }
    public string Status { get; }
    public string ReviewText { get; }
    public IRelayCommand Review { get; }

    public HomeViewModel(MainWindowViewModel main)
    {
        var services = main.Services;
        var progress = services.Progress;

        var taskUnits = services.Course.Units.Where(u => !u.IsCompanion).ToList();
        var next = taskUnits.FirstOrDefault(u =>
        {
            var (done, total) = progress.UnitProgress(u);
            return done < total || (u.FinalTask != null && !progress.FinalTaskDone(u));
        });
        Levels = taskUnits.GroupBy(u => u.Level).OrderBy(g => g.Key)
            .Select(group => new LevelGroup(group.Key.ToString(), group.Select(unit =>
            {
                var (done, total) = progress.UnitProgress(unit);
                return new UnitRow(unit.Id.ToUpperInvariant(), unit.Title, unit.Task,
                    done == 0 ? "Not started" : $"{done} of {total} exercises done",
                    total == 0 ? 0 : 100.0 * done / total,
                    unit == next,
                    new RelayCommand(() => main.ShowUnit(unit)));
            }).ToList()))
            .ToList();

        // Never "next": the companion is extra practice in any order.
        Books = services.Course.Units.Where(u => u.IsCompanion).GroupBy(u => u.Book!)
            .Select(group => new BookGroup(group.Key, group.Select(unit =>
            {
                var (done, total) = progress.UnitProgress(unit);
                return new UnitRow($"CHAPTER {unit.Chapter}", unit.Title, string.Join(" · ", unit.Focus),
                    done == 0 ? "Not started" : $"{done} of {total} exercises done",
                    total == 0 ? 0 : 100.0 * done / total,
                    false,
                    new RelayCommand(() => main.ShowUnit(unit)));
            }).ToList()))
            .ToList();

        var due = progress.DueCount();
        ReviewText = due == 0 ? "Review (nothing due)" : $"Review ({due} due)";
        Review = new RelayCommand(main.ShowReview, () => due > 0);

        // Only the themes that have practice drills (generated/theme-<pack>.yaml).
        Themes = services.Course.Packs
            .Where(pack => progress.ThemePool(pack, includeNewWords: true).Count > 0)
            .Select(pack =>
            {
                var known = progress.ThemePool(pack, includeNewWords: false).Count;
                return new ThemeRow(pack.Title, known == 0 ? "No known words yet" : $"{known} exercises with known words",
                    new RelayCommand(() => main.ShowTheme(pack)));
            }).ToList();

        Status = services.ContentDir == null ? "No content folder found."
            : services.ContentErrors > 0 ? $"The content has {services.ContentErrors} errors. Run Svenska.Validator."
            : services.Course.Units.Count == 0 ? "There are no units yet."
            : "";
    }
}
