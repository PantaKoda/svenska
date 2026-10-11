using Svenska.Core.Content;
using Svenska.Core.Progress;

namespace Svenska.Tests;

public sealed class ProgressTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProgressStore _store;
    private readonly Course _course;
    private readonly ProgressTracker _progress;
    private DateTime _now = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private static ExerciseItem Dictation(string sv, params string[] words) =>
        new() { Type = "dictation", Sv = sv, Words = [.. words], Grammar = ["def_singular"] };

    public ProgressTests()
    {
        _store = new ProgressStore(Path.Combine(_dir.Path, "sub", "progress.db"));
        _course = new Course
        {
            Levels = { [Level.A1] = new LevelDef { Grammar = ["def_singular", "modal"] }, [Level.A2] = new LevelDef { Grammar = ["past"] } },
            Grammar =
            [
                new GrammarPoint { Id = "def_singular", Level = Level.A1 },
                new GrammarPoint { Id = "modal", Level = Level.A1 },
                new GrammarPoint { Id = "past", Level = Level.A2 },
            ],
            Packs =
            [
                new ThemePack
                {
                    Theme = "shop",
                    Title = "Shop",
                    Words =
                    [
                        new Word { Lemma = "ost", Pos = "noun", Gender = "en", Gloss = "cheese", Level = Level.A1 },
                        new Word { Lemma = "köpa", Pos = "verb", Gloss = "to buy; past: bought", Level = Level.A1 },
                        new Word { Lemma = "hink", Pos = "noun", Gender = "en", Gloss = "bucket", Level = Level.A1 },
                        new Word { Lemma = "medel", Pos = "noun", Gender = "ett", Gloss = "detergent", Level = Level.A2 },
                    ],
                },
            ],
            Units =
            [
                new Unit
                {
                    Id = "a1-01", Level = Level.A1, Title = "Shop", NewWords = ["ost", "köpa"], NewGrammar = ["def_singular"],
                    Exercises = [Dictation("Var finns osten?"), Dictation("Jag köper osten."), new ExerciseItem { Type = "free_write" }],
                },
            ],
            Generated =
            [
                new GeneratedFile { Unit = "a1-01", Items = [Dictation("Osten är där.", "ost")] },
                new GeneratedFile
                {
                    Unit = "theme-shop",
                    Items =
                    [
                        Dictation("Var är osten?", "ost"),
                        Dictation("Var är hinken?", "hink"),
                        new ExerciseItem { Type = "dictation", Sv = "Jag ska köpa osten.", Words = ["köpa", "ost"], Grammar = ["modal"] },
                        new ExerciseItem { Type = "dictation", Sv = "Jag köpte medlet.", Words = ["köpa", "medel"], Grammar = ["past"] },
                    ],
                },
            ],
        };
        _progress = new ProgressTracker(_course, _store, new LeitnerScheduler(), () => _now);
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    private Unit Unit => _course.Units[0];
    private ThemePack Pack => _course.Packs[0];

    [Fact]
    public void Leitner_moves_up_one_box_when_right_and_back_to_the_first_when_wrong()
    {
        var leitner = new LeitnerScheduler();
        var card = leitner.New(_now, dueNow: false);
        Assert.Equal((1, _now.AddDays(1)), (card.Box, card.Due));

        card = leitner.Review(card, true, _now);
        Assert.Equal((2, _now.AddDays(2)), (card.Box, card.Due));
        card = leitner.Review(card, true, _now);
        card = leitner.Review(card, true, _now);
        card = leitner.Review(card, true, _now);
        card = leitner.Review(card, true, _now);
        Assert.Equal((5, _now.AddDays(21), 5, 0), (card.Box, card.Due, card.Reviews, card.Lapses));   // stays in the last box

        card = leitner.Review(card, false, _now);
        Assert.Equal((1, _now, 6, 1), (card.Box, card.Due, card.Reviews, card.Lapses));
    }

    [Fact]
    public void Unit_progress_counts_items_answered_right_at_least_once()
    {
        Assert.Equal((0, 3), _progress.UnitProgress(Unit));   // two authored + one generated; free_write is not playable

        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[1], correct: false);

        Assert.Equal((1, 3), _progress.UnitProgress(Unit));
    }

    [Fact]
    public void A_session_starts_with_the_items_not_yet_answered_right()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);

        var session = _progress.UnitSession(Unit, new Random(3));

        Assert.Equal(3, session.Count);
        Assert.Same(Unit.Exercises[0], session[^1]);
    }

    [Fact]
    public void Working_on_a_unit_puts_its_words_in_the_review_queue_for_tomorrow()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);
        Assert.Equal(0, _progress.DueCount());

        _now = _now.AddDays(1);
        var review = _progress.ReviewSession();

        Assert.Equal(2, review.Count);
        var ost = review.Single(r => r.Key == "word:ost").Item;
        Assert.Equal(("translate", "cheese", "en ost"), (ost.Type, ost.En, ost.Sv));
        var köpa = review.Single(r => r.Key == "word:köpa").Item;
        Assert.Equal(("to buy", "köpa"), (köpa.En, köpa.Sv));
        Assert.Contains("att köpa", köpa.Accept);
    }

    [Fact]
    public void A_missed_item_is_due_at_once_and_rests_longer_each_time_it_is_right()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[1], correct: false);

        var entry = Assert.Single(_progress.ReviewSession());
        Assert.Same(Unit.Exercises[1], entry.Item);

        _progress.RecordReviewAnswer(entry.Key, correct: true);
        Assert.Equal(0, _progress.DueCount());
        _now = _now.AddDays(2);
        Assert.Contains(_progress.ReviewSession(), r => r.Key == entry.Key);   // box 2: back after two days

        _progress.RecordReviewAnswer(entry.Key, correct: false);
        Assert.Contains(_progress.ReviewSession(), r => r.Key == entry.Key);   // wrong: due again now
    }

    [Fact]
    public void A_card_whose_item_left_the_content_is_dropped()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[1], correct: false);
        var changed = new ProgressTracker(new Course { Units = [new Unit { Id = "a1-01" }] }, _store, new LeitnerScheduler(), () => _now.AddDays(5));

        Assert.Empty(changed.ReviewSession());
        Assert.Equal(0, _store.CardCount());
    }

    [Fact]
    public void Nothing_is_known_before_the_first_unit_is_started()
    {
        Assert.Empty(_progress.KnownWords());
        Assert.Equal(Level.A1, _progress.CurrentLevel());
        Assert.Empty(_progress.ThemePool(Pack, includeNewWords: false));
    }

    [Fact]
    public void Theme_practice_uses_known_words_and_grammar_unless_new_words_are_asked_for()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);

        var known = _progress.ThemePool(Pack, includeNewWords: false).Select(i => i.Sv).ToList();
        var withNew = _progress.ThemePool(Pack, includeNewWords: true).Select(i => i.Sv).ToList();

        Assert.Equal(["Var är osten?"], known);                                             // hink is new, modal is not known
        Assert.Equal(["Var är osten?", "Var är hinken?", "Jag ska köpa osten."], withNew);  // all of A1, nothing of A2
    }

    [Fact]
    public void A_missed_theme_item_goes_to_review_too()
    {
        var item = _course.Generated[1].Items[1];
        _progress.RecordThemeAnswer(Pack, item, correct: false);

        Assert.Same(item, Assert.Single(_progress.ReviewSession()).Item);
    }

    [Fact]
    public void Progress_is_still_there_after_reopening_the_file()
    {
        _progress.RecordUnitAnswer(Unit, Unit.Exercises[0], correct: true);
        _store.Dispose();

        using var reopened = new ProgressStore(Path.Combine(_dir.Path, "sub", "progress.db"));
        var progress = new ProgressTracker(_course, reopened, new LeitnerScheduler(), () => _now);

        Assert.Equal((1, 3), progress.UnitProgress(Unit));
        Assert.Equal(2, reopened.CardCount());
    }

    [Fact]
    public void Item_keys_depend_on_the_content_of_the_item_not_its_position()
    {
        var a = ProgressTracker.ItemKey("a1-01", Dictation("Var finns osten?"));
        var b = ProgressTracker.ItemKey("a1-01", Dictation("Var finns osten?"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, ProgressTracker.ItemKey("a1-01", Dictation("Var finns mjölken?")));
        Assert.NotEqual(a, ProgressTracker.ItemKey("theme-shop", Dictation("Var finns osten?")));
    }
}
