using Svenska.Core.Content;
using Svenska.Core.Generation;
using Svenska.Validator;

namespace Svenska.Tests;

public class GeneratorTests
{
    private static FakeSaldo Saldo() => FakeSaldo.Shop()
        .Add("ha", "vb", null, "ha=inf aktiv", "har=pres ind aktiv")
        .Add("du", "pn", null, "du=nom")
        .Add("trasig", "av", null, "trasig=pos indef sg u nom", "trasigt=pos indef sg n nom", "trasiga=pos indef pl nom")
        .Add("variant", "nn", "en", "variant=sg indef nom", "varianten=sg def nom", "variantn=sg def nom");

    private const string Frames = """
        - { id: f-where, level: A1, grammar: [def_singular], sv: "Var finns {food|drink.def}?", en: "Where is the {1}?" }
        - { id: f-have,  level: A1, grammar: [en_ett], review: true, sv: "Har du {food|doc.a}?", en: "Do you have {1.a}?" }
        - { id: f-state, level: A1, grammar: [agree, def_singular], sv: "{food|drink|doc.def} är {state~1}.", en: "The {1} is {2}." }
        - { id: f-past,  level: A2, grammar: [past_tense], sv: "Jag {buy.past} {food.indef}.", en: "I {1.past} {2}." }
        - { id: f-early, level: A1, grammar: [plural_indef], sv: "Har du {food.pl}?", en: "Do you have {1}?" }
        - { id: f-word,  level: A1, grammar: [def_singular], sv: "Imorgon finns {food.def}.", en: "Tomorrow there is the {1}." }
        """;

    private static TempDir Course(string frames = Frames, string unitExtra = "")
    {
        var dir = new TempDir();
        dir.Write("levels.yaml", "A1:\n  grammar: [en_ett, def_singular, plural_indef, adjective_agreement_basic]\nA2:\n  grammar: [past_tense]\n");
        dir.Write("grammar.yaml", """
            - { id: en_ett, level: A1, title: En and ett, card: x }
            - { id: def_singular, level: A1, requires: [en_ett], title: Definite, card: x }
            - { id: plural_indef, level: A1, title: Plural, card: x }
            - { id: adjective_agreement_basic, level: A1, title: Adjectives, card: x }
            - { id: past_tense, level: A2, title: Past, card: x }
            """);
        dir.Write("tags.yaml", "nouns: [food, drink, doc]\nverbs: [buy]\nadjectives: [state]\n");
        dir.Write("frames.yaml", frames.Replace("agree", "adjective_agreement_basic"));
        dir.Write("themes/shop.yaml", """
            theme: shop
            title: Shop
            words:
              - { lemma: var, pos: adv, level: A1, gloss: where }
              - { lemma: en, pos: article, level: A1, gloss: a }
              - { lemma: vara, pos: verb, level: A1, gloss: to be }
              - { lemma: ha, pos: verb, level: A1, gloss: to have }
              - { lemma: du, pos: pron, level: A1, gloss: you }
              - { lemma: jag, pos: pron, level: A1, gloss: I }
              - { lemma: finnas, pos: verb, level: A1, gloss: to be there }
              - { lemma: mjölk, pos: noun, gender: en, tags: [drink], level: A1, gloss: milk }
              - { lemma: ost, pos: noun, gender: en, tags: [food], level: A1, gloss: cheese }
              - { lemma: bröd, pos: noun, gender: ett, tags: [food], level: A1, gloss: bread }
              - { lemma: kvitto, pos: noun, gender: ett, tags: [doc], level: A1, gloss: receipt }
              - { lemma: variant, pos: noun, gender: en, tags: [food], level: A1, gloss: variant }
              - { lemma: trasig, pos: adj, tags: [state], level: A1, gloss: broken }
              - { lemma: kosta, pos: verb, tags: [buy], level: A1, gloss: "to cost; past: cost" }
            """);
        dir.Write("units/a1/01.yaml", $$"""
            id: a1-01
            level: A1
            theme: shop
            title: Shop
            task: Buy.
            can_do: I can buy.
            assumes:
              words: [var, en, vara, ha, du, jag, finnas, mjölk, trasig, kosta]
              grammar: [en_ett, adjective_agreement_basic]
            new_words: [ost, bröd, kvitto, variant]
            new_grammar: [def_singular]
            core:
              templates: [cloze, dictation, word_order, translate, inflect]
              items_per_template: 4
            final_task: { type: dialogue, turns: [] }
            {{unitExtra}}
            """);
        return dir;
    }

