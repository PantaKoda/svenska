using Svenska.Core.Content;
using Svenska.Core.Generation;
using Svenska.Core.Progress;
using Svenska.Core.Reference;
using Svenska.Validator;

namespace Svenska.Tests;

public class PhraseTests
{
    private static TempDir Course(string phrases, string templates = "[phrases]", string exercises = "")
    {
        var dir = new TempDir();
        dir.Write("levels.yaml", "A1:\n  grammar: [present_tense]\n");
        dir.Write("grammar.yaml", "- { id: present_tense, level: A1, title: Present, card: x }\n");
        dir.Write("tags.yaml", "nouns: [food]\nverbs: []\nadjectives: []\n");
        dir.Write("frames.yaml", "[]\n");
        dir.Write("themes/shop.yaml", """
            theme: shop
            title: Shop
            words:
              - { lemma: ost, pos: noun, gender: en, level: A1, gloss: cheese }
              - { lemma: finnas, pos: verb, level: A1, gloss: to be, forms: { pres: finns } }
              - { lemma: var, pos: adv, level: A1, gloss: where }
            """);
        dir.Write("units/a1/01.yaml", $$"""
            id: a1-01
            level: A1
            theme: shop
            title: Shop
            task: Buy.
            can_do: I can buy.
            new_words: [ost, finnas, var]
            new_grammar: [present_tense]
            phrases:
            {{phrases}}
            core:
              templates: {{templates}}
              items_per_template: 2
            exercises:
              - { type: dictation, sv: "Var finns osten?" }
            {{exercises}}
            final_task: { type: dialogue, turns: [{ speaker: du, en: "Ask where the cheese is.", answer: "Var finns osten?" }] }
            """);
        return dir;
    }

    private const string Three = """
          - { sv: "Ursäkta.", en: "Excuse me.", accept: ["Ursäkta mig."] }
          - { sv: "Jag förstår inte.", en: "I don't understand." }
          - { sv: "Vi ses!", en: "See you!" }
        """;

    private static List<ContentIssue> Errors(TempDir dir)
    {
        dir.WriteGenerated(FakeSaldo.Shop());
        var (course, issues) = ContentLoader.Load(dir.Path);
        issues.AddRange(CourseValidator.Validate(course, FakeSaldo.Shop(), dir.Path));
        return issues.Where(i => i.Severity == Severity.Error).ToList();
    }

    [Fact]
    public void Every_phrase_gets_three_drills_whatever_the_template_size()
    {
        using var dir = Course(Three);
        var (course, _) = ContentLoader.Load(dir.Path);
        var items = new DrillGenerator(course, FakeSaldo.Shop()).Generate(course.Units[0]).File.Items;

        Assert.Equal(9, items.Count);
        Assert.All(items, i => Assert.Equal("phrase", i.Frame));

        var choose = items.First(i => i.Type == "listen_choose");
        Assert.Equal(("Ursäkta.", "Excuse me."), (choose.Sv, choose.Answer));
        Assert.Equal(["Excuse me.", "I don't understand.", "See you!"], choose.Options);

        var translate = items.First(i => i.Type == "translate");
        Assert.Equal(("Excuse me.", "Ursäkta."), (translate.En, translate.Sv));
        Assert.Equal(["Ursäkta mig."], translate.Accept);
        Assert.Contains(items, i => i is { Type: "dictation", Sv: "Vi ses!" });
    }

    [Fact]
    public void A_phrase_is_known_as_a_whole_although_its_words_are_not_taught()
    {
        // "förstår", "ses", "mig" are in no pack: the phrases, their accepted variants and a
        // sentence that contains a phrase are still valid.
        using var dir = Course(Three, exercises: "  - { type: dictation, sv: \"Ursäkta. Var finns osten?\" }");
        Assert.Empty(Errors(dir));
    }

    [Fact]
    public void The_words_of_a_phrase_are_not_known_outside_it()
    {
        using var dir = Course(Three, exercises: "  - { type: dictation, sv: \"Jag förstår osten.\" }");
        Assert.Contains(Errors(dir), e => e.Rule == 2 && e.Message.Contains("not known yet: Jag, förstår"));
    }

    [Fact]
    public void A_phrase_must_be_practised_and_a_unit_may_not_have_too_many()
    {
        using var unused = Course(Three, templates: "[]");
        Assert.Contains(Errors(unused), e => e.Rule == 9 && e.Message.Contains("phrases never used") && e.Message.Contains("Vi ses!"));

        var nine = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"  - {{ sv: \"Fras {i}!\", en: \"Phrase {i}!\" }}"));
        using var many = Course(nine);
        Assert.Contains(Errors(many), e => e.Rule == 5 && e.Message.Contains("9 new phrases"));

        using var twice = Course(Three + "\n  - { sv: \"vi ses!\", en: \"See you again!\" }");
        Assert.Contains(Errors(twice), e => e.Message.Contains("is already taught"));
    }

    [Fact]
    public void Phrases_have_audio_enter_the_review_queue_and_can_be_looked_up()
    {
        using var dir = Course(Three);
        var (course, _) = ContentLoader.Load(dir.Path);

        Assert.Contains(("Jag förstår inte.", true), Svenska.Core.Audio.AudioFiles.Lines(course));

        var now = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        using var store = new ProgressStore(Path.Combine(dir.Path, "progress.db"));
        var progress = new ProgressTracker(course, store, new LeitnerScheduler(), () => now);
        progress.RecordUnitAnswer(course.Units[0], course.Units[0].Exercises[0], correct: true);
        now = now.AddDays(1);
        var card = progress.ReviewSession().Single(r => r.Key == "phrase:Vi ses!").Item;
        Assert.Equal(("translate", "See you!", "Vi ses!"), (card.Type, card.En, card.Sv));

        var entry = new WordLookup(course, FakeSaldo.Shop()).Lookup("förstår").Entries.Single();
        Assert.Equal(("Jag förstår inte.", "phrase", "I don't understand.", "A1-01 Shop"),
            (entry.Headword, entry.PartOfSpeech, entry.Gloss, entry.TaughtIn));
    }
}
