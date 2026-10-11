using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.App.Services;
using Svenska.Core.Answers;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Generation;
using Svenska.Core.Saldo;
using Svenska.Core.Text;

namespace Svenska.App.ViewModels;

public sealed record TokenRow(string Text, IRelayCommand Add);

/// <summary>One choice of a listen-and-choose item. Keys 1, 2, 3 … pick it.</summary>
public partial class OptionRow(int number, string text, IRelayCommand choose) : ObservableObject
{
    [ObservableProperty] private bool _isRight;
    [ObservableProperty] private bool _isWrong;

    public string Label { get; } = $"{number}   {text}";
    public string Text { get; } = text;
    public IRelayCommand Choose { get; } = choose;
}

/// <summary>A line the learner hears before answering. The text shows after the answer.</summary>
public sealed record HeardRow(string Speaker, string Sv, string En, IRelayCommand Play, IRelayCommand PlaySlow);

/// <summary>One round for the player: the items, where the answers are recorded, and the way back.</summary>
/// <param name="Names">Proper names in the items; they keep their capital in word-order exercises.</param>
/// <param name="OnAnswer">Called once per item, with the learner's first answer.</param>
/// <param name="Heard">For a dialogue: what is heard before an item (null for none).</param>
/// <param name="Glosses">Meanings of unknown words in what is heard (word → meaning).</param>
public sealed record PlayerSession(string Title, IReadOnlyList<ExerciseItem> Items, IReadOnlyCollection<string> Names,
    Action<ExerciseItem, bool> OnAnswer, Action Back,
    Func<ExerciseItem, IReadOnlyList<DialogueLine>>? Heard = null,
    IReadOnlyDictionary<string, string>? Glosses = null);

