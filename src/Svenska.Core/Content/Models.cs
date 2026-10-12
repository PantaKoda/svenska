namespace Svenska.Core.Content;

// Plain classes that mirror the YAML files under content/ (snake_case keys).
// Unknown keys are a load error, so a typo in a content file is caught by the validator.

public enum Level { A1, A2, B1, B2, C1, C2 }

/// <summary>One entry of levels.yaml: the grammar allowed from this level on.</summary>
public sealed class LevelDef
{
    public List<string> Grammar { get; set; } = [];
}

/// <summary>One entry of grammar.yaml.</summary>
public sealed class GrammarPoint
{
    public string Id { get; set; } = "";
    public Level Level { get; set; }
    public List<string> Requires { get; set; } = [];
    public string Title { get; set; } = "";
    public string? Card { get; set; }
}

/// <summary>tags.yaml: the fixed list of semantic tags, per part of speech.</summary>
public sealed class TagList
{
    public List<string> Nouns { get; set; } = [];
    public List<string> Verbs { get; set; } = [];
    public List<string> Adjectives { get; set; } = [];

    public IEnumerable<string> All() => Nouns.Concat(Verbs).Concat(Adjectives);
}

public sealed class Word
{
    public string Lemma { get; set; } = "";
    public string Pos { get; set; } = "";
    public string? Gender { get; set; }
    public List<string> Tags { get; set; } = [];
    public Level Level { get; set; }
    public string Gloss { get; set; } = "";
    public string? Svg { get; set; }
    /// <summary>Hand-written forms for words missing in saldo.db (form name → form).</summary>
    public Dictionary<string, string>? Forms { get; set; }
    public bool Review { get; set; }
}

public sealed class Frame
{
    public string Id { get; set; } = "";
    public Level Level { get; set; }
    public List<string> Grammar { get; set; } = [];
    public string Sv { get; set; } = "";
    public string En { get; set; } = "";
    public bool Review { get; set; }
}

/// <summary>content/themes/*.yaml</summary>
public sealed class ThemePack
{
    public string Theme { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Scene { get; set; }
    public List<Word> Words { get; set; } = [];
    public List<Frame> Frames { get; set; } = [];
}

public sealed class CoreSpec
{
    public List<string> Templates { get; set; } = [];
    public int ItemsPerTemplate { get; set; }
}

public sealed class DialogueLine
{
    public string Speaker { get; set; } = "";
    public string Sv { get; set; } = "";
    public string En { get; set; } = "";
}

/// <summary>
/// One exercise item, authored or generated. Which fields a type uses is listed in
/// AGENTS.md ("Fields per type"); the validator checks it.
/// </summary>
public sealed class ExerciseItem
{
    public string Type { get; set; } = "";
    public string? Speaker { get; set; }
    public string? Sv { get; set; }
    public string? En { get; set; }
    public string? Answer { get; set; }
    public List<string> Accept { get; set; } = [];
    public List<string> Grammar { get; set; } = [];
    public bool Review { get; set; }
    public List<ExerciseItem>? Turns { get; set; }

    /// <summary>
    /// cloze: the base form of the missing word, shown as help.
    /// numbers: what to listen for ("the price").
    /// </summary>
    public string? Hint { get; set; }
    /// <summary>listen_choose and a chosen dialogue turn: the choices; `answer` is one of them.</summary>
    public List<string> Options { get; set; } = [];
    /// <summary>free_write: what the learner checks their own text against.</summary>
    public List<string> Checklist { get; set; } = [];
    /// <summary>inflect: the word to inflect and the form asked for (indef, def, pl, pres …).</summary>
    public string? Lemma { get; set; }
    public string? Form { get; set; }

    // Source of a generated item.
    public string? Frame { get; set; }
    public List<string>? Words { get; set; }
}

/// <summary>
/// A fixed everyday phrase ("Vi ses!", "Ingen fara."). It is learnt and counted as known
/// as a whole, so its words need not be taught one by one.
/// </summary>
public sealed class Phrase
{
    public string Sv { get; set; } = "";
    public string En { get; set; } = "";
    /// <summary>Other Swedish phrases that are right when the learner translates `en`.</summary>
    public List<string> Accept { get; set; } = [];
    public bool Review { get; set; }
}

/// <summary>
/// Words and grammar a unit takes as known from earlier units that are not written yet.
/// Temporary: the validator warns while a unit has it.
/// </summary>
public sealed class Assumes
{
    public List<string> Words { get; set; } = [];
    public List<string> Grammar { get; set; } = [];
}

/// <summary>content/units/&lt;level&gt;/*.yaml</summary>
public sealed class Unit
{
    public string Id { get; set; } = "";
    public Level Level { get; set; }
    public string Theme { get; set; } = "";
    public string Title { get; set; } = "";
    public string Task { get; set; } = "";
    public string CanDo { get; set; } = "";
    /// <summary>
    /// Set on a companion chapter: extra practice beside one chapter of a course book
    /// ("B1+B2"). Companion chapters stand apart from the task units and never count
    /// as known in them.
    /// </summary>
    public string? Book { get; set; }
    /// <summary>The chapter of the book a companion chapter goes with.</summary>
    public int Chapter { get; set; }
    /// <summary>What a companion chapter practises, in a few words each.</summary>
    public List<string> Focus { get; set; } = [];
    [YamlDotNet.Serialization.YamlIgnore]
    public bool IsCompanion => Book != null;
    public Assumes? Assumes { get; set; }
    public List<string> NewWords { get; set; } = [];
    public List<string> NewGrammar { get; set; } = [];
    /// <summary>The everyday phrases this unit teaches.</summary>
    public List<Phrase> Phrases { get; set; } = [];
    public List<string> Names { get; set; } = [];
    /// <summary>
    /// Meanings of the few words in the unit's listening texts that are not known yet
    /// (word → meaning). Shown with the transcript.
    /// </summary>
    public Dictionary<string, string> Glosses { get; set; } = [];
    public string? Scene { get; set; }
    public CoreSpec? Core { get; set; }
    public List<DialogueLine> ModelDialogue { get; set; } = [];
    public List<ExerciseItem> Exercises { get; set; } = [];
    public ExerciseItem? FinalTask { get; set; }
    public string? SpeakingPrompt { get; set; }
}

/// <summary>content/generated/*.yaml, written by Svenska.Generator.</summary>
public sealed class GeneratedFile
{
    public string Unit { get; set; } = "";
    public List<ExerciseItem> Items { get; set; } = [];
}

/// <summary>Everything under content/, loaded.</summary>
public sealed class Course
{
    public Dictionary<Level, LevelDef> Levels { get; set; } = [];
    public List<GrammarPoint> Grammar { get; set; } = [];
    public TagList Tags { get; set; } = new();
    public List<Frame> Frames { get; set; } = [];
    public List<ThemePack> Packs { get; set; } = [];
    /// <summary>In course order.</summary>
    public List<Unit> Units { get; set; } = [];
    public List<GeneratedFile> Generated { get; set; } = [];
}
