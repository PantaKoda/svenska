using Svenska.Core.Content;
using Svenska.Core.Text;

namespace Svenska.Core.Exercises;

/// <summary>What an exercise item shows and expects, for the four written exercise types.</summary>
public static class ExerciseText
{
    public const string Gap = "___";

    public const string Dictation = "dictation";
    public const string Cloze = "cloze";
    public const string Inflect = "inflect";
    public const string WordOrder = "word_order";

    /// <summary>The types the exercise player can run.</summary>
    public static readonly string[] Playable = [Dictation, Cloze, Inflect, WordOrder];

    /// <summary>All types of AGENTS.md; the others get their fields and player in M2 and M5.</summary>
    public static readonly string[] AllTypes =
        [.. Playable, "translate", "scene_click", "listen_choose", "numbers", "dialogue", "free_write"];

    /// <summary>What the learner must type.</summary>
    public static string Expected(ExerciseItem item) => item.Type switch
    {
        Cloze or Inflect => item.Answer ?? "",
        _ => item.Answer ?? item.Sv ?? "",
    };

    /// <summary>The complete Swedish text of the item, with the gap filled.</summary>
    public static string FullSentence(ExerciseItem item) => item.Type switch
    {
        Cloze => (item.Sv ?? "").Replace(Gap, item.Answer ?? ""),
        Inflect => item.Answer ?? "",
        _ => item.Sv ?? "",
    };

    /// <summary>
    /// The words of a word_order item in a mixed order. The order depends only on the
    /// sentence, so an item looks the same every time.
    /// </summary>
    public static List<string> ShuffledTokens(string sentence, IReadOnlyCollection<string> names)
    {
        var tokens = Tokenizer.Tokens(sentence);
        if (tokens.Count > 0 && !names.Contains(tokens[0]))
            tokens[0] = tokens[0].ToLowerInvariant(); // a capital would give the first word away
        if (tokens.Count < 2)
            return tokens;

        var original = tokens.ToList();
        var random = new Random(sentence.Aggregate(17, (hash, c) => unchecked(hash * 31 + c)));
        for (var i = tokens.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (tokens[i], tokens[j]) = (tokens[j], tokens[i]);
        }
        if (tokens.SequenceEqual(original))
            tokens = [.. original.Skip(1), original[0]];
        return tokens;
    }
}