    private static GenerationResult Generate(TempDir dir)
    {
        var (course, issues) = ContentLoader.Load(dir.Path);
        Assert.Empty(issues);
        return new DrillGenerator(course, Saldo()).Generate(course.Units[0]);
    }

    [Fact]
    public void Every_sentence_contains_a_new_word_of_the_unit_and_records_its_source()
    {
        using var dir = Course();
        var items = Generate(dir).File.Items;

        Assert.Equal(4, items.Count(i => i.Type == "cloze"));
        Assert.Equal(4, items.Count(i => i.Type == "dictation"));
        string[] focus = ["ost", "bröd", "kvitto", "variant"];
        Assert.All(items, i => Assert.Contains(i.Words!, focus.Contains));
        Assert.All(items.Where(i => i.Type != "inflect"), i => Assert.StartsWith("f-", i.Frame));
        // "Var finns mjölken?" has no new word: mjölk is assumed, not new.
        Assert.DoesNotContain(items, i => i.Sv == "Var finns mjölken?");
    }

    [Fact]
    public void Output_is_the_same_every_time()
    {
        using var dir = Course();

        Assert.Equal(DrillGenerator.ToYaml(Generate(dir).File), DrillGenerator.ToYaml(Generate(dir).File));
    }

    [Fact]
    public void Frames_above_the_level_or_with_grammar_not_yet_introduced_are_not_used()
    {
        using var dir = Course();
        var items = Generate(dir).File.Items;

        Assert.DoesNotContain(items, i => i.Frame is "f-past" or "f-early");
    }

    [Fact]
    public void A_frame_whose_fixed_words_are_not_known_is_skipped_and_reported()
    {
        using var dir = Course();
        var result = Generate(dir);

        Assert.DoesNotContain(result.File.Items, i => i.Frame == "f-word");
        Assert.Contains(result.Notes, n => n.Contains("f-word") && n.Contains("Imorgon"));
    }

    [Fact]
    public void A_word_with_several_forms_in_saldo_is_skipped_and_reported()
    {
        using var dir = Course();
        var result = Generate(dir);

        Assert.DoesNotContain(result.File.Items, i => i.Sv?.Contains("ariant") == true && i.Frame == "f-where");
        Assert.Contains(result.Notes, n => n.Contains("variant: several definite forms (varianten / variantn)"));
    }

    [Fact]
    public void Slots_are_inflected_with_article_and_agreement()
    {
        using var dir = Course(unitExtra: "", frames: Frames);
        var (course, _) = ContentLoader.Load(dir.Path);
        course.Units[0].Core!.ItemsPerTemplate = 50;
        var sentences = new DrillGenerator(course, Saldo()).Generate(course.Units[0]).File.Items
            .Where(i => i.Type == "dictation").ToDictionary(i => i.Sv!, i => i.En!);

        Assert.Equal("Do you have a receipt?", sentences["Har du ett kvitto?"]);
        Assert.Equal("Do you have a cheese?", sentences["Har du en ost?"]);
        Assert.Equal("The bread is broken.", sentences["Brödet är trasigt."]);
        Assert.Equal("The cheese is broken.", sentences["Osten är trasig."]);
        Assert.Equal("Where is the cheese?", sentences["Var finns osten?"]);
    }

