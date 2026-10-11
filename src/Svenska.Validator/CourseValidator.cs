using Svenska.Core.Audio;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Generation;
using Svenska.Core.Saldo;
using Svenska.Core.Scenes;
using Svenska.Core.Text;

namespace Svenska.Validator;

/// <summary>
/// Checks a loaded course against the rules of AGENTS.md. All eleven rules are checked;
/// the word rules (2, 3, 8, 9, 10) need saldo.db.
/// </summary>
public sealed class CourseValidator
{
    private const int MaxCardLines = 15;
    private const int MaxCardWidth = 66;   // what fits in the reference panel
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
        validator.CheckFrames();
        validator.CheckPacks();
        validator.CheckUnits();
        validator.CheckGenerated();
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
            else
            {
                var lines = point.Card.TrimEnd().Split('\n');
                if (lines.Length > MaxCardLines)
                    Warning(file, $"card of '{point.Id}' is longer than {MaxCardLines} lines");
                if (lines.Any(l => l.TrimEnd().Length > MaxCardWidth))
                    Warning(file, $"card of '{point.Id}' has lines wider than {MaxCardWidth} characters");
            }
        }
        if (noCard.Count > 0)
            Warning(file, $"{noCard.Count} grammar points have no card yet: {string.Join(", ", noCard)}");
    }

    private void CheckTags()
    {
        foreach (var tag in Duplicates(_course.Tags.All()))
            Error("tags.yaml", $"tag '{tag}' is listed twice");
    }

    // ---- frames (rule 7, and rule 4 for their grammar) ----

    private void CheckFrames()
    {
        var all = _course.Frames.Select(f => (File: "frames.yaml", Frame: f))
            .Concat(_course.Packs.SelectMany(p => p.Frames.Select(f => (File: $"themes/{p.Theme}.yaml", Frame: f))))
            .ToList();
        foreach (var id in Duplicates(all.Select(f => f.Frame.Id)))
            Error("frames", $"frame id '{id}' is used twice");

        var packWords = _course.Packs.SelectMany(p => p.Words).ToList();
        foreach (var (file, frame) in all)
        {
            var name = $"frame {frame.Id}";
            if (string.IsNullOrWhiteSpace(frame.Id) || string.IsNullOrWhiteSpace(frame.Sv) || string.IsNullOrWhiteSpace(frame.En))
                Error(file, $"{name}: needs id, sv and en");

            foreach (var id in frame.Grammar)
            {
                if (!_grammar.TryGetValue(id, out var point))
                    Error(file, $"{name}: grammar tag '{id}' is not in grammar.yaml");
                else if (point.Level > frame.Level)
                    Error(file, $"{name}: grammar '{id}' is {point.Level}, above the frame's level {frame.Level}", 4);
            }

            FrameTemplate template;
            List<EnglishRef> english;
            try
            {
                template = FrameTemplate.Parse(frame.Sv);
                english = FrameTemplate.EnglishRefs(frame.En);
            }
            catch (FormatException e)
            {
                Error(file, $"{name}: {e.Message}", 7);
                continue;
            }
            if (template.Slots.Count == 0)
                Error(file, $"{name}: has no slot", 7);

            foreach (var slot in template.Slots)
            {
                var text = $"{name}, slot {slot.Number}";
                if (slot.Pos == null)
                {
                    Error(file, $"{text}: unknown form '{slot.Form}' (use {string.Join(", ", WordForms.Names)})", 7);
                    continue;
                }
                if (slot.AgreesWith is { } target
                    && (target < 1 || target > template.Slots.Count || template.Slots[target - 1].Pos != "noun"))
                    Error(file, $"{text}: ~{target} must point to a noun slot", 7);

                var allowed = slot.Pos switch { "noun" => _course.Tags.Nouns, "verb" => _course.Tags.Verbs, _ => _course.Tags.Adjectives };
                foreach (var tag in slot.Tags.Where(t => !allowed.Contains(t)))
                    Error(file, $"{text}: '{tag}' is not a {slot.Pos} tag in tags.yaml", 7);
                if (!packWords.Any(w => w.Pos == slot.Pos && w.Tags.Intersect(slot.Tags).Any()))
                    Error(file, $"{text}: no word in any pack can fill it", 7);
                if (slot.Grammar != null && !frame.Grammar.Contains(slot.Grammar))
                    Error(file, $"{text}: uses grammar '{slot.Grammar}', which the frame does not list", 4);
            }

            foreach (var reference in english)
            {
                if (reference.Slot < 1 || reference.Slot > template.Slots.Count)
                    Error(file, $"{name}: en refers to slot {reference.Slot}, which does not exist", 7);
                else if (reference.Modifier is "pp" or "past" && template.Slots[reference.Slot - 1].Pos != "verb")
                    Error(file, $"{name}: en uses .{reference.Modifier} on slot {reference.Slot}, which is not a verb", 7);
                else if (reference.Modifier == "a" && template.Slots[reference.Slot - 1].Pos != "noun")
                    Error(file, $"{name}: en uses .a on slot {reference.Slot}, which is not a noun", 7);
            }
        }
    }

    /// <summary>Rule 8: the generated files are what the generator makes from the content now.</summary>
    private void CheckGenerated()
    {
        CheckThemeFiles();
        foreach (var unit in _course.Units)
        {
            var file = $"generated/{unit.Id}.yaml";
            var onDisk = _course.Generated.Any(g => g.Unit == unit.Id);
            if (unit.Core == null)
            {
                if (onDisk)
                    Error(file, "the unit has no core section; run the generator to remove the file", 8);
                continue;
            }
            foreach (var template in unit.Core.Templates.Where(t => !DrillGenerator.Templates.Contains(t)))
                Error($"units/{unit.Id}", $"core: unknown template '{template}' (use {string.Join(", ", DrillGenerator.Templates)})");

            if (_saldo == null || _contentDir == null)
                continue;
            var path = Path.Combine(_contentDir, "generated", unit.Id + ".yaml");
            var expected = DrillGenerator.ToYaml(new DrillGenerator(_course, _saldo).Generate(unit).File);
            if (!File.Exists(path))
                Error(file, "is missing; run the generator", 8);
            else if (File.ReadAllText(path).ReplaceLineEndings("\n") != expected)
                Error(file, "is out of date with its inputs; run the generator", 8);
        }
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

        foreach (var pack in _course.Packs)
            CheckScene(pack);

        foreach (var lemma in Duplicates(_course.Packs.SelectMany(p => p.Words).Select(w => w.Lemma)))
        {
            var packs = _course.Packs.Where(p => p.Words.Any(w => w.Lemma == lemma)).Select(p => p.Theme).Distinct();
            Error("themes", $"word '{lemma}' is listed more than once ({string.Join(", ", packs)})", 6);
        }
    }

    /// <summary>The word ids of a pack's drawn scene, or null if it is not drawn (or not checked).</summary>
    private HashSet<string>? SceneIds(ThemePack? pack)
    {
        if (_contentDir == null || pack?.Scene == null)
            return null;
        var path = Path.Combine(_contentDir, pack.Scene);
        if (!File.Exists(path))
            return null;
        try
        {
            return SceneMap.WordIds(path);
        }
        catch (System.Xml.XmlException)
        {
            return null; // reported by CheckScene
        }
    }

    /// <summary>A word's `svg` id must be an element of the pack's scene, once the scene is drawn.</summary>
    private void CheckScene(ThemePack pack)
    {
        var file = $"themes/{pack.Theme}.yaml";
        var withSvg = pack.Words.Where(w => w.Svg != null).ToList();
        if (_contentDir == null || (pack.Scene == null && withSvg.Count == 0))
            return;
        if (pack.Scene == null)
        {
            Error(file, "words have svg ids but the pack has no scene");
            return;
        }
        var path = Path.Combine(_contentDir, pack.Scene);
        if (!File.Exists(path))
        {
            Warning(file, $"the scene {pack.Scene} is not drawn yet; its words cannot be used in scene_click");
            return;
        }
        HashSet<string> ids;
        try
        {
            ids = SceneMap.WordIds(path);
        }
        catch (System.Xml.XmlException e)
        {
            Error(pack.Scene, $"is not valid SVG: {e.Message}");
            return;
        }
        foreach (var word in withSvg.Where(w => !ids.Contains(w.Svg!)))
            Error(file, $"'{word.Lemma}': the scene {pack.Scene} has no element with id '{word.Svg}'");
        var unused = ids.Except(withSvg.Select(w => w.Svg!)).Order().ToList();
        if (unused.Count > 0)
            Warning(pack.Scene, $"elements that no word of the pack points to: {string.Join(", ", unused)}");
    }

    // ---- units ----

    private void CheckUnits()
    {
        foreach (var id in Duplicates(_course.Units.Select(u => u.Id)))
            Error("units", $"unit id '{id}' is used twice");
        foreach (var generated in _course.Generated.Where(g => _course.Units.All(u => u.Id != g.Unit)
                     && _course.Packs.All(p => DrillGenerator.ThemePrefix + p.Theme != g.Unit)))
            Error($"generated/{generated.Unit}", "there is no unit or theme pack with this id");

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

            CheckFinalTask(unit, file);
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

    /// <summary>The unit's authored and generated items, and the learner's replies in the final task.</summary>
    private IEnumerable<ExerciseItem> ItemsOf(Unit unit)
    {
        var items = unit.Exercises.AsEnumerable();
        if (unit.FinalTask is { Type: ExerciseText.Dialogue } task)
            items = items.Concat(DialogueTask.Steps(task).Select(s => s.Item));
        return items.Concat(_course.Generated.Where(g => g.Unit == unit.Id).SelectMany(g => g.Items));
    }

    private void CheckFinalTask(Unit unit, string file)
    {
        if (unit.FinalTask is not { } task)
            return;
        if (task.Type != ExerciseText.Dialogue)
        {
            Error(file, $"final_task: type '{task.Type}' is not supported (use dialogue)");
            return;
        }
        var turns = task.Turns ?? [];
        if (turns.Count == 0 || !DialogueTask.IsLearnerTurn(turns[^1]))
            Error(file, $"final_task: needs turns, and the last one must be the learner's (speaker: {DialogueTask.Learner})");

        foreach (var turn in turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Speaker))
                Error(file, "final_task: every turn needs a speaker");
            else if (!DialogueTask.IsLearnerTurn(turn))
            {
                if (string.IsNullOrWhiteSpace(turn.Sv) || string.IsNullOrWhiteSpace(turn.En))
                    Error(file, $"final_task: the turn of {turn.Speaker} needs sv and en");
            }
            else if (string.IsNullOrWhiteSpace(turn.En) || string.IsNullOrWhiteSpace(turn.Answer))
                Error(file, $"final_task: a turn of {DialogueTask.Learner} needs en (what to say) and answer");
        }
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

            case ExerciseText.Translate:
                if (string.IsNullOrWhiteSpace(item.Sv) || string.IsNullOrWhiteSpace(item.En))
                    Error(file, $"{name}: needs en (the prompt) and sv (the answer)");
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

            case ExerciseText.Numbers:
                if (string.IsNullOrWhiteSpace(item.Sv))
                    Error(file, $"{name}: needs sv, the sentence that is heard");
                if (!(item.Answer ?? "").Any(char.IsDigit))
                    Error(file, $"{name}: answer must be the number in digits");
                break;

            case ExerciseText.ListenChoose:
                // A reply in the final task has no sv: there the learner chooses what to say.
                if (item.Options.Count < 2 || item.Options.Distinct().Count() != item.Options.Count)
                    Error(file, $"{name}: needs at least two different options");
                if (!item.Options.Contains(item.Answer ?? ""))
                    Error(file, $"{name}: answer '{item.Answer}' is not one of the options");
                break;

            case ExerciseText.SceneClick:
                CheckSceneClickItem(file, item);
                break;

            default:
                if (!ExerciseText.AllTypes.Contains(item.Type))
                    Error(file, $"{name}: unknown exercise type '{item.Type}'");
                break;
        }
    }

    private void CheckSceneClickItem(string file, ExerciseItem item)
    {
        var name = $"scene_click {item.Lemma}";
        if (item.Lemma == null || !_words.TryGetValue(item.Lemma, out var word))
        {
            Error(file, $"{name}: needs lemma, a word of a theme pack", 6);
            return;
        }
        if (word.Svg == null)
        {
            Error(file, $"{name}: the word has no svg id in its pack");
            return;
        }
        if (_contentDir == null)
            return;
        var pack = _course.Packs.FirstOrDefault(p => p.Words.Contains(word));
        if (SceneIds(pack) is not { } ids)
            Error(file, $"{name}: the scene of pack '{pack?.Theme}' is not drawn");
        else if (!ids.Contains(word.Svg))
            Error(file, $"{name}: the scene has no element with id '{word.Svg}'");
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

    /// <summary>
    /// The theme practice files. They are checked by generating them again: the generator
    /// only uses known words and allowed grammar, so an equal file obeys rules 2 and 4.
    /// </summary>
    private void CheckThemeFiles()
    {
        var allGrammar = _grammar.Keys.ToHashSet();
        foreach (var pack in _course.Packs)
        {
            var id = DrillGenerator.ThemePrefix + pack.Theme;
            var file = $"generated/{id}.yaml";
            var onDisk = _course.Generated.FirstOrDefault(g => g.Unit == id);
            foreach (var item in onDisk?.Items ?? [])
                CheckItem(new Unit { Id = id }, file, item, allGrammar);

            if (_saldo == null || _contentDir == null)
                continue;
            var expected = new DrillGenerator(_course, _saldo).GenerateTheme(pack).File;
            var path = Path.Combine(_contentDir, "generated", id + ".yaml");
            if (expected.Items.Count == 0)
            {
                if (File.Exists(path))
                    Error(file, "the pack gives no drills; run the generator to remove the file", 8);
            }
            else if (!File.Exists(path))
                Error(file, "is missing; run the generator", 8);
            else if (File.ReadAllText(path).ReplaceLineEndings("\n") != DrillGenerator.ToYaml(expected))
                Error(file, "is out of date with its inputs; run the generator", 8);
        }
    }

    /// <summary>Rules 2 and 3: only known words in what the learner writes and hears.</summary>
    private void CheckWordsAreKnown(Unit unit, string file, List<ExerciseItem> items)
    {
        if (_vocabulary == null)
            return;
        var known = _vocabulary.KnownWords(unit);

        // Rule 2. What the learner writes: the written exercise types, and the replies of
        // the final task. A chosen reply (a listen_choose without sv) has Swedish options.
        static bool IsChosenReply(ExerciseItem i) => i.Type == ExerciseText.ListenChoose && string.IsNullOrEmpty(i.Sv);
        foreach (var item in items.Where(i => ExerciseText.Written.Contains(i.Type) || IsChosenReply(i)))
        {
            var texts = new[] { ExerciseText.FullSentence(item), item.Answer ?? "" }.Concat(item.Accept);
            if (item.Type == ExerciseText.Cloze)
                texts = texts.Append(item.Hint ?? "");
            if (IsChosenReply(item))
                texts = texts.Concat(item.Options);
            var unknown = texts.SelectMany(t => _vocabulary.UnknownTokens(t, known, unit.Names)).Distinct().ToList();
            if (unknown.Count > 0)
                Error(file, $"{Describe(item)}: not known yet: {string.Join(", ", unknown)}", 2);
        }

        // Rule 3. What the learner hears: the model dialogue, the listening exercises and
        // the other speakers of the final task. A few unknown words are allowed (under 5 %
        // of all words heard in the unit) and each needs a gloss in the unit.
        var heard = unit.ModelDialogue.Select(l => l.Sv)
            .Concat(items.Where(i => i.Type is ExerciseText.ListenChoose or ExerciseText.Numbers).Select(i => i.Sv ?? ""))
            .Concat(unit.FinalTask == null ? [] : DialogueTask.Steps(unit.FinalTask).SelectMany(s => s.Heard).Select(l => l.Sv))
            .ToList();
        var total = 0;
        var unknownCount = 0;
        foreach (var text in heard)
        {
            total += Tokenizer.Tokens(text).Count(t => !Tokenizer.IsNumber(t));
            var unknown = _vocabulary.UnknownTokens(text, known, unit.Names);
            unknownCount += unknown.Count;
            var noGloss = unknown.Where(t => !HasGloss(unit, t)).ToList();
            if (noGloss.Count > 0)
                Error(file, $"listening \"{text}\": unknown words without a gloss: {string.Join(", ", noGloss)}", 3);
        }
        if (total > 0 && unknownCount > 0.05 * total)
            Error(file, $"{unknownCount} of the {total} words heard in the unit are unknown (at most 5 %)", 3);
    }

    /// <summary>A gloss may be given for the form heard ("blir") or for its base form ("bli").</summary>
    private bool HasGloss(Unit unit, string token) =>
        unit.Glosses.ContainsKey(token.ToLowerInvariant()) || _vocabulary!.LemmasOf(token).Any(unit.Glosses.ContainsKey);

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
            // A chosen reply of the final task has its Swedish in the answer, not in sv.
            var text = string.IsNullOrEmpty(item.Sv) ? item.Answer ?? "" : ExerciseText.FullSentence(item);
            foreach (var token in Tokenizer.Tokens(text))
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
        item.Type == ExerciseText.Inflect ? $"inflect {item.Lemma}.{item.Form}"
        : item.Type == ExerciseText.SceneClick ? $"scene_click {item.Lemma}"
        : $"{item.Type} \"{item.Sv ?? item.Answer}\"";

    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key);

    private void Error(string file, string message, int rule = 1) =>
        _issues.Add(new ContentIssue(Severity.Error, file, message, rule));

    private void Warning(string file, string message, int rule = 0) =>
        _issues.Add(new ContentIssue(Severity.Warning, file, message, rule));
}
