using Svenska.Core.Content;

namespace Svenska.Core.Saldo;

/// <summary>
/// The form names used in content (indef, def, pl, pres …), what SALDO calls them, the
/// grammar point each one needs, and how to say them to the learner.
/// </summary>
public static class WordForms
{
    private sealed record FormDef(string Pos, string Label, string? Grammar, string[] Msd);

    /// <summary>Noun with its article: "en påse", "ett kvitto".</summary>
    public const string WithArticle = "a";

    // For verbs the s-form is the fallback, so that "finnas" has a present tense (finns).
    private static readonly Dictionary<string, FormDef> Forms = new()
    {
        ["indef"] = new("noun", "indefinite", null, ["sg indef nom"]),
        [WithArticle] = new("noun", "indefinite with en/ett", "en_ett", ["sg indef nom"]),
        ["def"] = new("noun", "definite", "def_singular", ["sg def nom"]),
        ["pl"] = new("noun", "plural", "plural_indef", ["pl indef nom"]),
        ["pl_def"] = new("noun", "definite plural", "plural_def", ["pl def nom"]),
        ["inf"] = new("verb", "infinitive", null, ["inf aktiv", "inf s-form"]),
        ["pres"] = new("verb", "present", "present_tense", ["pres ind aktiv", "pres ind s-form"]),
        ["past"] = new("verb", "past", "past_tense", ["pret ind aktiv", "pret ind s-form"]),
        ["sup"] = new("verb", "supine", "perfect_tense", ["sup aktiv", "sup s-form"]),
        ["imp"] = new("verb", "imperative", "imperative", ["imper"]),
    };

    /// <summary>The grammar point an adjective needs to agree with a noun ({state~1}).</summary>
    public const string AgreementGrammar = "adjective_agreement_basic";

    private static readonly Dictionary<string, string> SaldoPos = new()
    {
        ["noun"] = "nn", ["verb"] = "vb", ["adj"] = "av", ["adv"] = "ab", ["prep"] = "pp", ["pron"] = "pn",
        ["conj"] = "kn", ["subj"] = "sn", ["interj"] = "in", ["num"] = "nl", ["article"] = "al",
    };

    /// <summary>The values allowed for `pos` in a theme pack.</summary>
    public static IEnumerable<string> PartsOfSpeech => SaldoPos.Keys;

    public static IEnumerable<string> Names => Forms.Keys;

    public static bool IsValid(string form, string pos) => Forms.TryGetValue(form, out var def) && def.Pos == pos;

    /// <summary>"def" → "noun". Null for an unknown form name.</summary>
    public static string? PosOf(string form) => Forms.GetValueOrDefault(form)?.Pos;

    /// <summary>"def" → "def_singular". Null if the form needs no grammar point (indef, inf).</summary>
    public static string? GrammarOf(string form) => Forms.GetValueOrDefault(form)?.Grammar;

    /// <summary>"def" → "definite". Unknown names are returned as they are.</summary>
    public static string Label(string form) => Forms.TryGetValue(form, out var def) ? def.Label : form;

    /// <summary>"sg def nom" → "definite". Null for forms the content has no name for.</summary>
    public static string? LabelOfMsd(string msd) =>
        Forms.Values.FirstOrDefault(f => f.Msd.Contains(msd))?.Label;

    /// <summary>
    /// The requested form of a pack word: from the pack's own `forms:` if given, otherwise
    /// from SALDO. Empty if unknown; more than one entry if SALDO has variants.
    /// </summary>
    public static IReadOnlyList<string> Inflect(Word word, string form, ISaldoLexicon saldo)
    {
        if (word.Forms != null && word.Forms.TryGetValue(form, out var own))
            return [own];
        if (!IsValid(form, word.Pos))
            return [];
        if (form == WithArticle)
            return word.Gender == null ? [] : [.. Inflect(word, "indef", saldo).Select(f => $"{word.Gender} {f}")];

        var all = FormsOf(word, saldo);
        foreach (var msd in Forms[form].Msd)
        {
            var found = all.Where(f => f.Msd == msd).Select(f => f.Form).Distinct().ToList();
            if (found.Count > 0)
                return found;
        }
        return [];
    }

    /// <summary>
    /// The form of an adjective after "är" for a noun: trasig (en-word), trasigt (ett-word),
    /// trasiga (plural).
    /// </summary>
    public static IReadOnlyList<string> Agree(Word adjective, Word noun, string nounForm, ISaldoLexicon saldo)
    {
        var key = nounForm is "pl" or "pl_def" ? "pl" : noun.Gender == "ett" ? "n" : "u";
        if (adjective.Forms != null && adjective.Forms.TryGetValue(key, out var own))
            return [own];
        var msd = key == "pl" ? "pos indef pl nom" : $"pos indef sg {key} nom";
        return FormsOf(adjective, saldo).Where(f => f.Msd == msd).Select(f => f.Form).Distinct().ToList();
    }

    private static List<SaldoForm> FormsOf(Word word, ISaldoLexicon saldo) =>
        saldo.FormsOf(word.Lemma, SaldoPos.GetValueOrDefault(word.Pos))
            .Where(f => word.Gender == null || f.Gender == null || f.Gender.Split('/').Contains(word.Gender))
            .ToList();
}
