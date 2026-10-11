using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.Core.Content;
using Svenska.Core.Exercises;

namespace Svenska.App.ViewModels;

public sealed record WordRow(string Swedish, string Gloss, IRelayCommand Play);

public sealed record LineRow(string Speaker, string Sv, string En, IRelayCommand Play, IRelayCommand PlaySlow);

/// <summary>A unit: the task, its words, the model dialogue to listen to, and the way into the exercises.</summary>
public partial class UnitViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly Unit _unit;

    [ObservableProperty]
    private bool _showTranscript;

    public string Title => _unit.Title;
    public string Task => _unit.Task;
    public string CanDo => _unit.CanDo;
    public string? SpeakingPrompt => _unit.SpeakingPrompt;
    public IReadOnlyList<WordRow> Words { get; }
    public IReadOnlyList<LineRow> Dialogue { get; }
    public string StartText { get; }
    public bool HasExercises { get; }

    public UnitViewModel(MainWindowViewModel main, Unit unit)
    {
        _main = main;
        _unit = unit;
        var services = main.Services;
        var packWords = Vocabulary.WordsByLemma(services.Course);

        Words = unit.NewWords.Select(lemma =>
        {
            var word = packWords.GetValueOrDefault(lemma);
            // Nouns are learnt with their article: "en ost", "ett bröd".
            var swedish = word is { Pos: "noun", Gender: not null } ? $"{word.Gender} {lemma}" : lemma;
            return new WordRow(swedish, word?.Gloss ?? "", new RelayCommand(() => services.Play(lemma)));
        }).ToList();

        Dialogue = unit.ModelDialogue.Select(line => new LineRow(line.Speaker, line.Sv, line.En,
            new RelayCommand(() => services.Play(line.Sv)),
            new RelayCommand(() => services.Play(line.Sv, slow: true)))).ToList();

        var count = unit.Exercises.Count(i => ExerciseText.Playable.Contains(i.Type));
        HasExercises = count > 0;
        StartText = $"Start exercises ({count})";
    }

    [RelayCommand]
    private void Back() => _main.ShowHome();

    [RelayCommand]
    private void Start() => _main.ShowExercises(_unit);
}
