using Svenska.Core.Content;
using Svenska.Core.Generation;
using Svenska.Core.Saldo;

namespace Svenska.Core.Reference;

/// <summary>One line of a word's form table: "definite" – "bilen".</summary>
public sealed record FormRow(string Label, string Forms);

/// <summary>One reading of a looked-up word.</summary>
/// <param name="Headword">The base form, for nouns with the article: "en bil".</param>
/// <param name="Gloss">The meaning, if the word is in a theme pack.</param>
/// <param name="TaughtIn">The unit that introduces the word, if any.</param>
public sealed record WordEntry(string Headword, string Lemma, string PartOfSpeech, string? Gloss, string? TaughtIn,
    IReadOnlyList<FormRow> Forms);

/// <param name="Suggestions">When nothing was found: the same word with å, ä or ö.</param>
public sealed record LookupResult(IReadOnlyList<WordEntry> Entries, IReadOnlyList<string> Suggestions);

/// <summary>
/// The word lookup of the reference panel: type any form and get the lemma, the meaning
/// (for pack words), en/ett and the forms.
/// </summary>
public sealed class WordLookup(Course course, ISaldoLexicon saldo)
{
    private const int MaxOtherForms = 12;

    private static readonly Dictionary<string, string> PosNames = new()
    {
        ["nn"] = "noun", ["vb"] = "verb", ["av"] = "adjective", ["ab"] = "adverb", ["pn"] = "pronoun",
        ["pp"] = "preposition", ["kn"] = "conjunction", ["sn"] = "subjunction", ["in"] = "interjection",
        ["nl"] = "numeral", ["al"] = "article", ["pm"] = "name",
    };

    // The forms worth a line of their own, in display order. Verbs fall back to the s-form
    // so that a verb like "finnas" still shows its tenses.
    private static readonly Dictionary<string, (string Label, string[] Msd)[]> MainForms = new()
    {
        ["nn"] =
        [
            ("indefinite", ["sg indef nom"]), ("definite", ["sg def nom"]),
            ("plural", ["pl indef nom"]), ("definite plural", ["pl def nom"]),
        ],
        ["vb"] =
        [
            ("infinitive", ["inf aktiv", "inf s-form"]), ("present", ["pres ind aktiv", "pres ind s-form"]),
            ("past", ["pret ind aktiv", "pret ind s-form"]), ("supine (har …)", ["sup aktiv", "sup s-form"]),
            ("imperative", ["imper"]),
        ],
        ["av"] =
        [
            ("with en-words", ["pos indef sg u nom"]), ("with ett-words", ["pos indef sg n nom"]),
            ("plural and definite", ["pos indef pl nom"]),
            ("comparative", ["komp nom"]), ("superlative", ["super indef nom"]),
        ],
    };

    private readonly Dictionary<string, Word> _packWords = Vocabulary.WordsByLemma(course);

    public LookupResult Lookup(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return new([], []);

        var entries = Entries(text);
        entries.AddRange(PhraseEntries(text));
        if (entries.Count > 0)
            return new(entries, []);

        var suggestions = DiacriticVariants(text.ToLowerInvariant())
            .Where(v => saldo.Lookup(v).Count > 0)
            .Take(5).ToList();
        return new([], suggestions);
    }

    private List<WordEntry> Entries(string text)
    {
        var entries = new List<(int Rank, WordEntry Entry)>();
        foreach (var reading in saldo.Lookup(text).GroupBy(f => f.LemmaId).Select(g => g.First()))
        {
            var word = PackWord(reading);
            var basePos = reading.Pos.Length >= 2 ? reading.Pos[..2] : reading.Pos;
            var pos = PosNames.GetValueOrDefault(basePos, reading.Pos)
                      + (reading.Pos.EndsWith('a') && reading.Pos.Length == 3 ? " (abbreviation)" : "");
            var headword = basePos == "nn" && reading.Gender != null ? $"{reading.Gender} {reading.Lemma}" : reading.Lemma;
            // Pack words first; among them the reading whose base form is what was typed.
            var rank = (word != null ? 0 : 100) + (reading.Lemma == text.ToLowerInvariant() ? 0 : 20)
                       + Math.Max(0, Array.IndexOf([.. PosNames.Keys], basePos))
                       + (reading.Pos.Length > 2 ? 50 : 0);
            entries.Add((rank, new WordEntry(headword, reading.Lemma, pos, word == null ? null : English.Meaning(word),
                TaughtIn(reading.Lemma, word), FormRows(basePos, saldo.FormsOfLemma(reading.LemmaId)))));
        }

        // A pack word that SALDO does not have (its forms are written in the pack).
        if (entries.Count == 0)
        {
            var lower = text.ToLowerInvariant();
            foreach (var word in _packWords.Values.Where(w =>
                         w.Lemma == lower || (w.Forms?.Values.Contains(lower) ?? false)))
            {
                var headword = word is { Pos: "noun", Gender: not null } ? $"{word.Gender} {word.Lemma}" : word.Lemma;
                var rows = (word.Forms ?? []).Select(f => new FormRow(WordForms.Label(f.Key), f.Value)).ToList();
                entries.Add((0, new WordEntry(headword, word.Lemma, word.Pos, English.Meaning(word), TaughtIn(word.Lemma, word), rows)));
            }
        }
        // SALDO sometimes has the same word twice (two senses with the same forms).
        return entries.OrderBy(e => e.Rank).Select(e => e.Entry)
            .DistinctBy(e => (e.Headword, e.PartOfSpeech)).ToList();
    }

