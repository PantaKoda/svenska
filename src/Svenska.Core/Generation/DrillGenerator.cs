using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Saldo;
using Svenska.Core.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Svenska.Core.Generation;

/// <param name="File">What goes into content/generated/&lt;unit&gt;.yaml.</param>
/// <param name="Notes">What was skipped and why (frames, missing or ambiguous forms).</param>
public sealed record GenerationResult(GeneratedFile File, List<string> Notes);

/// <summary>
/// Makes the core-layer drills of a unit: fills the unit's known words into the sentence
/// frames and turns the sentences into exercise items. The same content always gives
/// the same output.
/// </summary>
public sealed class DrillGenerator(Course course, ISaldoLexicon saldo)
{
    private static readonly string[] SentenceTemplates =
        [ExerciseText.Cloze, ExerciseText.Dictation, ExerciseText.WordOrder, ExerciseText.Translate];

    /// <summary>The templates a unit may list under core.templates.</summary>
    public static readonly string[] Templates = [.. SentenceTemplates, ExerciseText.Inflect, ExerciseText.SceneClick];

    private readonly Vocabulary _vocabulary = new(course, saldo);

    /// <summary>A frame with a word in every slot.</summary>
    private sealed record Sentence(Frame Frame, FrameTemplate Template, Word[] Words, string[] SlotTexts, string Sv, string En);

    public GenerationResult Generate(Unit unit)
    {
        var notes = new SortedSet<string>(StringComparer.Ordinal);
        var items = new List<ExerciseItem>();
        var core = unit.Core ?? new CoreSpec();
        var random = new Random(unit.Id.Aggregate(17, (hash, c) => unchecked(hash * 31 + c)));
        var focus = unit.NewWords.ToHashSet();

        var sentences = Sentences(unit, focus, notes);
        Shuffle(sentences, random);

        var usedSentences = new Dictionary<string, int>();
        var usedFrames = new Dictionary<string, int>();
        var usedWords = new Dictionary<string, int>();

        foreach (var template in core.Templates)
        {
            if (template == ExerciseText.Inflect)
                items.AddRange(InflectItems(unit, core.ItemsPerTemplate, random, notes));
            else if (template == ExerciseText.SceneClick)
                items.AddRange(unit.NewWords.Select(_vocabulary.Find).Where(w => w?.Svg != null)
                    .Take(core.ItemsPerTemplate)
                    .Select(w => new ExerciseItem { Type = template, Lemma = w!.Lemma, Words = [w.Lemma] }));
            else if (SentenceTemplates.Contains(template))
            {
                var pool = sentences
                    .Where(s => template != ExerciseText.WordOrder || Tokenizer.Tokens(s.Sv).Count >= 3)
                    .ToList();
                for (var n = 0; n < core.ItemsPerTemplate && pool.Count > 0; n++)
                {
                    // Spread the items: a sentence, frame or focus word that was used less comes first.
                    // Among equals, a sentence that also reuses older words wins.
                    var best = pool.MinBy(s =>
                        1000 * usedSentences.GetValueOrDefault(s.Sv)
                        + 10 * usedFrames.GetValueOrDefault(s.Frame.Id)
                        + 10 * s.Words.Where(w => focus.Contains(w.Lemma)).Sum(w => usedWords.GetValueOrDefault(w.Lemma))
                        - s.Words.Count(w => !focus.Contains(w.Lemma)))!;
                    pool.Remove(best);
                    usedSentences[best.Sv] = usedSentences.GetValueOrDefault(best.Sv) + 1;
                    usedFrames[best.Frame.Id] = usedFrames.GetValueOrDefault(best.Frame.Id) + 1;
                    foreach (var word in best.Words.Where(w => focus.Contains(w.Lemma)))
                        usedWords[word.Lemma] = usedWords.GetValueOrDefault(word.Lemma) + 1;
                    var item = Item(template, best, focus);
                    // Another sentence with the same English is an equally good translation.
                    if (template == ExerciseText.Translate)
                        item.Accept = [.. sentences.Where(s => s.En == best.En && s.Sv != best.Sv).Select(s => s.Sv).Order(StringComparer.Ordinal)];
                    items.Add(item);
                }
                var made = items.Count(i => i.Type == template);
                if (made < core.ItemsPerTemplate)
                    notes.Add($"{template}: only {made} of {core.ItemsPerTemplate} items, there are not enough sentences");
            }
            else
                notes.Add($"unknown template '{template}'");
        }

        return new GenerationResult(new GeneratedFile { Unit = unit.Id, Items = items }, [.. notes]);
    }

