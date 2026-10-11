using Svenska.Core.Answers;
using Svenska.Core.Audio;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Scenes;
using Svenska.Validator;

namespace Svenska.Tests;

public class RichExerciseTests
{
    // ---- numbers ----

    [Theory]
    [InlineData("87", "87", true)]
    [InlineData("87 kr", "87", true)]
    [InlineData("15:30", "15.30", true)]
    [InlineData("031 12 34 56", "031-12 34 56", true)]
    [InlineData("78", "87", false)]
    [InlineData("åttiosju", "87", false)]
    [InlineData("", "87", false)]
    public void A_number_is_right_when_its_digits_are(string given, string expected, bool right)
    {
        var result = AnswerChecker.CheckNumber(given, expected);

        Assert.Equal(right, result.IsCorrect);
        if (!right)
            Assert.Equal($"It was {expected}.", result.Feedback);
    }

    // ---- scenes ----

    [Fact]
    public void Scene_hit_takes_the_smallest_thing_under_the_click_with_a_little_margin()
    {
        var map = new SceneMap(new Dictionary<string, SceneRect>
        {
            ["w-kassa"] = new(520, 196, 780, 420),
            ["w-kort"] = new(560, 318, 644, 372),
            ["w-agg"] = new(326, 315, 370, 373),
        });

        Assert.Equal("w-kort", map.Hit(600, 340));    // the card lies on the counter
        Assert.Equal("w-kassa", map.Hit(700, 400));
        Assert.Equal("w-agg", map.Hit(322, 344));     // just beside the egg
        Assert.Null(map.Hit(100, 100));
    }