/// <summary>
/// The exercise player: one item at a time. Enter checks the answer, Enter again goes on.
/// An item answered wrongly comes back once at the end. Items are typed (most types),
/// chosen (listen_choose) or clicked in the scene (scene_click).
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private const int SceneRenderWidth = 1600;

    private readonly MainWindowViewModel _main;
    private readonly PlayerSession _session;
    private readonly AnswerChecker _checker;
    private readonly Dictionary<string, Word> _packWords;
    private readonly List<ExerciseItem> _queue;
    private readonly HashSet<ExerciseItem> _repeated = [];
    private readonly int _total;
    private int _index;
    private int _rightFirstTime;
    private GrammarPoint? _grammar;
    private SvgScene? _scene;
    private string? _scenePath;
    private Word? _sceneWord;

    [ObservableProperty] private string _position = "";
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _translation = "";
    [ObservableProperty] private string _glossText = "";
    [ObservableProperty] private bool _hasAudio;
    [ObservableProperty] private bool _isTyped = true;
    [ObservableProperty] private bool _isScene;
    [ObservableProperty] private Bitmap? _sceneImage;
    [ObservableProperty] private IReadOnlyList<TokenRow> _tokens = [];
    [ObservableProperty] private IReadOnlyList<OptionRow> _options = [];
    [ObservableProperty] private IReadOnlyList<HeardRow> _heard = [];
    [ObservableProperty] private string _answer = "";
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isRight;
    [ObservableProperty] private string _feedback = "";
    [ObservableProperty] private IReadOnlyList<WordMark> _expectedWords = [];
    [ObservableProperty] private string _grammarTitle = "";
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _submitText = "Check";

    public string Title => _session.Title;

    /// <summary>The size of the scene drawing, for turning a click into drawing coordinates.</summary>
    public (double Width, double Height) SceneSize => _scene == null ? (0, 0) : (_scene.Width, _scene.Height);

    public PlayerViewModel(MainWindowViewModel main, PlayerSession session)
    {
        _main = main;
        _session = session;
        _checker = main.Services.CreateChecker();
        _packWords = Vocabulary.WordsByLemma(main.Services.Course);
        // An item whose scene is not drawn cannot be shown; the validator normally prevents it.
        _queue = [.. session.Items.Where(i => i.Type != ExerciseText.SceneClick || ScenePathOf(i) != null)];
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
        GlossText = "";
        SubmitText = "Check";
        Tokens = [];
        Options = [];
        Translation = item.En ?? "";
        HasAudio = ExerciseText.Heard.Contains(item.Type) && !string.IsNullOrEmpty(item.Sv) || item.Type == ExerciseText.SceneClick;
        IsScene = item.Type == ExerciseText.SceneClick;
        IsTyped = item.Type is not (ExerciseText.SceneClick or ExerciseText.ListenChoose);

        Heard = (_session.Heard?.Invoke(item) ?? []).Select(line => new HeardRow(line.Speaker, line.Sv, line.En,
            new RelayCommand(() => _main.Services.Play(line.Sv)),
            new RelayCommand(() => _main.Services.Play(line.Sv, slow: true)))).ToList();

        switch (item.Type)
        {
            case ExerciseText.Dictation:
                Instruction = "Listen and write the sentence.";
                Prompt = "";
                Translation = ""; // shown after checking, it would give the sentence away
                break;
            case ExerciseText.Numbers:
                Instruction = item.Hint == null ? "Listen and write the number." : $"Listen and write the number: {item.Hint}.";
                Prompt = "";
                Translation = "";
                break;
            case ExerciseText.ListenChoose:
                // In a dialogue the learner chooses what to say; elsewhere what was heard.
                Instruction = string.IsNullOrEmpty(item.Sv) ? "Choose what you say." : "Listen and choose (keys 1, 2, 3).";
                Prompt = string.IsNullOrEmpty(item.Sv) ? item.En ?? "" : "";
                Translation = "";
                var shuffled = item.Options.OrderBy(_ => Random.Shared.Next()).ToList();
                Options = shuffled.Select((text, i) => new OptionRow(i + 1, text, new RelayCommand(() => ChooseOption(text)))).ToList();
                break;
            case ExerciseText.SceneClick:
                Instruction = "Listen and click the thing in the picture.";
                Prompt = "";
                Translation = "";
                ShowScene(item);
                break;
            case ExerciseText.Cloze:
                Instruction = item.Hint == null ? "Write the missing word." : $"Write the missing word ({item.Hint}).";
                Prompt = item.Sv ?? "";
                break;
            case ExerciseText.Inflect:
                var word = _packWords.GetValueOrDefault(item.Lemma ?? "");
                Instruction = $"Write the {WordForms.Label(item.Form ?? "")} form.";
                Prompt = word is { Pos: "noun", Gender: not null } ? $"{word.Gender} {item.Lemma}" : item.Lemma ?? "";
                Translation = word == null ? "" : English.Meaning(word);
                break;
            case ExerciseText.Translate:
                Instruction = item.Hint == null ? "Write this in Swedish." : $"Write this in Swedish ({item.Hint}).";
                Prompt = item.En ?? "";
                Translation = "";
                break;
            case ExerciseText.WordOrder:
                Instruction = "Put the words in order. Type the sentence or click the words.";
                Prompt = item.En ?? "";
                Translation = "";
                Tokens = ExerciseText.ShuffledTokens(item.Sv ?? "", _session.Names)
                    .Select(t => new TokenRow(t, new RelayCommand(() => Answer = (Answer.TrimEnd() + " " + t).TrimStart())))
                    .ToList();
                break;
        }

        // Link to the card of the newest grammar point the item practises.
        _grammar = _main.Services.Course.Grammar.LastOrDefault(g => item.Grammar.Contains(g.Id));
        GrammarTitle = _grammar?.Title ?? "";

        // What is heard first: the other speakers of a dialogue, then the item's own audio.
        _main.Services.PlayAll([.. Heard.Select(h => h.Sv), .. HasAudio ? new[] { AudioText(item) } : []]);
    }

    private static string AudioText(ExerciseItem item) =>
        item.Type == ExerciseText.SceneClick ? item.Lemma ?? "" : item.Sv ?? "";

    // ---- answering ----

    /// <summary>Enter: check the typed answer, or go to the next item when it is already checked.</summary>
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
        if (!IsTyped || string.IsNullOrWhiteSpace(Answer))
            return;

        var item = Current;
        var result = item.Type == ExerciseText.Numbers
            ? AnswerChecker.CheckNumber(Answer, ExerciseText.Expected(item))
            : _checker.Check(Answer, ExerciseText.Expected(item), item.Accept);
        Finish(result.IsCorrect, result.IsCorrect ? "Right!" : result.Feedback, result.Words);
    }

    /// <summary>A choice was clicked, or its number key was pressed.</summary>
    public void ChooseOption(int number)
    {
        if (number >= 1 && number <= Options.Count)
            ChooseOption(Options[number - 1].Text);
    }

    private void ChooseOption(string text)
    {
        if (IsChecked || IsFinished)
            return;
        var expected = ExerciseText.Expected(Current);
        foreach (var option in Options)
        {
            option.IsRight = option.Text == expected;
            option.IsWrong = option.Text == text && text != expected;
        }
        Finish(text == expected, text == expected ? "Right!" : $"It was: {expected}", []);
    }

    /// <summary>A click in the scene, in the coordinates of the drawing.</summary>
    public void SceneClick(double x, double y)
    {
        if (IsChecked || IsFinished || _scene == null || _sceneWord?.Svg == null)
            return;
        var hit = _scene.HitTest(x, y);
        if (hit == null)
            return; // a click on the background is not an answer
        var right = hit == _sceneWord.Svg;
        SceneImage = Render(right ? [(hit, true)] : [(hit, false), (_sceneWord.Svg, true)]);
        var name = _sceneWord is { Gender: not null } ? $"{_sceneWord.Gender} {_sceneWord.Lemma}" : _sceneWord.Lemma;
        Finish(right, right ? $"Right! {name}" : $"It was: {name}", []);
    }

    private void Finish(bool correct, string feedback, IReadOnlyList<WordMark> words)
    {
        var item = Current;
        IsRight = correct;
        IsChecked = true;
        SubmitText = "Next";
        ExpectedWords = words;
        Feedback = feedback;

        // Now the learner may see what was heard.
        if (ExerciseText.Heard.Contains(item.Type) && !string.IsNullOrEmpty(item.Sv))
            Translation = item.Type == ExerciseText.Dictation ? item.En ?? "" : $"{item.Sv}\n{item.En}".Trim();
        else if (item.Type == ExerciseText.SceneClick && _sceneWord != null)
            Translation = English.Meaning(_sceneWord);
        GlossText = Glosses([.. Heard.Select(h => h.Sv), item.Sv ?? ""]);

        var firstTry = !_repeated.Contains(item);
        if (firstTry)
            _session.OnAnswer(item, correct);
        if (correct && firstTry)
            _rightFirstTime++;
        if (!correct && _repeated.Add(item))
        {
            _queue.Add(item);
            Position = PositionText();
        }
    }

    /// <summary>The glossed words that occur in what was heard: "bli: det blir … = that comes to …".</summary>
    private string Glosses(IEnumerable<string> texts)
    {
        if (_session.Glosses is not { Count: > 0 } glosses)
            return "";
        var saldo = _main.Services.Saldo;
        var found = new List<string>();
        foreach (var token in texts.SelectMany(Tokenizer.Tokens).Select(t => t.ToLowerInvariant()).Distinct())
        {
            var keys = new[] { token }.Concat(saldo?.Lookup(token).Select(f => f.Lemma) ?? []);
            foreach (var key in keys.Where(glosses.ContainsKey).Distinct())
                if (!found.Contains($"{key}: {glosses[key]}"))
                    found.Add($"{key}: {glosses[key]}");
        }
        return string.Join("\n", found);
    }

    // ---- scene ----

    /// <summary>The drawn scene of the pack that the item's word belongs to, if the file exists.</summary>
    private string? ScenePathOf(ExerciseItem item)
    {
        var services = _main.Services;
        if (services.ContentDir == null || item.Lemma == null || !_packWords.TryGetValue(item.Lemma, out var word) || word.Svg == null)
            return null;
        var scene = services.Course.Packs.FirstOrDefault(p => p.Words.Contains(word))?.Scene;
        if (scene == null)
            return null;
        var path = Path.Combine(services.ContentDir, scene);
        return File.Exists(path) ? path : null;
    }

    private void ShowScene(ExerciseItem item)
    {
        var path = ScenePathOf(item)!;
        if (path != _scenePath)
        {
            _scene?.Dispose();
            _scene = new SvgScene(path);
            _scenePath = path;
        }
        _sceneWord = _packWords[item.Lemma!];
        SceneImage = Render([]);
    }

    private Bitmap Render(IEnumerable<(string Id, bool Right)> frames)
    {
        var height = (int)(SceneRenderWidth * _scene!.Height / _scene.Width);
        return new Bitmap(new MemoryStream(_scene.RenderPng(SceneRenderWidth, height, frames)));
    }

    // ---- commands ----

    [RelayCommand]
    private void Play()
    {
        if (!IsFinished && HasAudio)
            _main.Services.Play(AudioText(Current));
    }

    [RelayCommand]
    private void PlaySlow()
    {
        if (!IsFinished && HasAudio)
            _main.Services.Play(AudioText(Current), slow: true);
    }

    [RelayCommand]
    private void ShowGrammar()
    {
        if (_grammar != null)
            _main.Reference.ShowCard(_grammar.Id);
    }

    [RelayCommand]
    private void Back()
    {
        _main.Services.StopAudio();
        _scene?.Dispose();
        _scene = null;
        _session.Back();
    }
}