    /// <summary>The text of a generated file. The validator compares it with the file on disk.</summary>
    public static string ToYaml(GeneratedFile file)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults
                                            | DefaultValuesHandling.OmitEmptyCollections)
            .WithNewLine("\n")
            .Build();
        return "# Generated by Svenska.Generator. Do not edit by hand; change the frames or the unit\n"
               + "# and run the generator again.\n"
               + serializer.Serialize(file);
    }

    /// <summary>
    /// A stand-in unit for practising a theme pack outside the unit order: the pack's words
    /// up to <paramref name="level"/> are new, every other word and all grammar of the level are known.
    /// </summary>
    public static Unit ThemeUnit(Course course, ThemePack pack, Level level, int itemsPerTemplate = 6)
    {
        var own = pack.Words.Where(w => w.Level <= level).Select(w => w.Lemma).ToList();
        return new Unit
        {
            Id = "theme-" + pack.Theme,
            Level = level,
            Theme = pack.Theme,
            Title = pack.Title,
            NewWords = own,
            Assumes = new Assumes
            {
                Words = [.. course.Packs.SelectMany(p => p.Words).Where(w => w.Level <= level)
                    .Select(w => w.Lemma).Except(own)],
                Grammar = [.. course.Grammar.Where(g => g.Level <= level).Select(g => g.Id)],
            },
            Core = new CoreSpec { Templates = [.. SentenceTemplates, ExerciseText.Inflect], ItemsPerTemplate = itemsPerTemplate },
        };
    }

    // ---- sentences ----

    private List<Sentence> Sentences(Unit unit, HashSet<string> focus, SortedSet<string> notes)
    {
        var known = _vocabulary.KnownWords(unit);
        var grammar = _vocabulary.KnownGrammar(unit);
        var words = known.Order(StringComparer.Ordinal).Select(_vocabulary.Find)
            .Where(w => w != null && w.Level <= unit.Level).Select(w => w!).ToList();
        var authored = unit.Exercises.Select(ExerciseText.FullSentence).ToHashSet();

        var sentences = new List<Sentence>();
        var frames = course.Frames.Concat(course.Packs.Where(p => p.Theme == unit.Theme).SelectMany(p => p.Frames));
        foreach (var frame in frames.Where(f => f.Level <= unit.Level && f.Grammar.All(grammar.Contains)))
        {
            FrameTemplate template;
            try
            {
                template = FrameTemplate.Parse(frame.Sv);
                FrameTemplate.EnglishRefs(frame.En);
            }
            catch (FormatException e)
            {
                notes.Add($"frame {frame.Id} skipped: {e.Message}");
                continue;
            }
            if (template.Slots.Any(s => s.Pos == null || (s.Grammar != null && !grammar.Contains(s.Grammar))))
                continue;

            // {tool.a} writes "en" or "ett" into the sentence, so the article must be known too.
            var article = template.Slots.Any(s => s.Form == WordForms.WithArticle) ? " en" : "";
            var unknown = _vocabulary.UnknownTokens(template.FixedText + article, known, unit.Names);
            if (unknown.Count > 0)
            {
                notes.Add($"frame {frame.Id} skipped: its fixed words are not known yet ({string.Join(", ", unknown)})");
                continue;
            }

            var before = sentences.Count;
            foreach (var choice in Choices(template.Slots, words, 0, []))
            {
                if (!choice.Any(w => focus.Contains(w.Lemma)))
                    continue;
                var sentence = Render(frame, template, choice, notes);
                if (sentence != null && !authored.Contains(sentence.Sv) && sentences.All(s => s.Sv != sentence.Sv))
                    sentences.Add(sentence);
            }
            if (sentences.Count == before)
                notes.Add($"frame {frame.Id}: no sentence with a new word of this unit");
        }
        return sentences;
    }

    /// <summary>Every way to put a different known word with a fitting tag into each slot.</summary>
    private static IEnumerable<Word[]> Choices(IReadOnlyList<Slot> slots, List<Word> words, int index, Word[] chosen)
    {
        if (index == slots.Count)
        {
            yield return chosen;
            yield break;
        }
        var slot = slots[index];
        foreach (var word in words.Where(w => w.Pos == slot.Pos && w.Tags.Intersect(slot.Tags).Any() && !chosen.Contains(w)))
            foreach (var choice in Choices(slots, words, index + 1, [.. chosen, word]))
                yield return choice;
    }

    private Sentence? Render(Frame frame, FrameTemplate template, Word[] words, SortedSet<string> notes)
    {
        var texts = new string[words.Length];
        foreach (var slot in template.Slots)
        {
            var word = words[slot.Number - 1];
            IReadOnlyList<string> forms;
            if (slot.AgreesWith is { } target)
            {
                if (target < 1 || target > words.Length || template.Slots[target - 1].Pos != "noun")
                    return null; // the validator reports the frame
                forms = WordForms.Agree(word, words[target - 1], template.Slots[target - 1].Form!, saldo);
            }
            else
                forms = WordForms.Inflect(word, slot.Form!, saldo);

            if (forms.Count != 1)
            {
                var what = slot.AgreesWith != null ? "agreeing form" : $"{WordForms.Label(slot.Form!)} form";
                notes.Add(forms.Count == 0
                    ? $"{word.Lemma}: no {what} in SALDO or the pack; sentences with it are skipped"
                    : $"{word.Lemma}: several {what}s ({string.Join(" / ", forms)}); sentences with it are skipped");
                return null;
            }
            texts[slot.Number - 1] = forms[0];
        }

        var sv = Capitalize(string.Concat(template.Parts.Select(p => p is Slot s ? texts[s.Number - 1] : (string)p)));
        var en = Capitalize(FrameTemplate.FillEnglish(frame.En, reference =>
        {
            if (reference.Slot < 1 || reference.Slot > words.Length)
                return "?";
            var word = words[reference.Slot - 1];
            var plural = template.Slots[reference.Slot - 1].Form is "pl" or "pl_def";
            return reference.Modifier switch
            {
                "a" => English.WithArticle(English.Base(word)),
                "pp" => English.PastParticiple(word),
                "past" => English.Past(word),
                _ => plural ? English.Plural(word) : English.Base(word),
            };
        }));
        return new Sentence(frame, template, words, texts, sv, en);
    }

    private static ExerciseItem Item(string type, Sentence sentence, HashSet<string> focus)
    {
        var item = new ExerciseItem
        {
            Type = type,
            Sv = sentence.Sv,
            En = sentence.En,
            Grammar = [.. sentence.Frame.Grammar],
            Review = sentence.Frame.Review,
            Frame = sentence.Frame.Id,
            Words = [.. sentence.Words.Select(w => w.Lemma)],
        };
        if (type != ExerciseText.Cloze)
            return item;

        // Blank the unit's new word.
        var slot = sentence.Template.Slots.First(s => focus.Contains(sentence.Words[s.Number - 1].Lemma));
        var first = ReferenceEquals(sentence.Template.Parts[0], slot);
        item.Answer = first ? Capitalize(sentence.SlotTexts[slot.Number - 1]) : sentence.SlotTexts[slot.Number - 1];
        item.Hint = sentence.Words[slot.Number - 1].Lemma;
        item.Sv = string.Concat(sentence.Template.Parts.Select(p =>
            p is not Slot s ? (string)p : s == slot ? ExerciseText.Gap : sentence.SlotTexts[s.Number - 1]));
        if (!first)
            item.Sv = Capitalize(item.Sv);
        return item;
    }

    // ---- single words ----

    private List<ExerciseItem> InflectItems(Unit unit, int count, Random random, SortedSet<string> notes)
    {
        var grammar = _vocabulary.KnownGrammar(unit);
        var authored = unit.Exercises.Where(i => i.Type == ExerciseText.Inflect).Select(i => (i.Lemma, i.Form)).ToHashSet();

        var options = new List<(Word Word, string Form, string Grammar, string Answer)>();
        foreach (var word in unit.NewWords.Select(_vocabulary.Find).Where(w => w != null).Select(w => w!))
        foreach (var form in WordForms.Names.Where(f => WordForms.IsValid(f, word.Pos)))
        {
            if (WordForms.GrammarOf(form) is not { } needs || !grammar.Contains(needs) || authored.Contains((word.Lemma, form)))
                continue;
            var forms = WordForms.Inflect(word, form, saldo);
            if (forms.Count == 1)
                options.Add((word, form, needs, forms[0]));
            else
                notes.Add(forms.Count == 0
                    ? $"{word.Lemma}: no {WordForms.Label(form)} form in SALDO or the pack"
                    : $"{word.Lemma}: several {WordForms.Label(form)} forms ({string.Join(" / ", forms)})");
        }
        Shuffle(options, random);

        // The unit's new grammar first, and no word twice before every word has had a turn.
        var used = new Dictionary<string, int>();
        var items = new List<ExerciseItem>();
        while (items.Count < count && options.Count > 0)
        {
            var best = options.MinBy(o => 10 * used.GetValueOrDefault(o.Word.Lemma) + (unit.NewGrammar.Contains(o.Grammar) ? 0 : 1));
            options.Remove(best);
            used[best.Word.Lemma] = used.GetValueOrDefault(best.Word.Lemma) + 1;
            items.Add(new ExerciseItem
            {
                Type = ExerciseText.Inflect,
                Lemma = best.Word.Lemma,
                Form = best.Form,
                Answer = best.Answer,
                Grammar = [best.Grammar],
                Words = [best.Word.Lemma],
            });
        }
        return items;
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
