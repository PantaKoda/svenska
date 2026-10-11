using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Svenska.App.Services;
using Svenska.Core.Content;
using Svenska.Core.Reference;

namespace Svenska.App.ViewModels;

public partial class CardRow(GrammarPoint point) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public string Id => point.Id;
    public Level Level => point.Level;
    public string Title => $"{point.Level} · {point.Title}";
    public string Card => point.Card?.TrimEnd() ?? "This card is not written yet.";
}

public sealed record SuggestionRow(string Text, IRelayCommand Use);

/// <summary>
/// The reference panel beside the page: word lookup and grammar cards. It never takes
/// the place of the exercise; it opens next to it.
/// </summary>
public partial class ReferenceViewModel : ObservableObject
{
    public const string AllLevels = "All levels";

    private readonly AppServices _services;
    private WordLookup? _lookup;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private IReadOnlyList<WordEntry> _entries = [];
    [ObservableProperty] private IReadOnlyList<SuggestionRow> _suggestions = [];
    [ObservableProperty] private string _message = "Type a Swedish word in any form.";
    [ObservableProperty] private string _selectedLevel = AllLevels;
    [ObservableProperty] private IReadOnlyList<CardRow> _cards = [];

    private readonly List<CardRow> _allCards;

    /// <summary>The choices of the level filter: "A1", "A2" …, "All levels".</summary>
    public IReadOnlyList<string> Levels { get; }

    public ReferenceViewModel(AppServices services)
    {
        _services = services;
        _allCards = services.Course.Grammar.Select(g => new CardRow(g)).ToList();
        Levels = [.. services.Course.Levels.Keys.Order().Select(l => l.ToString()), AllLevels];
        SelectedLevel = Levels[0];
    }

    /// <summary>Shows the cards up to the level the learner is working at.</summary>
    public void SetLevel(Level level)
    {
        if (Levels.Contains(level.ToString()))
            SelectedLevel = level.ToString();
    }

    /// <summary>Opens the panel on one grammar card (the link from an exercise).</summary>
    public void ShowCard(string grammarId)
    {
        var row = _allCards.FirstOrDefault(c => c.Id == grammarId);
        if (row == null)
            return;
        if (!Cards.Contains(row))
            SelectedLevel = AllLevels;
        foreach (var card in _allCards)
            card.IsExpanded = card == row;
        SelectedTab = 1;
        IsOpen = true;
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    partial void OnSelectedLevelChanged(string value)
    {
        Cards = Enum.TryParse<Level>(value, out var level)
            ? _allCards.Where(c => c.Level <= level).ToList()
            : _allCards;
    }

    partial void OnQueryChanged(string value)
    {
        Entries = [];
        Suggestions = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            Message = "Type a Swedish word in any form.";
            return;
        }
        if (_services.Saldo is not { } saldo)
        {
            Message = "The word database (data/saldo.db) is not built.";
            return;
        }

        _lookup ??= new WordLookup(_services.Course, saldo);
        var result = _lookup.Lookup(value);
        Entries = result.Entries;
        Suggestions = result.Suggestions.Select(s => new SuggestionRow(s, new RelayCommand(() => Query = s))).ToList();
        Message = result.Entries.Count > 0 ? ""
            : result.Suggestions.Count > 0 ? "Not found. Did you mean:"
            : "Not found. Check the spelling; names and compound words may be missing.";
    }
}
