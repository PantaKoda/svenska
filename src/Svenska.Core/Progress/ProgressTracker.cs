using System.Security.Cryptography;
using System.Text;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Generation;

namespace Svenska.Core.Progress;

/// <summary>A card that is due, with the exercise that tests it.</summary>
public sealed record ReviewEntry(string Key, ExerciseItem Item);

/// <summary>
/// What the learner has done: progress per unit, the words and grammar they know, and the
/// review queue (the words of the units they started, and the items they got wrong).
/// </summary>
public sealed class ProgressTracker(Course course, ProgressStore store, IScheduler scheduler, Func<DateTime> now)
{
    private const string WordPrefix = "word:";
    private const string PhrasePrefix = "phrase:";
    private Dictionary<string, ExerciseItem>? _itemsByKey;
    private readonly Dictionary<string, Word> _words = Vocabulary.WordsByLemma(course);

    public static string ThemeSource(ThemePack pack) => "theme-" + pack.Theme;

    /// <summary>Identifies an item by what it asks, so it survives reordering of the content.</summary>
    public static string ItemKey(string source, ExerciseItem item)
    {
        var text = string.Join("|", item.Type, item.Sv, item.Lemma, item.Form, item.Answer);
        return source + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    // ---- units ----

    /// <summary>How many of the unit's items were answered right at least once.</summary>
    public (int Done, int Total) UnitProgress(Unit unit)
    {
        var pool = ExerciseSession.Pool(course, unit);
        var right = store.RightKeys(unit.Id);
        return (pool.Count(i => right.Contains(ItemKey(unit.Id, i))), pool.Count);
    }

    public List<ExerciseItem> UnitSession(Unit unit, Random random, int size = ExerciseSession.DefaultSize)
    {
        var right = store.RightKeys(unit.Id);
        return ExerciseSession.Pick(ExerciseSession.Pool(course, unit), random, size, i => right.Contains(ItemKey(unit.Id, i)));
    }

    public void RecordUnitAnswer(Unit unit, ExerciseItem item, bool correct)
    {
        Record(unit.Id, item, correct);
        // The unit's words enter the review queue the first time the learner works on the unit.
        foreach (var key in unit.NewWords.Where(_words.ContainsKey).Select(w => WordPrefix + w)
                     .Concat(unit.Phrases.Select(p => PhrasePrefix + p.Sv)))
            if (store.GetCard(key) == null)
                store.SaveCard(key, scheduler.New(now(), dueNow: false));
    }

    // ---- final task ----

    private static string FinalSource(Unit unit) => unit.Id + "/final";

    public void RecordFinalAnswer(Unit unit, DialogueStep step, bool correct) =>
        store.RecordResult(FinalSource(unit), $"{FinalSource(unit)}:{step.Index}", correct, now());

    /// <summary>True when every reply of the unit's final task has been right at least once.</summary>
    public bool FinalTaskDone(Unit unit)
    {
        if (unit.FinalTask == null)
            return false;
        var right = store.RightKeys(FinalSource(unit));
        var steps = DialogueTask.Steps(unit.FinalTask);
        return steps.Count > 0 && steps.All(s => right.Contains($"{FinalSource(unit)}:{s.Index}"));
    }

    // ---- what the learner knows ----

    /// <summary>The units up to the last one the learner has worked on.</summary>
    private List<Unit> UnitsSoFar()
    {
        var started = store.StartedSources();
        var last = course.Units.FindLastIndex(u => started.Contains(u.Id) || started.Contains(FinalSource(u)));
        return course.Units.Take(last + 1).ToList();
    }

    public HashSet<string> KnownWords() =>
        UnitsSoFar().SelectMany(u => u.NewWords.Concat(u.Assumes?.Words ?? [])).ToHashSet();

    public HashSet<string> KnownGrammar() =>
        UnitsSoFar().SelectMany(u => u.NewGrammar.Concat(u.Assumes?.Grammar ?? [])).ToHashSet();

    /// <summary>The level of the last unit worked on; the lowest level before any work.</summary>
    public Level CurrentLevel() =>
        UnitsSoFar().LastOrDefault()?.Level ?? (course.Levels.Count > 0 ? course.Levels.Keys.Min() : Level.A1);

    // ---- theme practice ----

    /// <summary>
    /// The checked drills of a theme pack that the learner can do: by default only those
    /// made of known words and grammar; with <paramref name="includeNewWords"/> every
    /// drill up to the learner's level.
    /// </summary>
    public List<ExerciseItem> ThemePool(ThemePack pack, bool includeNewWords)
    {
        var items = course.Generated.Where(g => g.Unit == ThemeSource(pack)).SelectMany(g => g.Items)
            .Where(i => ExerciseText.Playable.Contains(i.Type));
        if (includeNewWords)
        {
            var level = CurrentLevel();
            var grammar = course.Grammar.Where(g => g.Level <= level).Select(g => g.Id).ToHashSet();
            return items.Where(i => i.Grammar.All(grammar.Contains)
                                    && (i.Words ?? []).All(w => _words.TryGetValue(w, out var word) && word.Level <= level)).ToList();
        }
        var knownWords = KnownWords();
        var knownGrammar = KnownGrammar();
        return items.Where(i => i.Grammar.All(knownGrammar.Contains) && (i.Words ?? []).All(knownWords.Contains)).ToList();
    }

    public List<ExerciseItem> ThemeSession(ThemePack pack, bool includeNewWords, Random random, int size = ExerciseSession.DefaultSize)
    {
        var right = store.RightKeys(ThemeSource(pack));
        return ExerciseSession.Pick(ThemePool(pack, includeNewWords), random, size, i => right.Contains(ItemKey(ThemeSource(pack), i)));
    }

    public void RecordThemeAnswer(ThemePack pack, ExerciseItem item, bool correct) => Record(ThemeSource(pack), item, correct);

    // ---- review ----

    public int DueCount() => DueEntries().Count;

    /// <summary>The cards to review now, the longest overdue first.</summary>
    public List<ReviewEntry> ReviewSession(int size = ExerciseSession.DefaultSize) => DueEntries().Take(size).ToList();

    public void RecordReviewAnswer(string key, bool correct)
    {
        if (store.GetCard(key) is { } state)
            store.SaveCard(key, scheduler.Review(state, correct, now()));
    }

    private List<ReviewEntry> DueEntries()
    {
        var entries = new List<ReviewEntry>();
        foreach (var (key, _) in store.DueCards(now()))
        {
            var item = key.StartsWith(WordPrefix)
                ? _words.TryGetValue(key[WordPrefix.Length..], out var word) ? WordItem(word) : null
                : key.StartsWith(PhrasePrefix)
                    ? course.Units.SelectMany(u => u.Phrases).FirstOrDefault(p => p.Sv == key[PhrasePrefix.Length..]) is { } phrase
                        ? new ExerciseItem { Type = ExerciseText.Translate, En = phrase.En, Sv = phrase.Sv, Accept = [.. phrase.Accept], Hint = "a phrase" }
                        : null
                    : ItemsByKey().GetValueOrDefault(key);
            if (item != null)
                entries.Add(new ReviewEntry(key, item));
            else
                store.DeleteCard(key); // the word or item is no longer in the content
        }
        return entries;
    }

    /// <summary>The review exercise for a word: its meaning is shown, the learner writes the word.</summary>
    private ExerciseItem WordItem(Word word)
    {
        var noun = word is { Pos: "noun", Gender: not null };
        return new ExerciseItem
        {
            Type = ExerciseText.Translate,
            En = English.Meaning(word),
            Sv = noun ? $"{word.Gender} {word.Lemma}" : word.Lemma,
            Hint = noun ? "noun, with en or ett" : word.Pos == "verb" ? "verb" : null,
            Accept = word.Pos == "verb" ? [$"att {word.Lemma}"] : [],
            Grammar = noun && course.Grammar.Any(g => g.Id == "en_ett") ? ["en_ett"] : [],
            Words = [word.Lemma],
        };
    }

    private Dictionary<string, ExerciseItem> ItemsByKey()
    {
        if (_itemsByKey != null)
            return _itemsByKey;
        _itemsByKey = [];
        foreach (var unit in course.Units)
            foreach (var item in unit.Exercises)
                _itemsByKey.TryAdd(ItemKey(unit.Id, item), item);
        foreach (var file in course.Generated)
            foreach (var item in file.Items)
                _itemsByKey.TryAdd(ItemKey(file.Unit, item), item);
        return _itemsByKey;
    }

    private void Record(string source, ExerciseItem item, bool correct)
    {
        var key = ItemKey(source, item);
        store.RecordResult(source, key, correct, now());
        if (correct)
            return;
        // A missed item goes into the review queue, due at once.
        var card = store.GetCard(key);
        store.SaveCard(key, card == null ? scheduler.New(now(), dueNow: true) : scheduler.Review(card, false, now()));
    }
}