    [Fact]
    public void Cloze_blanks_the_new_word_and_keeps_the_capital_at_the_start()
    {
        using var dir = Course();
        var (course, _) = ContentLoader.Load(dir.Path);
        course.Units[0].Core!.ItemsPerTemplate = 50;
        var cloze = new DrillGenerator(course, Saldo()).Generate(course.Units[0]).File.Items.Where(i => i.Type == "cloze").ToList();

        Assert.Contains(cloze, i => i is { Sv: "Var finns ___?", Answer: "osten", Hint: "ost" });
        Assert.Contains(cloze, i => i is { Sv: "___ är trasigt.", Answer: "Brödet", Hint: "bröd" });
        Assert.Contains(cloze, i => i is { Sv: "Har du ___?", Answer: "ett kvitto", Hint: "kvitto" });
    }

    [Fact]
    public void Items_inherit_review_from_their_frame()
    {
        using var dir = Course();
        var items = Generate(dir).File.Items.Where(i => i.Frame != null).ToList();

        Assert.All(items, i => Assert.Equal(i.Frame == "f-have", i.Review));
    }

    [Fact]
    public void Inflect_items_use_the_new_grammar_and_skip_what_is_authored()
    {
        using var dir = Course(unitExtra: "exercises:\n  - { type: inflect, lemma: ost, form: def, answer: osten }");
        var inflect = Generate(dir).File.Items.Where(i => i.Type == "inflect").ToList();

        Assert.Contains(inflect, i => i is { Lemma: "bröd", Form: "def", Answer: "brödet" });
        Assert.DoesNotContain(inflect, i => i is { Lemma: "ost", Form: "def" });
        Assert.DoesNotContain(inflect, i => i.Lemma == "variant" && i.Form == "def");
        Assert.Equal(["def", "def"], inflect.Take(2).Select(i => i.Form));
    }

    [Fact]
    public void Sentences_already_authored_are_not_generated_again()
    {
        using var dir = Course(unitExtra: "exercises:\n  - { type: dictation, sv: \"Var finns osten?\" }");
        var (course, _) = ContentLoader.Load(dir.Path);
        course.Units[0].Core!.ItemsPerTemplate = 50;

        Assert.DoesNotContain(new DrillGenerator(course, Saldo()).Generate(course.Units[0]).File.Items, i => i.Sv == "Var finns osten?");
    }

    [Fact]
    public void Generated_file_loads_back_and_passes_the_validator()
    {
        using var dir = Course();
        dir.WriteGenerated(Saldo());

        var (course, issues) = ContentLoader.Load(dir.Path);
        issues.AddRange(CourseValidator.Validate(course, Saldo(), dir.Path));

        Assert.Empty(issues.Where(i => i.Severity == Severity.Error));
        Assert.Equal(20, course.Generated.Single(g => g.Unit == "a1-01").Items.Count);
        Assert.NotEmpty(course.Generated.Single(g => g.Unit == "theme-shop").Items);
    }

    [Fact]
    public void Rule_8_a_generated_file_that_is_missing_or_out_of_date()
    {
        using var dir = Course();
        var (course, _) = ContentLoader.Load(dir.Path);
        Assert.Contains(CourseValidator.Validate(course, Saldo(), dir.Path), i => i.Rule == 8 && i.Message.Contains("missing"));

        dir.Write("generated/a1-01.yaml", DrillGenerator.ToYaml(Generate(dir).File).Replace("osten", "ostet"));
        (course, _) = ContentLoader.Load(dir.Path);
        Assert.Contains(CourseValidator.Validate(course, Saldo(), dir.Path), i => i.Rule == 8 && i.Message.Contains("out of date"));
    }

