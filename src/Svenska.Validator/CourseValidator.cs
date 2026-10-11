using Svenska.Core.Audio;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Saldo;
using Svenska.Core.Text;

namespace Svenska.Validator;

/// <summary>
/// Checks a loaded course against the rules of AGENTS.md. Done so far: rules 1–6 and 9–11.
/// Rules 7 and 8 (frame slots, generated files up to date) come with the drill generator in M2.
/// </summary>
public sealed class CourseValidator
{
    private const int MaxCardLines = 15;
    private const int MaxNewWords = 25;
    private const int MaxNewGrammar = 2;

    private readonly Course _course;
    private readonly ISaldoLexicon? _saldo;
    private readonly string? _contentDir;
    private readonly Vocabulary? _vocabulary;
    private readonly Dictionary<string, Word> _words;
    private readonly Dictionary<string, GrammarPoint> _grammar;
    private readonly List<ContentIssue> _issues = [];

    private CourseValidator(Course course, ISaldoLexicon? saldo, string? contentDir)
    {
        _course = course;
        _saldo = saldo;
        _contentDir = contentDir;
        _vocabulary = saldo == null ? null : new Vocabulary(course, saldo);
        _words = Vocabulary.WordsByLemma(course);
        _grammar = course.Grammar.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <param name="saldo">Without it the word checks (rules 2, 3, 9, 10) are skipped.</param>
    /// <param name="contentDir">Without it the audio files are not checked (rule 11).</param>
    public static List<ContentIssue> Validate(Course course, ISaldoLexicon? saldo = null, string? contentDir = null)
    {
        var validator = new CourseValidator(course, saldo, contentDir);
        validator.CheckGrammar();
        validator.CheckTags();
        validator.CheckFrameIds();
        validator.CheckPacks();
        validator.CheckUnits();
        validator.CheckReuse();
        validator.CheckAudio();
        return validator._issues;
    }

    // ---- base files ----

    private void CheckGrammar()
    {
        const string file = "grammar.yaml";
        foreach (var id in Duplicates(_course.Grammar.Select(g => g.Id)))
            Error(file, $"grammar id '{id}' is defined twice");

        foreach (var (level, def) in _course.Levels)
        {
            foreach (var id in Duplicates(def.Grammar))
                Error("levels.yaml", $"{level} lists '{id}' twice");
            foreach (var id in def.Grammar)
            {
                if (!_grammar.TryGetValue(id, out var point))
                    Error("levels.yaml", $"{level} lists '{id}', which is not in grammar.yaml");
                else if (point.Level != level)
                    Error("levels.yaml", $"{level} lists '{id}', but grammar.yaml says {point.Level}", 4);
            }
        }

        var noCard = new List<string>();
        foreach (var point in _course.Grammar)
        {
            if (string.IsNullOrWhiteSpace(point.Id) || string.IsNullOrWhiteSpace(point.Title))
                Error(file, $"grammar point '{point.Id}' needs an id and a title");
            if (!_course.Levels.TryGetValue(point.Level, out var def) || !def.Grammar.Contains(point.Id))
                Error(file, $"'{point.Id}' is not listed under {point.Level} in levels.yaml", 4);

            foreach (var required in point.Requires)
            {
                if (!_grammar.TryGetValue(required, out var other))
                    Error(file, $"'{point.Id}' requires unknown grammar point '{required}'");
                else if (other.Level > point.Level)
                    Error(file, $"'{point.Id}' ({point.Level}) requires '{required}' ({other.Level})", 4);
            }

            if (string.IsNullOrWhiteSpace(point.Card))
                noCard.Add(point.Id);
            else if (point.Card.TrimEnd().Split('\n').Length > MaxCardLines)
                Warning(file, $"card of '{point.Id}' is longer than {MaxCardLines} lines");
        }
        if (noCard.Count > 0)
            Warning(file, $"{noCard.Count} grammar points have no card yet: {string.Join(", ", noCard)}");
    }

    private void CheckTags()
    {
        foreach (var tag in Duplicates(_course.Tags.All()))
            Error("tags.yaml", $"tag '{tag}' is listed twice");
    }

    private void CheckFrameIds()
    {
        var ids = _course.Frames.Select(f => f.Id).Concat(_course.Packs.SelectMany(p => p.Frames).Select(f => f.Id));
        foreach (var id in Duplicates(ids))
            Error("frames", $"frame id '{id}' is used twice");
    }

    // ---- theme packs ----

    private void CheckPacks()
    {
        foreach (var theme in Duplicates(_course.Packs.Select(p => p.Theme)))
            Error("themes", $"theme '{theme}' is defined in two packs");

        foreach (var pack in _course.Packs)
        {
            var file = $"themes/{pack.Theme}.yaml";
            if (string.IsNullOrWhiteSpace(pack.Theme) || string.IsNullOrWhiteSpace(pack.Title))
                Error(file, "a pack needs a theme and a title");

            foreach (var word in pack.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Lemma) || string.IsNullOrWhiteSpace(word.Gloss))
                    Error(file, $"word '{word.Lemma}' needs a lemma and a gloss");
                if (!WordForms.PartsOfSpeech.Contains(word.Pos))
                    Error(file, $"'{word.Lemma}': unknown pos '{word.Pos}' (use {string.Join(", ", WordForms.PartsOfSpeech)})");
                if (word.Pos == "noun" && word.Gender is not ("en" or "ett"))
                    Error(file, $"noun '{word.Lemma}' needs gender: en or ett");

                var allowed = word.Pos switch
                {
                    "noun" => _course.Tags.Nouns,
                    "verb" => _course.Tags.Verbs,
                    "adj" => _course.Tags.Adjectives,
                    _ => [],
                };
                foreach (var tag in word.Tags.Where(t => !allowed.Contains(t)))
                    Error(file, $"'{word.Lemma}': tag '{tag}' is not a {word.Pos} tag in tags.yaml", 7);
            }
        }