    /// <summary>The taught phrases that contain the typed word, or start with what was typed.</summary>
    private IEnumerable<WordEntry> PhraseEntries(string text)
    {
        var typed = text.ToLowerInvariant();
        foreach (var unit in course.Units)
            foreach (var phrase in unit.Phrases)
            {
                var words = Text.Tokenizer.Tokens(phrase.Sv).Select(w => w.ToLowerInvariant());
                if (words.Contains(typed) || phrase.Sv.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    yield return new WordEntry(phrase.Sv, phrase.Sv, "phrase", phrase.En,
                        $"{unit.Id.ToUpperInvariant()} {unit.Title}", []);
            }
    }

    /// <summary>The pack word behind a SALDO reading: same lemma, part of speech and gender.</summary>
    private Word? PackWord(SaldoForm reading)
    {
        if (!_packWords.TryGetValue(reading.Lemma, out var word) || WordForms.SaldoCode(word.Pos) != reading.Pos)
            return null;
        var sameGender = word.Gender == null || reading.Gender == null || reading.Gender.Split('/').Contains(word.Gender);
        return sameGender ? word : null;
    }

    private string? TaughtIn(string lemma, Word? word)
    {
        if (word == null)
            return null;
        var unit = course.Units.FirstOrDefault(u => u.NewWords.Contains(lemma));
        return unit == null ? null : $"{unit.Id.ToUpperInvariant()} {unit.Title}";
    }

    private static List<FormRow> FormRows(string pos, IReadOnlyList<SaldoForm> forms)
    {
        var rows = new List<FormRow>();
        var shown = new HashSet<string>();

        foreach (var (label, msds) in MainForms.GetValueOrDefault(pos, []))
        {
            var found = msds.Select(msd => forms.Where(f => f.Msd == msd).Select(f => f.Form).Distinct().ToList())
                .FirstOrDefault(list => list.Count > 0);
            if (found == null)
                continue;
            rows.Add(new FormRow(label, string.Join(" / ", found)));
            shown.UnionWith(found);
        }

        if (pos == "nn")
        {
            var genitive = forms.Where(f => f.Msd.EndsWith(" gen")).Select(f => f.Form).Distinct().ToList();
            if (genitive.Count > 0)
                rows.Add(new FormRow("genitive (-s)", string.Join(", ", genitive)));
            shown.UnionWith(genitive);
        }

        var other = forms.Select(f => f.Form).Distinct().Where(f => !shown.Contains(f)).ToList();
        if (other.Count > 0)
            rows.Add(new FormRow(rows.Count == 0 ? "forms" : "other forms",
                string.Join(", ", other.Take(MaxOtherForms)) + (other.Count > MaxOtherForms ? " …" : "")));
        return rows;
    }

    /// <summary>"mjolk" → "mjölk", "mjålk", "mjälk": every way to read a and o as å, ä, ö.</summary>
    private static IEnumerable<string> DiacriticVariants(string text)
    {
        var positions = Enumerable.Range(0, text.Length).Where(i => text[i] is 'a' or 'o').Take(5).ToList();
        var variants = new List<string> { text };
        foreach (var i in positions)
        {
            var replacements = text[i] == 'a' ? "åä" : "ö";
            variants.AddRange(variants.ToList().SelectMany(v => replacements.Select(r => v[..i] + r + v[(i + 1)..])));
        }
        return variants.Skip(1);
    }
}