    [Theory]
    [InlineData("Var är {food.def", "unbalanced", 7)]
    [InlineData("Var är {food.definite}?", "unknown form 'definite'", 7)]
    [InlineData("Var är {fruit.def}?", "'fruit' is not a noun tag", 7)]
    [InlineData("{food.def} är {state~2}.", "~2 must point to a noun slot", 7)]
    [InlineData("Har du {food.pl}?", "uses grammar 'plural_indef', which the frame does not list", 4)]
    [InlineData("Har du {buy.def}?", "'buy' is not a noun tag", 7)]
    public void Rule_7_a_frame_with_a_bad_slot(string sv, string message, int rule)
    {
        var frame = "- { id: bad, level: A1, grammar: [def_singular, adjective_agreement_basic], sv: \"" + sv + "\", en: \"x {1}\" }";
        using var dir = Course(frames: frame);
        var (course, loadIssues) = ContentLoader.Load(dir.Path);

        // An unclosed brace is caught by the frame parser, not by YAML.
        Assert.Empty(loadIssues);
        Assert.Contains(CourseValidator.Validate(course), i => i.Rule == rule && i.Message.Contains(message));
    }

    [Fact]
    public void Rule_7_english_line_must_refer_to_real_slots()
    {
        using var dir = Course(frames: "- { id: bad, level: A1, grammar: [def_singular], sv: \"Var är {food.def}?\", en: \"Where is {2} {1.pp}?\" }");
        var (course, _) = ContentLoader.Load(dir.Path);
        var issues = CourseValidator.Validate(course);

        Assert.Contains(issues, i => i.Rule == 7 && i.Message.Contains("slot 2, which does not exist"));
        Assert.Contains(issues, i => i.Rule == 7 && i.Message.Contains(".pp on slot 1, which is not a verb"));
    }

    [Fact]
    public void English_words_come_from_the_gloss()
    {
        var buy = new Word { Lemma = "köpa", Pos = "verb", Gloss = "to buy; past: bought; pp: bought" };
        var wipe = new Word { Lemma = "torka", Pos = "verb", Gloss = "to wipe" };
        var carry = new Word { Lemma = "bära", Pos = "verb", Gloss = "to carry" };
        var krona = new Word { Lemma = "krona", Pos = "noun", Gloss = "krona (Swedish money)" };
        var loaf = new Word { Lemma = "limpa", Pos = "noun", Gloss = "loaf; pl: loaves" };
        var box = new Word { Lemma = "låda", Pos = "noun", Gloss = "box, drawer" };

        Assert.Equal("buy", English.Base(buy));
        Assert.Equal("bought", English.PastParticiple(buy));
        Assert.Equal("wiped", English.Past(wipe));
        Assert.Equal("carried", English.PastParticiple(carry));
        Assert.Equal("krona", English.Base(krona));
        Assert.Equal("loaves", English.Plural(loaf));
        Assert.Equal("boxes", English.Plural(box));
        Assert.Equal("an apple", English.WithArticle("apple"));
        Assert.Equal("a box", English.WithArticle("box"));
    }

    [Fact]
    public void Theme_unit_makes_every_word_of_the_pack_new()
    {
        using var dir = Course();
        var (course, _) = ContentLoader.Load(dir.Path);

        var unit = DrillGenerator.ThemeUnit(course, course.Packs[0], Level.A1);
        var result = new DrillGenerator(course, Saldo()).GenerateTheme(course.Packs[0]);

        Assert.Contains("mjölk", unit.NewWords);
        Assert.Equal("theme-shop", result.File.Unit);
        Assert.Contains(result.File.Items, i => i.Words!.Contains("mjölk"));
    }

    [Fact]
    public void A_session_draws_from_authored_and_generated_items()
    {
        using var dir = Course(unitExtra: "exercises:\n  - { type: dictation, sv: \"Var finns osten?\" }\n  - { type: scene_click, lemma: ost }");
        dir.Write("generated/a1-01.yaml", DrillGenerator.ToYaml(Generate(dir).File));
        var (course, _) = ContentLoader.Load(dir.Path);

        var pool = Svenska.Core.Exercises.ExerciseSession.Pool(course, course.Units[0]);
        var session = Svenska.Core.Exercises.ExerciseSession.Pick(pool, new Random(1), size: 5);

        Assert.Equal(1 + course.Generated.Single(g => g.Unit == "a1-01").Items.Count, pool.Count);   // scene_click is not playable yet
        Assert.Equal(5, session.Distinct().Count());
        Assert.All(session, i => Assert.Contains(i, pool));
    }
}
