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
/// One exercise item, authored or generated. Only the fields AGENTS.md names are here;
/// the per-type fields are settled in M1 and M5 when the exercise types are implemented.
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

    // Source of a generated item.
    public string? Frame { get; set; }
    public List<string>? Words { get; set; }
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
    public List<string> NewWords { get; set; } = [];
    public List<string> NewGrammar { get; set; } = [];
    public List<string> Names { get; set; } = [];
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
    public string InputsHash { get; set; } = "";
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
