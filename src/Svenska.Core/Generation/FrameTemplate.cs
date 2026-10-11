using System.Text.RegularExpressions;
using Svenska.Core.Saldo;

namespace Svenska.Core.Generation;

/// <summary>
/// One slot of a frame: {tool|container.def} or {state~1}.
/// <paramref name="Form"/> is null for an adjective that agrees with slot <paramref name="AgreesWith"/>.
/// </summary>
public sealed record Slot(int Number, IReadOnlyList<string> Tags, string? Form, int? AgreesWith)
{
    public string? Pos => AgreesWith != null ? "adj" : WordForms.PosOf(Form ?? "");

    /// <summary>The grammar point the slot's form needs, if any.</summary>
    public string? Grammar => AgreesWith != null ? WordForms.AgreementGrammar : WordForms.GrammarOf(Form ?? "");
}

/// <summary>A reference to a slot in the English line: {1}, {1.a}, {2.pp}.</summary>
public sealed record EnglishRef(int Slot, string? Modifier);

/// <summary>The Swedish line of a frame, split into fixed text and slots.</summary>
public sealed partial class FrameTemplate
{
    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Braces();

    [GeneratedRegex(@"^([a-z_]+(?:\|[a-z_]+)*)(?:\.([a-z_]+)|~(\d+))$")]
    private static partial Regex SlotSyntax();

    [GeneratedRegex(@"^(\d+)(?:\.([a-z]+))?$")]
    private static partial Regex EnglishSyntax();

    public static readonly string[] EnglishModifiers = ["a", "pp", "past"];

    /// <summary>Fixed text (string) and slots (<see cref="Slot"/>) in sentence order.</summary>
    public IReadOnlyList<object> Parts { get; }
    public IReadOnlyList<Slot> Slots { get; }

    /// <summary>The words of the frame that are not slots, e.g. "Var är ?".</summary>
    public string FixedText => string.Join(" ", Parts.OfType<string>());

    private FrameTemplate(List<object> parts)
    {
        Parts = parts;
        Slots = parts.OfType<Slot>().ToList();
    }

    /// <exception cref="FormatException">The slot syntax is wrong; the message says how.</exception>
    public static FrameTemplate Parse(string sv)
    {
        var parts = new List<object>();
        var position = 0;
        var number = 0;
        foreach (Match match in Braces().Matches(sv))
        {
            if (match.Index > position)
                parts.Add(sv[position..match.Index]);
            position = match.Index + match.Length;

            var syntax = SlotSyntax().Match(match.Groups[1].Value);
            if (!syntax.Success)
                throw new FormatException($"slot {match.Value} is not {{tags.form}} or {{tag~N}}");
            parts.Add(new Slot(++number, syntax.Groups[1].Value.Split('|'),
                syntax.Groups[2].Success ? syntax.Groups[2].Value : null,
                syntax.Groups[3].Success ? int.Parse(syntax.Groups[3].Value) : null));
        }
        if (position < sv.Length)
            parts.Add(sv[position..]);
        if (parts.OfType<string>().Any(p => p.Contains('{') || p.Contains('}')))
            throw new FormatException("unbalanced { }");
        return new FrameTemplate(parts);
    }

    /// <summary>The slot references of an English line, in order.</summary>
    /// <exception cref="FormatException">A reference is not {N}, {N.a}, {N.pp} or {N.past}.</exception>
    public static List<EnglishRef> EnglishRefs(string en)
    {
        var refs = new List<EnglishRef>();
        foreach (Match match in Braces().Matches(en))
        {
            var syntax = EnglishSyntax().Match(match.Groups[1].Value);
            var modifier = syntax.Groups[2].Success ? syntax.Groups[2].Value : null;
            if (!syntax.Success || (modifier != null && !EnglishModifiers.Contains(modifier)))
                throw new FormatException($"en: {match.Value} is not {{N}}, {{N.a}}, {{N.pp}} or {{N.past}}");
            refs.Add(new EnglishRef(int.Parse(syntax.Groups[1].Value), modifier));
        }
        return refs;
    }

    /// <summary>Replaces each {…} of an English line with what <paramref name="render"/> returns.</summary>
    public static string FillEnglish(string en, Func<EnglishRef, string> render) =>
        Braces().Replace(en, match =>
        {
            var syntax = EnglishSyntax().Match(match.Groups[1].Value);
            return render(new EnglishRef(int.Parse(syntax.Groups[1].Value),
                syntax.Groups[2].Success ? syntax.Groups[2].Value : null));
        });
}
