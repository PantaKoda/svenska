using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Generation;

namespace Svenska.App.ViewModels;

public sealed record ThemeWordRow(string Swedish, string Gloss, bool Known);

/// <summary>
/// Theme practice: the core drills of one theme pack, outside the unit order. By default
/// only drills made of words the learner knows; a switch adds the pack's other words.
/// </summary>
public partial class ThemeViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly ThemePack _pack;

    [ObservableProperty] private bool _includeNewWords;
    [ObservableProperty] private string _startText = "";
    [ObservableProperty] private bool _hasExercises;
    [ObservableProperty] private string _note = "";

    public string Title => _pack.Title;
    public IReadOnlyList<ThemeWordRow> Words { get; }

    public ThemeViewModel(MainWindowViewModel main, ThemePack pack)
    {
        _main = main;
        _pack = pack;
        var progress = main.Services.Progress;
        var known = progress.KnownWords();
        var level = progress.CurrentLevel();

        Words = pack.Words.Where(w => w.Level <= level)
            .Select(w => new ThemeWordRow(
                w is { Pos: "noun", Gender: not null } ? $"{w.Gender} {w.Lemma}" : w.Lemma,
                English.Meaning(w), known.Contains(w.Lemma)))
            .ToList();
        Refresh();
    }

    partial void OnIncludeNewWordsChanged(bool value) => Refresh();

    private void Refresh()
    {
        var pool = _main.Services.Progress.ThemePool(_pack, IncludeNewWords).Count;
        HasExercises = pool > 0;
        StartText = pool > ExerciseSession.DefaultSize
            ? $"Start ({ExerciseSession.DefaultSize} of {pool})"
            : $"Start ({pool})";
        Note = pool > 0 ? ""
            : "You have not learned words of this theme in a unit yet. Tick the box to practise them anyway.";
    }

    [RelayCommand]
    private void Back() => _main.ShowHome();

    [RelayCommand]
    private void Start() => _main.ShowThemeExercises(_pack, IncludeNewWords);
}
