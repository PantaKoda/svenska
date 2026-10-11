using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.Core.Answers;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Saldo;

namespace Svenska.App.ViewModels;

public sealed record TokenRow(string Text, IRelayCommand Add);

/// <summary>
/// The exercise player: one item at a time. Enter checks the answer, Enter again goes on.
/// An item answered wrongly comes back once at the end.
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly Unit _unit;
    private readonly AnswerChecker _checker;
    private readonly Dictionary<string, Word> _packWords;
    private readonly List<ExerciseItem> _queue;
    private readonly HashSet<ExerciseItem> _repeated = [];
    private readonly int _total;
    private int _index;
    private int _rightFirstTime;

    [ObservableProperty] private string _position = "";
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _translation = "";
    [ObservableProperty] private bool _isDictation;
    [ObservableProperty] private IReadOnlyList<TokenRow> _tokens = [];
    [ObservableProperty] private string _answer = "";
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isRight;
    [ObservableProperty] private string _feedback = "";
    [ObservableProperty] private IReadOnlyList<WordMark> _expectedWords = [];
    [ObservableProperty] private string _grammarTitle = "";
    [ObservableProperty] private string _grammarCard = "";
    [ObservableProperty] private bool _showCard;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _submitText = "Check";

    public string UnitTitle => _unit.Title;

    public PlayerViewModel(MainWindowViewModel main, Unit unit)
    {
        _main = main;
        _unit = unit;
        _checker = main.Services.CreateChecker();
        _packWords = Vocabulary.WordsByLemma(main.Services.Course);
        _queue = unit.Exercises.Where(i => ExerciseText.Playable.Contains(i.Type)).ToList();
        _total = _queue.Count;
        Show();
    }

    private ExerciseItem Current => _queue[_index];

    /// <summary>"3 / 18", then "Repeat 1 / 4" for the items that come back.</summary>
    private string PositionText() => _index < _total
        ? $"{_index + 1} / {_total}"
        : $"Repeat {_index + 1 - _total} / {_queue.Count - _total}";

    private void Show()
    {
        if (_index >= _queue.Count)
        {
            IsFinished = true;
            Summary = $"{_rightFirstTime} of {_total} right at the first try.";
            return;
        }

        var item = Current;
        Position = PositionText();
        Answer = "";
        IsChecked = false;
        Feedback = "";
        ExpectedWords = [];
        ShowCard = false;
        SubmitText = "Check";
        IsDictation = item.Type == ExerciseText.Dictation;
        Tokens = [];
        Translation = item.En ?? "";

        switch (item.Type)
        {
            case ExerciseText.Dictation:
                Instruction = "Listen and write the sentence.";
                Prompt = "";
                Translation = ""; // shown after checking, it would give the sentence away
                Play();
                break;
            case ExerciseText.Cloze:
                Instruction = item.Hint == null ? "Write the missing word." : $"Write the missing word ({item.Hint}).";
                Prompt = item.Sv ?? "";
                break;
            case ExerciseText.Inflect:
                var word = _packWords.GetValueOrDefault(item.Lemma ?? "");
                Instruction = $"Write the {WordForms.Label(item.Form ?? "")} form.";
                Prompt = word is { Pos: "noun", Gender: not null } ? $"{word.Gender} {item.Lemma}" : item.Lemma ?? "";
                Translation = word?.Gloss ?? "";
                break;
            case ExerciseText.WordOrder:
                Instruction = "Put the words in order. Type the sentence or click the words.";
                Prompt = item.En ?? "";
                Translation = "";
                Tokens = ExerciseText.ShuffledTokens(item.Sv ?? "", _unit.Names)
                    .Select(t => new TokenRow(t, new RelayCommand(() => Answer = (Answer.TrimEnd() + " " + t).TrimStart())))
                    .ToList();
                break;
        }

        var grammar = _main.Services.Course.Grammar.FirstOrDefault(g => item.Grammar.Contains(g.Id) && g.Card != null)
                      ?? _main.Services.Course.Grammar.FirstOrDefault(g => item.Grammar.Contains(g.Id));
        GrammarTitle = grammar?.Title ?? "";
        GrammarCard = grammar?.Card?.TrimEnd() ?? "The card for this point is not written yet.";
    }

    /// <summary>Enter: check the answer, or go to the next item when it is already checked.</summary>
    [RelayCommand]
    private void Submit()
    {
        if (IsFinished)
            return;
        if (IsChecked)
        {
            _index++;
            Show();
            return;
        }
        if (string.IsNullOrWhiteSpace(Answer))
            return;

        var item = Current;
        var result = _checker.Check(Answer, ExerciseText.Expected(item), item.Accept);
        IsRight = result.IsCorrect;
        IsChecked = true;
        SubmitText = "Next";
        ExpectedWords = result.Words;
        Feedback = result.IsCorrect ? "Right!" : result.Feedback;
        if (IsDictation)
            Translation = item.En ?? "";

        var firstTry = !_repeated.Contains(item);
        if (result.IsCorrect && firstTry)
            _rightFirstTime++;
        if (!result.IsCorrect && _repeated.Add(item))
        {
            _queue.Add(item);
            Position = PositionText();
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (!IsFinished && IsDictation)
            _main.Services.Play(Current.Sv ?? "");
    }

    [RelayCommand]
    private void PlaySlow()
    {
        if (!IsFinished && IsDictation)
            _main.Services.Play(Current.Sv ?? "", slow: true);
    }

    [RelayCommand]
    private void ToggleCard() => ShowCard = !ShowCard;

    [RelayCommand]
    private void Back() => _main.ShowUnit(_unit);
}
