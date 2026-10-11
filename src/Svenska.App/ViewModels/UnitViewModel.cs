using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Generation;

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
    /// <summary>The everyday phrases of the unit (the same row type as the words: Swedish, meaning, play).</summary>
    public IReadOnlyList<WordRow> Phrases { get; }
    public bool HasPhrases => Phrases.Count > 0;
    public string StartText { get; }
    public bool HasExercises { get; }
    public string ProgressText { get; }
    public bool HasFinalTask => _unit.FinalTask != null;
    public string FinalTaskText { get; }
    /// <summary>The unit's glossed words, shown with the transcript: "bli: …".</summary>
    public string GlossText => string.Join("\n", _unit.Glosses.Select(g => $"{g.Key}: {g.Value}"));

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
            return new WordRow(swedish, word == null ? "" : English.Meaning(word), new RelayCommand(() => services.Play(lemma)));
        }).ToList();

        Phrases = unit.Phrases.Select(p => new WordRow(p.Sv, p.En, new RelayCommand(() => services.Play(p.Sv)))).ToList();

        Dialogue = unit.ModelDialogue.Select(line => new LineRow(line.Speaker, line.Sv, line.En,
            new RelayCommand(() => services.Play(line.Sv)),
            new RelayCommand(() => services.Play(line.Sv, slow: true)))).ToList();

        var (done, pool) = services.Progress.UnitProgress(unit);
        HasExercises = pool > 0;
        StartText = pool > ExerciseSession.DefaultSize
            ? $"Start exercises ({ExerciseSession.DefaultSize} of {pool})"
            : $"Start exercises ({pool})";
        ProgressText = done == 0 ? "" : $"{done} of {pool} done";
        FinalTaskText = services.Progress.FinalTaskDone(unit) ? "Done. You can do it again." : unit.Task;
    }

    [RelayCommand]
    private void Back() => _main.ShowHome();

    [RelayCommand]
    private void Start() => _main.ShowExercises(_unit);

    [RelayCommand]
    private void StartFinalTask() => _main.ShowFinalTask(_unit);
}