        foreach (var lemma in Duplicates(_course.Packs.SelectMany(p => p.Words).Select(w => w.Lemma)))
        {
            var packs = _course.Packs.Where(p => p.Words.Any(w => w.Lemma == lemma)).Select(p => p.Theme).Distinct();
            Error("themes", $"word '{lemma}' is listed more than once ({string.Join(", ", packs)})", 6);
        }
    }

    // ---- units ----

    private void CheckUnits()
    {
        foreach (var id in Duplicates(_course.Units.Select(u => u.Id)))
            Error("units", $"unit id '{id}' is used twice");
        foreach (var generated in _course.Generated.Where(g => _course.Units.All(u => u.Id != g.Unit)))
            Error($"generated/{generated.Unit}", "there is no unit with this id");

        var introduced = new HashSet<string>();
        var knownGrammar = new HashSet<string>();
        foreach (var unit in _course.Units)
        {
            var file = $"units/{unit.Id}";
            if (string.IsNullOrWhiteSpace(unit.Id) || string.IsNullOrWhiteSpace(unit.Title)
                || string.IsNullOrWhiteSpace(unit.Task) || string.IsNullOrWhiteSpace(unit.CanDo))
                Error(file, "a unit needs id, title, task and can_do");
            if (_course.Packs.All(p => p.Theme != unit.Theme))
                Error(file, $"theme '{unit.Theme}' has no pack");

            if (unit.Assumes is { } assumes)
                Warning(file, $"assumes {assumes.Words.Count} words and {assumes.Grammar.Count} grammar points " +
                              "from units that are not written yet");

            CheckUnitWords(unit, file, introduced);
            CheckUnitGrammar(unit, file, knownGrammar);

            var items = ItemsOf(unit).ToList();
            foreach (var item in items)
                CheckItem(unit, file, item, knownGrammar);
            CheckWordsAreKnown(unit, file, items);
            CheckNewWordsAreUsed(unit, file, items);

            if (unit.FinalTask == null)
                Warning(file, "has no final_task yet");
        }
    }

    private void CheckUnitWords(Unit unit, string file, HashSet<string> introduced)
    {
        if (unit.NewWords.Count > MaxNewWords)
            Error(file, $"{unit.NewWords.Count} new words (at most {MaxNewWords})", 5);

        foreach (var lemma in unit.NewWords.Concat(unit.Assumes?.Words ?? []))
        {
            if (!_words.TryGetValue(lemma, out var word))
                Error(file, $"word '{lemma}' is in no theme pack", 6);
            else if (word.Level > unit.Level)
                Error(file, $"word '{lemma}' is {word.Level} in its pack, above this {unit.Level} unit", 5);
            if (!introduced.Add(lemma))
                Error(file, $"word '{lemma}' is already introduced (listed twice or in an earlier unit)");
        }
    }

    private void CheckUnitGrammar(Unit unit, string file, HashSet<string> knownGrammar)
    {
        if (unit.NewGrammar.Count > MaxNewGrammar)
            Error(file, $"{unit.NewGrammar.Count} new grammar points (at most {MaxNewGrammar})", 5);

        foreach (var id in (unit.Assumes?.Grammar ?? []).Concat(unit.NewGrammar))
        {
            if (!_grammar.TryGetValue(id, out var point))
            {
                Error(file, $"grammar point '{id}' is not in grammar.yaml");
                continue;
            }
            if (point.Level > unit.Level)
                Error(file, $"grammar '{id}' is {point.Level}, above this {unit.Level} unit", 4);
            knownGrammar.Add(id);
        }
        // A point may be introduced together with what it requires, so check after adding all.
        foreach (var id in unit.NewGrammar.Where(_grammar.ContainsKey))
            foreach (var required in _grammar[id].Requires.Where(r => !knownGrammar.Contains(r)))
                Error(file, $"grammar '{id}' requires '{required}', which is not introduced yet", 4);
    }

    private IEnumerable<ExerciseItem> ItemsOf(Unit unit)
    {
        var items = unit.Exercises.AsEnumerable();
        if (unit.FinalTask != null)
            items = items.Append(unit.FinalTask).Concat(unit.FinalTask.Turns ?? []);
        return items.Concat(_course.Generated.Where(g => g.Unit == unit.Id).SelectMany(g => g.Items));
    }

    /// <summary>Rule 1 (the item has what its type needs) and rule 4 (grammar tags).</summary>
    private void CheckItem(Unit unit, string file, ExerciseItem item, HashSet<string> knownGrammar)
    {
        var name = Describe(item);
        foreach (var id in item.Grammar)
        {
            if (!_grammar.ContainsKey(id))
                Error(file, $"{name}: grammar tag '{id}' is not in grammar.yaml");
            else if (!knownGrammar.Contains(id))
                Error(file, $"{name}: grammar '{id}' is used before it is introduced", 4);
        }

        switch (item.Type)
        {
            case ExerciseText.Dictation:
                if (string.IsNullOrWhiteSpace(item.Sv))
                    Error(file, $"{name}: needs sv");
                break;

            case ExerciseText.WordOrder:
                if (Tokenizer.Tokens(item.Sv ?? "").Count < 3)
                    Error(file, $"{name}: needs sv with at least three words");
                if (string.IsNullOrWhiteSpace(item.En))
                    Error(file, $"{name}: needs en, the sentence to build");
                break;

            case ExerciseText.Cloze:
                if ((item.Sv ?? "").Split(ExerciseText.Gap).Length != 2)
                    Error(file, $"{name}: sv needs exactly one gap ({ExerciseText.Gap})");
                if (string.IsNullOrWhiteSpace(item.Answer))
                    Error(file, $"{name}: needs answer");
                break;

            case ExerciseText.Inflect:
                CheckInflectItem(file, item, name);
                break;

            default:
                if (!ExerciseText.AllTypes.Contains(item.Type))
                    Error(file, $"{name}: unknown exercise type '{item.Type}'");
                break;
        }
    }

    private void CheckInflectItem(string file, ExerciseItem item, string name)
    {
        if (string.IsNullOrWhiteSpace(item.Lemma) || string.IsNullOrWhiteSpace(item.Form)
            || string.IsNullOrWhiteSpace(item.Answer))
        {
            Error(file, $"{name}: needs lemma, form and answer");
            return;
        }
        if (!_words.TryGetValue(item.Lemma, out var word))
        {
            Error(file, $"{name}: '{item.Lemma}' is in no theme pack", 6);
            return;
        }
        if (!WordForms.IsValid(item.Form, word.Pos))
        {
            Error(file, $"{name}: '{item.Form}' is not a form of a {word.Pos}", 7);
            return;
        }
        if (_saldo == null)
            return;

        var forms = WordForms.Inflect(word, item.Form, _saldo);
        if (forms.Count == 0)
            Error(file, $"{name}: SALDO has no {WordForms.Label(item.Form)} form of '{item.Lemma}'; add forms: to the pack");
        else if (!forms.Contains(item.Answer))
            Error(file, $"{name}: answer '{item.Answer}' is not the {WordForms.Label(item.Form)} form ({string.Join(" / ", forms)})");
    }

    /// <summary>Rules 2 and 3: only known words in what the learner writes and hears.</summary>
    private void CheckWordsAreKnown(Unit unit, string file, List<ExerciseItem> items)
    {
        if (_vocabulary == null)
            return;
        var known = _vocabulary.KnownWords(unit);

        foreach (var item in items.Where(i => ExerciseText.Playable.Contains(i.Type)))
        {
            var texts = new[] { ExerciseText.FullSentence(item), item.Hint ?? "" }.Concat(item.Accept);
            var unknown = texts.SelectMany(t => _vocabulary.UnknownTokens(t, known, unit.Names)).Distinct().ToList();
            if (unknown.Count > 0)
                Error(file, $"{Describe(item)}: not known yet: {string.Join(", ", unknown)}", 2);
        }

        // There is no gloss field yet (it comes with the listening exercises in M5), so a
        // listening text cannot contain any unknown word.
        foreach (var line in unit.ModelDialogue)
        {
            var unknown = _vocabulary.UnknownTokens(line.Sv, known, unit.Names);
            if (unknown.Count > 0)
                Error(file, $"dialogue \"{line.Sv}\": unknown words without a gloss: {string.Join(", ", unknown)}", 3);
        }
    }

    /// <summary>Rule 9: every new word is practised in its own unit.</summary>
    private void CheckNewWordsAreUsed(Unit unit, string file, List<ExerciseItem> items)
    {
        if (_vocabulary == null)
            return;
        var used = LemmasUsed(items);
        var unused = unit.NewWords.Where(w => !used.Contains(w)).ToList();
        if (unused.Count > 0)
            Error(file, $"new words never used in an exercise: {string.Join(", ", unused)}", 9);
    }

    /// <summary>Rule 10: a word introduced three or more units ago should have come back since.</summary>
    private void CheckReuse()
    {
        if (_vocabulary == null)
            return;
        for (var i = 0; i + 3 < _course.Units.Count; i++)
        {
            var later = LemmasUsed(_course.Units.Skip(i + 1).SelectMany(ItemsOf));
            var forgotten = _course.Units[i].NewWords.Where(w => !later.Contains(w)).ToList();
            if (forgotten.Count > 0)
                Warning($"units/{_course.Units[i].Id}", $"never reused in later units: {string.Join(", ", forgotten)}", 10);
        }
    }

    private HashSet<string> LemmasUsed(IEnumerable<ExerciseItem> items)
    {
        var used = new HashSet<string>();
        foreach (var item in items)
        {
            if (item.Lemma != null)
                used.Add(item.Lemma);
            foreach (var token in Tokenizer.Tokens(ExerciseText.FullSentence(item)))
                used.UnionWith(_vocabulary!.LemmasOf(token));
        }
        return used;
    }

    /// <summary>Rule 11.</summary>
    private void CheckAudio()
    {
        if (_contentDir == null)
            return;
        var missing = AudioFiles.Lines(_course)
            .Count(line => !File.Exists(Path.Combine(_contentDir, "audio", AudioFiles.FileName(line.Text, line.Slow))));
        if (missing > 0)
            Warning("audio", $"{missing} audio files are missing (run tools/gen_audio.py)", 11);
    }

    private static string Describe(ExerciseItem item) =>
        item.Type == ExerciseText.Inflect
            ? $"inflect {item.Lemma}.{item.Form}"
            : $"{item.Type} \"{item.Sv}\"";

    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key);

    private void Error(string file, string message, int rule = 1) =>
        _issues.Add(new ContentIssue(Severity.Error, file, message, rule));

    private void Warning(string file, string message, int rule = 0) =>
        _issues.Add(new ContentIssue(Severity.Warning, file, message, rule));
}
