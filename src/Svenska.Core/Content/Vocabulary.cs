using Svenska.Core.Saldo;
using Svenska.Core.Text;

namespace Svenska.Core.Content;

/// <summary>Answers "which words and grammar does the learner know at this unit?".</summary>
public sealed class Vocabulary
{
    private readonly Course _course;
    private readonly ISaldoLexicon _saldo;
    private readonly Dictionary<string, Word> _words;
    private readonly ILookup<string, string> _lemmasOfOwnForms;

    public Vocabulary(Course course, ISaldoLexicon saldo)
    {
        _course = course;
        _saldo = saldo;
        _words = WordsByLemma(course);
        _lemmasOfOwnForms = _words.Values
            .Where(w => w.Forms != null)
            .SelectMany(w => w.Forms!.Values.Select(form => (Form: form.ToLowerInvariant(), w.Lemma)))
            .ToLookup(x => x.Form, x => x.Lemma);
    }

    /// <summary>Every pack word by lemma. If a lemma is in two packs (validator rule 6) the first wins.</summary>
    public static Dictionary<string, Word> WordsByLemma(Course course)
    {
        var words = new Dictionary<string, Word>();
        foreach (var word in course.Packs.SelectMany(p => p.Words))
            words.TryAdd(word.Lemma, word);
        return words;
    }

    public Word? Find(string lemma) => _words.GetValueOrDefault(lemma);

    /// <summary>Lemmas known once the unit is done: its own and those of all earlier units.</summary>
    public HashSet<string> KnownWords(Unit unit) =>
        UnitsUpTo(unit).SelectMany(u => u.NewWords.Concat(u.Assumes?.Words ?? [])).ToHashSet();

    public HashSet<string> KnownGrammar(Unit unit) =>
        UnitsUpTo(unit).SelectMany(u => u.NewGrammar.Concat(u.Assumes?.Grammar ?? [])).ToHashSet();

    /// <summary>
    /// The lemmas a written word can belong to: SALDO's readings, the packs' own forms, and
    /// the word itself (for pack words that SALDO does not have).
    /// </summary>
    public HashSet<string> LemmasOf(string token)
    {
        var lower = token.ToLowerInvariant();
        var lemmas = _saldo.Lookup(token).Select(f => f.Lemma).ToHashSet();
        lemmas.UnionWith(_lemmasOfOwnForms[lower]);
        if (_words.ContainsKey(lower))
            lemmas.Add(lower);
        if (_words.ContainsKey(token))
            lemmas.Add(token);
        return lemmas;
    }

    /// <summary>The phrases taught up to and including the unit.</summary>
    public List<Phrase> KnownPhrases(Unit unit) => UnitsUpTo(unit).SelectMany(u => u.Phrases).ToList();

    /// <summary>
    /// The words of a text that are neither known, a number, one of the given names, nor
    /// part of a known phrase that occurs in the text as a whole.
    /// </summary>
    public List<string> UnknownTokens(string text, HashSet<string> known, IReadOnlyCollection<string> names,
        IReadOnlyCollection<Phrase>? phrases = null)
    {
        var tokens = Tokenizer.Tokens(text);
        var covered = new bool[tokens.Count];
        foreach (var phrase in phrases ?? [])
        {
            var words = Tokenizer.Tokens(phrase.Sv);
            for (var i = 0; words.Count > 0 && i + words.Count <= tokens.Count; i++)
                if (words.Select((w, k) => string.Equals(w, tokens[i + k], StringComparison.OrdinalIgnoreCase)).All(same => same))
                    for (var k = 0; k < words.Count; k++)
                        covered[i + k] = true;
        }
        return tokens
            .Where((t, i) => !covered[i] && !Tokenizer.IsNumber(t) && !names.Contains(t) && !LemmasOf(t).Overlaps(known))
            .Distinct()
            .ToList();
    }

    /// <summary>True if the phrase occurs in the text as a whole (same words, any capitals).</summary>
    public static bool ContainsPhrase(string text, Phrase phrase)
    {
        var tokens = Tokenizer.Tokens(text);
        var words = Tokenizer.Tokens(phrase.Sv);
        for (var i = 0; words.Count > 0 && i + words.Count <= tokens.Count; i++)
            if (words.Select((w, k) => string.Equals(w, tokens[i + k], StringComparison.OrdinalIgnoreCase)).All(same => same))
                return true;
        return false;
    }

    private IEnumerable<Unit> UnitsUpTo(Unit unit) => _course.Units.TakeWhile(u => u != unit).Append(unit);
}
