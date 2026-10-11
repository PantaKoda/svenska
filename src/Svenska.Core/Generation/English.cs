using System.Text.RegularExpressions;
using Svenska.Core.Content;

namespace Svenska.Core.Generation;

/// <summary>
/// Turns a pack word's gloss into the English word for a frame's `en` line.
/// A gloss is "meaning; key: value; …": "to buy; pp: bought", "loaf; pl: loaves".
/// Regular plurals and past forms are built; irregular ones come from the gloss.
/// </summary>
public static partial class English
{
    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parenthesis();

    /// <summary>The gloss as shown to the learner: "to buy; pp: bought" → "to buy".</summary>
    public static string Meaning(Word word) => word.Gloss.Split(';')[0].Trim();

    /// <summary>"to buy; pp: bought" → "buy"; "krona (Swedish money)" → "krona"; "at, by" → "at".</summary>
    public static string Base(Word word)
    {
        var text = Parenthesis().Replace(word.Gloss.Split(';')[0], "").Split(',')[0].Trim();
        return word.Pos == "verb" && text.StartsWith("to ") ? text[3..] : text;
    }

    /// <summary>The value of "key: value" in the gloss, or null.</summary>
    public static string? Irregular(Word word, string key) =>
        word.Gloss.Split(';').Skip(1)
            .Select(part => part.Split(':', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() == key)
            .Select(pair => pair[1].Trim())
            .FirstOrDefault();

    public static string WithArticle(string noun) =>
        (noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? "an " : "a ") + noun;

    public static string Plural(Word word)
    {
        if (Irregular(word, "pl") is { } irregular)
            return irregular;
        var noun = Base(word);
        if (noun.EndsWith('y') && noun.Length > 1 && !"aeiou".Contains(noun[^2]))
            return noun[..^1] + "ies";
        return noun + (noun.EndsWith('s') || noun.EndsWith('x') || noun.EndsWith("ch") || noun.EndsWith("sh") ? "es" : "s");
    }

    /// <summary>"bought" from "pp: bought", otherwise the regular -ed form.</summary>
    public static string PastParticiple(Word word) => Irregular(word, "pp") ?? Irregular(word, "past") ?? Regular(Base(word));

    public static string Past(Word word) => Irregular(word, "past") ?? Irregular(word, "pp") ?? Regular(Base(word));

    // Only the first word is a verb: "pick up" → "picked up".
    private static string Regular(string verb)
    {
        var words = verb.Split(' ', 2);
        var first = words[0];
        first = first.EndsWith('e') ? first + "d"
            : first.EndsWith('y') && first.Length > 1 && !"aeiou".Contains(first[^2]) ? first[..^1] + "ied"
            : first + "ed";
        return words.Length == 1 ? first : $"{first} {words[1]}";
    }
}
