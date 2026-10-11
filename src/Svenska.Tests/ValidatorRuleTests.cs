using Svenska.Core.Audio;
using Svenska.Core.Content;
using Svenska.Validator;

namespace Svenska.Tests;

public class ValidatorRuleTests
{
    /// <summary>A small valid course; each test breaks one thing in it.</summary>
    private static TempDir Course(string? pack = null, string? unit = null)
    {
        var dir = new TempDir();
        dir.Write("levels.yaml", "A1:\n  grammar: [present_tense, en_ett, def_singular]\nA2:\n  grammar: [past_tense]\n");
        dir.Write("grammar.yaml", """
            - { id: present_tense, level: A1, title: Present, card: x }
            - { id: en_ett, level: A1, title: En and ett, card: x }
            - { id: def_singular, level: A1, requires: [en_ett], title: Definite, card: x }
            - { id: past_tense, level: A2, requires: [present_tense], title: Past, card: x }
            """);
        dir.Write("tags.yaml", "nouns: [food, drink]\nverbs: [action_object]\nadjectives: [state]\n");
        dir.Write("frames.yaml", "[]\n");
        dir.Write("themes/basic.yaml", """
            theme: basic
            title: Basic
            words:
              - { lemma: var, pos: adv, level: A1, gloss: where }
              - { lemma: vad, pos: pron, level: A1, gloss: what }
              - { lemma: inte, pos: adv, level: A1, gloss: not }
            """);
        dir.Write("themes/shop.yaml", pack ?? Pack);
        dir.Write("units/a1/04-shop.yaml", unit ?? Unit);
        return dir;
    }

    private const string Pack = """
        theme: shop
        title: Shop
        words:
          - { lemma: mjölk, pos: noun, gender: en, tags: [drink], level: A1, gloss: milk }
          - { lemma: ost, pos: noun, gender: en, tags: [food], level: A1, gloss: cheese }
          - { lemma: kosta, pos: verb, level: A1, gloss: to cost }
          - { lemma: finnas, pos: verb, level: A1, gloss: to be }
          - { lemma: banan, pos: noun, gender: en, tags: [food], level: A2, gloss: banana }
        """;

    private const string Unit = """
        id: a1-04
        level: A1
        theme: shop
        title: Shop
        task: Buy things.
        can_do: I can buy things.
        assumes:
          words: [var, vad, inte]
          grammar: [present_tense, en_ett]
        new_words: [mjölk, ost, kosta, finnas]
        new_grammar: [def_singular]
        model_dialogue:
          - { speaker: du, sv: "Var finns mjölken?", en: "Where is the milk?" }
        exercises:
          - { type: dictation, sv: "Var finns mjölken?", grammar: [def_singular] }
          - { type: cloze, sv: "Vad kostar ___?", hint: ost, answer: osten, grammar: [def_singular] }
          - { type: inflect, lemma: ost, form: def, answer: osten }
          - { type: word_order, sv: "Mjölken finns inte.", en: "The milk is not there." }
        final_task: { type: dialogue, turns: [{ speaker: du, en: "Ask where the milk is.", answer: "Var finns mjölken?" }] }
        """;

    private static List<ContentIssue> Errors(TempDir dir)
    {
        var (course, issues) = ContentLoader.Load(dir.Path);
        issues.AddRange(CourseValidator.Validate(course, FakeSaldo.Shop(), dir.Path));
        return issues.Where(i => i.Severity == Severity.Error).ToList();
    }

    private static void AssertError(TempDir dir, int rule, string text)
    {
        var errors = Errors(dir);
        Assert.Contains(errors, e => e.Rule == rule && e.Message.Contains(text));
    }

    [Fact]
    public void The_small_course_is_valid()
    {
        using var dir = Course();
        dir.WriteGenerated(FakeSaldo.Shop());
        Assert.Empty(Errors(dir));
    }

    [Fact]
    public void Rule_2_a_writing_exercise_with_a_word_that_is_not_known_yet()
    {
        using var dir = Course(unit: Unit.Replace("\"Mjölken finns inte.\"", "\"Bananen finns inte.\""));
        AssertError(dir, 2, "not known yet: Bananen");
    }

    [Fact]
    public void Rule_2_numbers_and_listed_names_are_exempt()
    {
        using var dir = Course(unit: Unit
            .Replace("\"Mjölken finns inte.\"", "\"Osten kostar inte 45 Zlatan.\"")
            .Replace("new_grammar:", "names: [Zlatan]\nnew_grammar:"));
        dir.WriteGenerated(FakeSaldo.Shop());
        Assert.Empty(Errors(dir));
    }

    [Fact]
    public void Rule_3_a_listening_text_with_an_unknown_word()
    {
        using var dir = Course(unit: Unit.Replace("sv: \"Var finns mjölken?\", en: \"Where", "sv: \"Var finns bananen?\", en: \"Where"));
        AssertError(dir, 3, "bananen");
    }

