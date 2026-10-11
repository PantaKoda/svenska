using Svenska.Core.Saldo;

namespace Svenska.Core.Answers;

public enum AnswerKind { Correct, MissingDiacritic, WrongForm, Wrong }

/// <summary>One word of the expected answer and whether the learner got it.</summary>
public sealed record WordMark(string Word, bool Right);

/// <param name="Expected">The answer to show (the item's main answer).</param>
/// <param name="Feedback">One or two short lines; empty when correct.</param>
/// <param name="Words">The expected answer word by word.</param>
public sealed record AnswerResult(AnswerKind Kind, string Expected, string Feedback, IReadOnlyList<WordMark> Words)
{
    public bool IsCorrect => Kind == AnswerKind.Correct;
}

/// <summary>
/// Compares what the learner typed with the expected answer. Spacing, punctuation and
/// the capital at the start are ignored; å, ä and ö are never ignored.
/// </summary>
public sealed class AnswerChecker(ISaldoLexicon? saldo = null)
{
    private const string Punctuation = ".,!?;:\"“”«»…";

    public AnswerResult Check(string given, string expected, IEnumerable<string>? accept = null)
    {
        var givenWords = Words(given);
        var candidates = new[] { expected }.Concat(accept ?? []).Select(c => Words(c)).ToList();

        if (candidates.Any(c => c.SequenceEqual(givenWords)))
            return new(AnswerKind.Correct, expected, "", Words(expected, keepCase: true).ConvertAll(w => new WordMark(w, true)));

        // Explain against the accepted answer that is closest to what was typed.
        var target = candidates.MinBy(c => c.Count + givenWords.Count - 2 * CommonWords(givenWords, c).Count)!;
        var display = target == candidates[0] ? Words(expected, keepCase: true) : target;

        if (target.Select(WithoutDiacritics).SequenceEqual(givenWords.Select(WithoutDiacritics)))
        {
            var marks = display.Select((w, i) => new WordMark(w, target[i] == givenWords[i])).ToList();
            var wrong = string.Join(", ", marks.Where(m => !m.Right).Select(m => m.Word));
            return new(AnswerKind.MissingDiacritic, expected, $"Almost. Check å, ä and ö: {wrong}.", marks);
        }

        // Only capital letters differ ("ali" for "Ali"): say so, it is not a wrong word.
        if (target.Count == givenWords.Count
            && target.Zip(givenWords).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
        {
            var marks = display.Select((w, i) => new WordMark(w, target[i] == givenWords[i])).ToList();
            var wrong = string.Join(", ", marks.Where(m => !m.Right).Select(m => m.Word));
            return new(AnswerKind.Wrong, expected, $"Almost. Check the capital letters: {wrong}.", marks);
        }

        var common = CommonWords(givenWords, target);
        var missed = Remaining(target, common);
        var extra = Remaining(givenWords, common);
        var wordMarks = Mark(display, target, common);

        if (missed.Count > 0 && missed.Order().SequenceEqual(extra.Order()))
            return new(AnswerKind.Wrong, expected, "The words are right, but not the order.", wordMarks);

        var wrongForms = WrongForms(extra, missed);
        if (wrongForms.Count > 0 && wrongForms.Count == missed.Count && extra.Count == missed.Count)
            return new(AnswerKind.WrongForm, expected,
                "Right word, wrong form: expected " + string.Join(", ", wrongForms) + ".", wordMarks);

        var lines = new List<string>();
        if (missed.Count > 0)
            lines.Add((extra.Count > 0 ? "Wrong or missing: " : "Missing: ")
                      + string.Join(", ", wordMarks.Where(m => !m.Right).Select(m => m.Word)) + ".");
        else if (extra.Count > 0)
            lines.Add("Too many words: " + string.Join(", ", extra) + ".");
        if (wrongForms.Count > 0)
            lines.Add("Right word, wrong form: expected " + string.Join(", ", wrongForms) + ".");
        return new(AnswerKind.Wrong, expected, string.Join("\n", lines), wordMarks);
    }

    /// <summary>
    /// A number the learner heard (a price, a time, a phone number). Only the digits count:
    /// "15.30", "15:30" and "1530" are the same answer.
    /// </summary>
    public static AnswerResult CheckNumber(string given, string expected)
    {
        static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());
        var right = Digits(given).Length > 0 && Digits(given) == Digits(expected);
        return new(right ? AnswerKind.Correct : AnswerKind.Wrong, expected, right ? "" : $"It was {expected}.",
            [new WordMark(expected, right)]);
    }