    [Fact]
    public void Scene_ids_are_the_lemma_in_ascii_and_are_read_from_the_svg()
    {
        using var dir = new TempDir();
        var svg = dir.Write("scene.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <rect id="background" width="10" height="10"/>
              <g id="w-mjolk"><rect width="2" height="2"/></g>
              <circle id="w-agg" r="1"/>
            </svg>
            """);

        Assert.Equal("w-mjolk", SceneMap.IdFor("mjölk"));
        Assert.Equal("w-pase", SceneMap.IdFor("påse"));
        Assert.Equal("w-apple", SceneMap.IdFor("Äpple"));
        Assert.Equal(["w-agg", "w-mjolk"], SceneMap.WordIds(svg).Order());
    }

    // ---- dialogue ----

    private static ExerciseItem Task() => new()
    {
        Type = "dialogue",
        Turns =
        [
            new() { Speaker = "du", En = "Ask where the milk is.", Answer = "Var finns mjölken?", Accept = ["Var är mjölken?"] },
            new() { Speaker = "personal", Sv = "Mjölken finns där.", En = "The milk is there." },
            new() { Speaker = "kassör", Sv = "Det blir 45 kronor.", En = "That comes to 45 kronor." },
            new() { Speaker = "du", En = "Say thanks.", Options = ["Tack!", "Var?"], Answer = "Tack!" },
        ],
    };

    [Fact]
    public void A_dialogue_becomes_one_step_per_reply_with_what_is_heard_before_it()
    {
        var steps = DialogueTask.Steps(Task());

        Assert.Equal(2, steps.Count);
        Assert.Empty(steps[0].Heard);
        Assert.Equal(("translate", "Ask where the milk is.", "Var finns mjölken?"), (steps[0].Item.Type, steps[0].Item.En, steps[0].Item.Sv));
        Assert.Equal(["Var är mjölken?"], steps[0].Item.Accept);

        Assert.Equal(["Mjölken finns där.", "Det blir 45 kronor."], steps[1].Heard.Select(l => l.Sv));
        Assert.Equal(("listen_choose", "Tack!", 3), (steps[1].Item.Type, steps[1].Item.Answer, steps[1].Index));
        Assert.Equal("Tack!", ExerciseText.Expected(steps[1].Item));
    }

    // ---- validator ----

    private static TempDir Course(string exercises, string extra = "", string words = "", bool scene = true)
    {
        var dir = new TempDir();
        dir.Write("levels.yaml", "A1:\n  grammar: [present_tense]\n");
        dir.Write("grammar.yaml", "- { id: present_tense, level: A1, title: Present, card: x }\n");
        dir.Write("tags.yaml", "nouns: [food]\nverbs: []\nadjectives: []\n");
        dir.Write("frames.yaml", "[]\n");
        if (scene)
            dir.Write("svg/shop.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><g id=\"w-ost\"/><g id=\"w-mjolk\"/></svg>");
        dir.Write("themes/shop.yaml", $$"""
            theme: shop
            title: Shop
            scene: svg/shop.svg
            words:
              - { lemma: mjölk, pos: noun, gender: en, level: A1, gloss: milk, svg: w-mjolk }
              - { lemma: ost, pos: noun, gender: en, level: A1, gloss: cheese, svg: w-ost }
              - { lemma: kosta, pos: verb, level: A1, gloss: to cost }
              - { lemma: krona, pos: noun, gender: en, level: A1, gloss: krona }
              - { lemma: finnas, pos: verb, level: A1, gloss: to be }
              - { lemma: var, pos: adv, level: A1, gloss: where }
              - { lemma: den, pos: pron, level: A1, gloss: it }
            {{words}}
            """);
        dir.Write("units/a1/01.yaml", $$"""
            id: a1-01
            level: A1
            theme: shop
            title: Shop
            task: Buy.
            can_do: I can buy.
            new_words: [mjölk, ost, kosta, krona, finnas, var, den]
            new_grammar: [present_tense]
            exercises:
              - { type: dictation, sv: "Var finns mjölken?" }
              - { type: dictation, sv: "Osten kostar 45 kronor. Den finns." }
            {{exercises}}
            {{extra}}
            """);
        return dir;
    }

    private static List<ContentIssue> Errors(TempDir dir)
    {
        var saldo = FakeSaldo.Shop().Add("bli", "vb", null, "bli=inf aktiv", "blir=pres ind aktiv");
        dir.WriteGenerated(saldo);
        var (course, issues) = ContentLoader.Load(dir.Path);
        issues.AddRange(CourseValidator.Validate(course, saldo, dir.Path));
        return issues.Where(i => i.Severity == Severity.Error).ToList();
    }

    private const string Rich = """
          - { type: numbers, sv: "Osten kostar 45 kronor.", hint: "the price", answer: "45" }
          - { type: listen_choose, sv: "Var finns mjölken?", options: ["Where is the milk?", "What does it cost?"], answer: "Where is the milk?" }
          - { type: scene_click, lemma: ost }
        """;

    private const string FinalTask = """
        final_task:
          type: dialogue
          turns:
            - { speaker: kassör, sv: "Osten kostar 45 kronor.", en: "The cheese costs 45 kronor." }
            - { speaker: du, en: "Ask where the milk is.", answer: "Var finns mjölken?" }
        """;

    [Fact]
    public void The_new_exercise_types_and_a_final_task_are_valid()
    {
        using var dir = Course(Rich, FinalTask);
        Assert.Empty(Errors(dir));
    }

    [Fact]
    public void Items_must_have_what_their_type_needs()
    {
        using var numbers = Course(Rich.Replace("answer: \"45\"", "answer: \"fyrtiofem\""));
        Assert.Contains(Errors(numbers), e => e.Message.Contains("answer must be the number in digits"));

        using var choose = Course(Rich.Replace("answer: \"Where is the milk?\"", "answer: \"Where?\""));
        Assert.Contains(Errors(choose), e => e.Message.Contains("'Where?' is not one of the options"));

        using var noSvg = Course(Rich.Replace("lemma: ost", "lemma: kosta"));
        Assert.Contains(Errors(noSvg), e => e.Message.Contains("scene_click kosta: the word has no svg id"));
    }

    [Fact]
    public void A_scene_click_word_must_be_drawn_in_the_scene()
    {
        using var notDrawn = Course(Rich, scene: false);
        Assert.Contains(Errors(notDrawn), e => e.Message.Contains("the scene of pack 'shop' is not drawn"));

        using var missing = Course(Rich, words: "  - { lemma: bröd, pos: noun, gender: ett, level: A1, gloss: bread, svg: w-brod }");
        Assert.Contains(Errors(missing), e => e.Message.Contains("'bröd': the scene svg/shop.svg has no element with id 'w-brod'"));
    }

    [Fact]
    public void Rule_3_an_unknown_word_that_is_heard_needs_a_gloss_and_must_be_rare()
    {
        using var noGloss = Course(Rich.Replace("Osten kostar 45 kronor.", "Det blir 45 kronor."));
        Assert.Contains(Errors(noGloss), e => e.Rule == 3 && e.Message.Contains("unknown words without a gloss: blir"));

        // With the gloss the word is allowed, but one unknown word in so little text is too much.
        using var glossed = Course(Rich.Replace("Osten kostar 45 kronor.", "Det blir 45 kronor."), "glosses: { bli: \"becomes\" }");
        var errors = Errors(glossed);
        Assert.DoesNotContain(errors, e => e.Message.Contains("without a gloss"));
        Assert.Contains(errors, e => e.Rule == 3 && e.Message.Contains("at most 5 %"));
    }

    [Fact]
    public void Rule_2_the_learners_replies_in_the_final_task_use_known_words_only()
    {
        using var typed = Course("", FinalTask.Replace("answer: \"Var finns mjölken?\"", "answer: \"Var finns bananen?\""));
        Assert.Contains(Errors(typed), e => e.Rule == 2 && e.Message.Contains("bananen"));

        using var chosen = Course("", FinalTask.Replace("answer: \"Var finns mjölken?\"",
            "options: [\"Var finns mjölken?\", \"Var finns bananen?\"], answer: \"Var finns mjölken?\""));
        Assert.Contains(Errors(chosen), e => e.Rule == 2 && e.Message.Contains("bananen"));
    }

    [Fact]
    public void A_final_task_must_end_with_the_learner_and_have_complete_turns()
    {
        using var ends = Course("", FinalTask + "\n    - { speaker: kassör, sv: \"Den finns.\", en: \"It exists.\" }");
        Assert.Contains(Errors(ends), e => e.Message.Contains("the last one must be the learner's"));

        using var incomplete = Course("", FinalTask.Replace("en: \"Ask where the milk is.\", ", ""));
        Assert.Contains(Errors(incomplete), e => e.Message.Contains("needs en (what to say) and answer"));
    }

    [Fact]
    public void Everything_that_is_heard_gets_audio_in_both_speeds()
    {
        using var dir = Course(Rich, FinalTask);
        var (course, _) = ContentLoader.Load(dir.Path);
        var lines = AudioFiles.Lines(course);

        Assert.Contains(("Osten kostar 45 kronor.", true), lines);    // numbers, and the cashier in the final task
        Assert.Contains(("Var finns mjölken?", false), lines);        // dictation and listen_choose
        Assert.Contains(("ost", false), lines);                       // the word of a scene_click
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }
}