    [Fact]
    public void Rule_4_grammar_used_before_it_is_introduced_or_above_the_level()
    {
        using var early = Course(unit: Unit.Replace("\"Var finns mjölken?\", grammar: [def_singular]", "\"Var finns mjölken?\", grammar: [past_tense]"));
        AssertError(early, 4, "'past_tense' is used before it is introduced");

        using var level = Course(unit: Unit.Replace("new_grammar: [def_singular]", "new_grammar: [def_singular, past_tense]"));
        AssertError(level, 4, "'past_tense' is A2, above this A1 unit");

        using var requires = Course(unit: Unit.Replace("grammar: [present_tense, en_ett]", "grammar: [present_tense]"));
        AssertError(requires, 4, "'def_singular' requires 'en_ett'");
    }

    [Fact]
    public void Rule_5_too_much_new_at_once_or_a_word_above_the_level()
    {
        using var grammar = Course(unit: Unit
            .Replace("grammar: [present_tense, en_ett]", "grammar: []")
            .Replace("new_grammar: [def_singular]", "new_grammar: [def_singular, present_tense, en_ett]"));
        AssertError(grammar, 5, "3 new grammar points");

        using var word = Course(unit: Unit.Replace("new_words: [mjölk,", "new_words: [banan, mjölk,"));
        AssertError(word, 5, "'banan' is A2");
    }

    [Fact]
    public void Rule_6_a_word_in_no_pack_or_in_two_packs()
    {
        using var missing = Course(unit: Unit.Replace("new_words: [mjölk,", "new_words: [kaviar, mjölk,"));
        AssertError(missing, 6, "'kaviar' is in no theme pack");

        using var twice = Course(pack: Pack + "\n  - { lemma: inte, pos: adv, level: A1, gloss: not }");
        AssertError(twice, 6, "'inte' is listed more than once");
    }

    [Fact]
    public void Rule_7_a_tag_that_is_not_in_the_tag_list()
    {
        using var dir = Course(pack: Pack.Replace("tags: [drink]", "tags: [liquid]"));
        AssertError(dir, 7, "tag 'liquid'");
    }

    [Fact]
    public void Rule_9_a_new_word_that_no_exercise_uses()
    {
        using var dir = Course(unit: Unit.Replace("- { type: cloze, sv: \"Vad kostar ___?\", hint: ost, answer: osten, grammar: [def_singular] }", "")
            .Replace("- { type: inflect, lemma: ost, form: def, answer: osten }", ""));
        AssertError(dir, 9, "never used in an exercise: ost, kosta");
    }

    [Fact]
    public void Rule_11_missing_audio_is_a_warning_until_the_files_exist()
    {
        using var dir = Course();
        var (course, _) = ContentLoader.Load(dir.Path);

        Assert.Contains(CourseValidator.Validate(course, null, dir.Path), i => i.Rule == 11 && i.Severity == Severity.Warning);

        foreach (var (text, slow) in AudioFiles.Lines(course))
            dir.Write("audio/" + AudioFiles.FileName(text, slow), "");
        Assert.DoesNotContain(CourseValidator.Validate(course, null, dir.Path), i => i.Rule == 11);
    }

    [Fact]
    public void Items_must_have_what_their_type_needs()
    {
        using var gap = Course(unit: Unit.Replace("\"Vad kostar ___?\"", "\"Vad kostar osten?\""));
        AssertError(gap, 1, "exactly one gap");

        using var form = Course(unit: Unit.Replace("form: def, answer: osten", "form: def, answer: ostet"));
        AssertError(form, 1, "'ostet' is not the definite form (osten)");

        using var verbForm = Course(unit: Unit.Replace("form: def, answer: osten", "form: pres, answer: ostar"));
        AssertError(verbForm, 7, "'pres' is not a form of a noun");

        using var type = Course(unit: Unit.Replace("type: dictation", "type: karaoke"));
        AssertError(type, 1, "unknown exercise type 'karaoke'");

        using var gender = Course(pack: Pack.Replace("pos: noun, gender: en, tags: [food], level: A1", "pos: noun, tags: [food], level: A1"));
        AssertError(gender, 1, "noun 'ost' needs gender");
    }

    [Fact]
    public void Audio_file_names_depend_on_text_and_speed_only()
    {
        Assert.Equal(AudioFiles.FileName("Var finns mjölken?", false), AudioFiles.FileName(" Var finns mjölken? ", false));
        Assert.NotEqual(AudioFiles.FileName("Var finns mjölken?", false), AudioFiles.FileName("Var finns mjölken?", true));
        Assert.Matches("^[0-9a-f]{20}\\.ogg$", AudioFiles.FileName("ost", false));
    }
}