    /// <summary>
    /// The words of an answer without punctuation. Unless <paramref name="keepCase"/>, the
    /// first letter of every sentence is made lower-case ("Hej! Var finns …" → hej, var, finns).
    /// </summary>
    private static List<string> Words(string text, bool keepCase = false)
    {
        var words = new List<string>();
        var sentenceStart = true;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = new string(raw.Where(c => !Punctuation.Contains(c)).ToArray());
            if (word.Length > 0)
                words.Add(sentenceStart && !keepCase ? char.ToLowerInvariant(word[0]) + word[1..] : word);
            if (word.Length > 0 || raw.Length > 0)
                sentenceStart = raw[^1] is '.' or '!' or '?' or '…';
        }
        return words;
    }

    private static string WithoutDiacritics(string word) => string.Create(word.Length, word, (span, w) =>
    {
        for (var i = 0; i < w.Length; i++)
            span[i] = w[i] switch
            {
                'å' or 'ä' => 'a', 'ö' => 'o', 'é' => 'e',
                'Å' or 'Ä' => 'A', 'Ö' => 'O', 'É' => 'E',
                var c => c,
            };
    });

    /// <summary>Longest common subsequence: the words that are right and in the right order.</summary>
    private static List<string> CommonWords(List<string> a, List<string> b)
    {
        var length = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        for (var j = b.Count - 1; j >= 0; j--)
            length[i, j] = a[i] == b[j] ? length[i + 1, j + 1] + 1 : Math.Max(length[i + 1, j], length[i, j + 1]);

        var common = new List<string>();
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (a[i] == b[j]) { common.Add(a[i]); i++; j++; }
            else if (length[i + 1, j] >= length[i, j + 1]) i++;
            else j++;
        }
        return common;
    }

    /// <summary>The words that are not part of the common sequence, in order.</summary>
    private static List<string> Remaining(List<string> words, List<string> common)
    {
        var rest = new List<string>();
        var next = 0;
        foreach (var word in words)
        {
            if (next < common.Count && word == common[next]) next++;
            else rest.Add(word);
        }
        return rest;
    }

    private static List<WordMark> Mark(List<string> display, List<string> target, List<string> common)
    {
        var marks = new List<WordMark>();
        var next = 0;
        for (var i = 0; i < target.Count; i++)
        {
            var right = next < common.Count && target[i] == common[next];
            if (right) next++;
            marks.Add(new WordMark(display[i], right));
        }
        return marks;
    }

    /// <summary>
    /// For each missed word that the learner wrote in another form of the same lemma:
    /// "mjölken (definite)".
    /// </summary>
    private List<string> WrongForms(List<string> typed, List<string> missed)
    {
        var result = new List<string>();
        if (saldo == null)
            return result;

        var unused = typed.ToList();
        foreach (var expected in missed)
        {
            var readings = saldo.Lookup(expected);
            foreach (var word in unused)
            {
                var typedLemmas = saldo.Lookup(word).Select(f => f.LemmaId).ToHashSet();
                var shared = readings.FirstOrDefault(f => typedLemmas.Contains(f.LemmaId));
                if (shared == null)
                    continue;
                var label = WordForms.LabelOfMsd(shared.Msd);
                result.Add(label == null ? expected : $"{expected} ({label})");
                unused.Remove(word);
                break;
            }
        }
        return result;
    }
}
