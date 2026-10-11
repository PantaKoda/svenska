using Svenska.Core.Content;

namespace Svenska.Core.Saldo;

/// <summary>
/// The form names used in content (indef, def, pl, pres …), what SALDO calls them, and
/// how to say them to the learner.
/// </summary>
public static class WordForms
{
    private sealed record FormDef(string Pos, string Label, string[] Msd);

    // For verbs the s-form is the fallback, so that "finnas" has a present tense (finns).
    private static readonly Dictionary<string, FormDef> Forms = new()
    {
        ["indef"] = new("noun", "indefinite", ["sg indef nom"]),
        ["def"] = new("noun", "definite", ["sg def nom"]),
        ["pl"] = new("noun", "plural", ["pl indef nom"]),
        ["pl_def"] = new("noun", "definite plural", ["pl def nom"]),
        ["inf"] = new("verb", "infinitive", ["inf aktiv", "inf s-form"]),
        ["pres"] = new("verb", "present", ["pres ind aktiv", "pres ind s-form"]),
        ["past"] = new("verb", "past", ["pret ind aktiv", "pret ind s-form"]),
        ["sup"] = new("verb", "supine", ["sup aktiv", "sup s-form"]),
        ["imp"] = new("verb", "imperative", ["imper"]),
    };

    private static readonly Dictionary<string, string> SaldoPos = new()
    {
        ["noun"] = "nn", ["verb"] = "vb", ["adj"] = "av", ["adv"] = "ab", ["prep"] = "pp", ["pron"] = "pn",
        ["conj"] = "kn", ["subj"] = "sn", ["interj"] = "in", ["num"] = "nl", ["article"] = "al",
    };

    /// <summary>The values allowed for `pos` in a theme pack.</summary>
    public static IEnumerable<string> PartsOfSpeech => SaldoPos.Keys;

    public static bool IsValid(string form, string pos) => Forms.TryGetValue(form, out var def) && def.Pos == pos;

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

        var all = saldo.FormsOf(word.Lemma, SaldoPos[word.Pos])
            .Where(f => word.Gender == null || f.Gender == null || f.Gender.Split('/').Contains(word.Gender))
            .ToList();
        foreach (var msd in Forms[form].Msd)
        {
            var found = all.Where(f => f.Msd == msd).Select(f => f.Form).Distinct().ToList();
            if (found.Count > 0)
                return found;
        }
        return [];
    }
}
