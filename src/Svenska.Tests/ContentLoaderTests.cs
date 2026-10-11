using Svenska.Core.Content;
using Svenska.Validator;

namespace Svenska.Tests;

public class ContentLoaderTests
{
    private const string Levels = "A1:\n  grammar: [en_ett, def_singular]\n";
    private const string Grammar = """
        - id: en_ett
          level: A1
          title: "En and ett"
          card: "en bil, ett hus"
        - id: def_singular
          level: A1
          requires: [en_ett]
          title: "Definite form"
          card: |
            en bil → bilen
            ett hus → huset
        """;
    private const string Tags = "nouns: [tool, container, surface]\nverbs: [action_surface]\nadjectives: [state]\n";

    private static TempDir BaseCourse()
    {
        var dir = new TempDir();
        dir.Write("levels.yaml", Levels);
        dir.Write("grammar.yaml", Grammar);
        dir.Write("tags.yaml", Tags);
        dir.Write("frames.yaml", "[]\n");
        return dir;
    }

    [Fact]
    public void Repository_content_loads_and_validates_without_errors()
    {
        var contentDir = ContentLoader.FindContentDir(AppContext.BaseDirectory);
        Assert.NotNull(contentDir);

        var (course, issues) = ContentLoader.Load(contentDir);
        issues.AddRange(CourseValidator.Validate(course));

        Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
        Assert.Contains("def_singular", course.Levels[Level.A1].Grammar);
    }

    [Fact]
    public void Empty_course_loads_base_files()
    {
        using var dir = BaseCourse();

        var (course, issues) = ContentLoader.Load(dir.Path);

        Assert.Empty(issues);
        Assert.Equal(["en_ett", "def_singular"], course.Levels[Level.A1].Grammar);
        Assert.Equal(["en_ett"], course.Grammar[1].Requires);
        Assert.Equal("en bil → bilen\nett hus → huset", course.Grammar[1].Card!.ReplaceLineEndings("\n").TrimEnd());
        Assert.Equal(5, course.Tags.All().Count());
        Assert.Empty(course.Units);
        Assert.Empty(CourseValidator.Validate(course));
    }

    [Fact]
    public void Theme_pack_and_unit_load_as_written_in_the_spec()
    {
        using var dir = BaseCourse();
        dir.Write("themes/stadning.yaml", """
            theme: stadning
            title: "Städning"
            scene: svg/stadning.svg
            words:
              - { lemma: dammsugare, pos: noun, gender: en, tags: [tool, device], level: A1,
                  gloss: vacuum cleaner, svg: w-dammsugare }
              - { lemma: golv, pos: noun, gender: ett, tags: [surface], level: A1, gloss: floor }
              - { lemma: dammsuga, pos: verb, tags: [action_surface], level: A2, gloss: to vacuum }
            frames:
              - { id: stad-01, level: A1, grammar: [def_singular], review: true,
                  sv: "Jag ska {action_surface.inf} {surface.def}.",
                  en: "I am going to {action_surface} the {surface}." }
            """);
        dir.Write("units/a1/04-matbutiken.yaml", """
            id: a1-04
            level: A1
            theme: matbutiken
            title: "I matbutiken"
            task: "Find groceries, understand the price and pay at the checkout."
            can_do: "I can buy food and pay in a Swedish supermarket."
            new_words: [mjölk, bröd, påse]
            new_grammar: [def_singular]
            scene: svg/matbutik.svg
            core:
              templates: [cloze, dictation]
              items_per_template: 6
            model_dialogue:
              - { speaker: kassör, sv: "Hej! Behöver du en påse?", en: "Hi! Do you need a bag?" }
              - { speaker: du,     sv: "Nej tack.",                en: "No thanks." }
            exercises:
              - { type: cloze, sv: "Var är ___?", answer: mjölken, accept: [Mjölken], grammar: [def_singular] }
            speaking_prompt: "Say aloud: ask where the milk is."
            """);

        var (course, issues) = ContentLoader.Load(dir.Path);

        Assert.Empty(issues);
        var pack = Assert.Single(course.Packs);
        Assert.Equal("Städning", pack.Title);
        Assert.Equal(3, pack.Words.Count);
        Assert.Equal("ett", pack.Words[1].Gender);
        Assert.Equal(Level.A2, pack.Words[2].Level);
        Assert.Equal(["tool", "device"], pack.Words[0].Tags);
        Assert.True(pack.Frames[0].Review);

        var unit = Assert.Single(course.Units);
        Assert.Equal("a1-04", unit.Id);
        Assert.Equal(["mjölk", "bröd", "påse"], unit.NewWords);
        Assert.Equal(6, unit.Core!.ItemsPerTemplate);
        Assert.Equal("Nej tack.", unit.ModelDialogue[1].Sv);
        Assert.Equal("mjölken", unit.Exercises[0].Answer);
        Assert.StartsWith("I can buy", unit.CanDo);
    }

    [Fact]
    public void Unknown_key_is_reported_with_file_and_line()
    {
        using var dir = BaseCourse();
        dir.Write("themes/bad.yaml", "theme: bad\ntitle: Bad\nwordz: []\n");

        var (course, issues) = ContentLoader.Load(dir.Path);

        var issue = Assert.Single(issues);
        Assert.Equal(Severity.Error, issue.Severity);
        Assert.Equal("themes/bad.yaml", issue.File);
        Assert.Contains("line 3", issue.Message);
        Assert.Empty(course.Packs);
    }

    [Fact]
    public void Missing_base_file_is_an_error()
    {
        using var dir = BaseCourse();
        File.Delete(Path.Combine(dir.Path, "tags.yaml"));

        var (_, issues) = ContentLoader.Load(dir.Path);

        Assert.Contains(issues, i => i.File == "tags.yaml" && i.Severity == Severity.Error);
    }

    [Fact]
    public void Validator_reports_grammar_that_levels_and_grammar_files_disagree_on()
    {
        using var dir = BaseCourse();
        dir.Write("levels.yaml", "A1:\n  grammar: [en_ett, plural_indef]\nA2:\n  grammar: [def_singular]\n");
        dir.Write("grammar.yaml", Grammar + "\n- id: past_tense\n  level: A1\n  requires: [nothing]\n  title: Past\n  card: x\n");

        var (course, loadIssues) = ContentLoader.Load(dir.Path);
        var issues = CourseValidator.Validate(course);

        Assert.Empty(loadIssues);
        Assert.Contains(issues, i => i.Message.Contains("'plural_indef', which is not in grammar.yaml"));
        Assert.Contains(issues, i => i.Message.Contains("A2 lists 'def_singular', but grammar.yaml says A1"));
        Assert.Contains(issues, i => i.Message.Contains("'past_tense' is not listed under A1"));
        Assert.Contains(issues, i => i.Message.Contains("requires unknown grammar point 'nothing'"));
    }
}
