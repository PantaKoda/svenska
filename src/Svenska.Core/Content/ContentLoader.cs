using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Svenska.Core.Content;

public enum Severity { Warning, Error }

/// <summary>A problem found in the content. Rule is the validator rule number in AGENTS.md (0 = none).</summary>
public sealed record ContentIssue(Severity Severity, string File, string Message, int Rule = 0)
{
    public override string ToString() =>
        $"{(Severity == Severity.Error ? "ERROR" : "warn ")} {(Rule > 0 ? $"[rule {Rule}] " : "")}{File}: {Message}";
}

public static class ContentLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Loads everything under <paramref name="contentDir"/>. Never throws on bad content:
    /// a file that does not parse is skipped and reported in the issue list.
    /// </summary>
    public static (Course Course, List<ContentIssue> Issues) Load(string contentDir)
    {
        var issues = new List<ContentIssue>();
        var course = new Course
        {
            Levels = Read<Dictionary<Level, LevelDef>>(contentDir, "levels.yaml", issues, required: true) ?? [],
            Grammar = Read<List<GrammarPoint>>(contentDir, "grammar.yaml", issues, required: true) ?? [],
            Tags = Read<TagList>(contentDir, "tags.yaml", issues, required: true) ?? new(),
            Frames = Read<List<Frame>>(contentDir, "frames.yaml", issues, required: true) ?? [],
            Packs = ReadAll<ThemePack>(contentDir, "themes", issues),
            Units = ReadAll<Unit>(contentDir, "units", issues),
            Generated = ReadAll<GeneratedFile>(contentDir, "generated", issues),
        };
        course.Units = [.. course.Units.OrderBy(u => u.Level).ThenBy(u => u.Id, StringComparer.Ordinal)];
        return (course, issues);
    }

    /// <summary>Walks up from <paramref name="startDir"/> to the folder that holds content/levels.yaml.</summary>
    public static string? FindContentDir(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "levels.yaml")))
                return candidate;
        }
        return null;
    }

    private static List<T> ReadAll<T>(string contentDir, string subDir, List<ContentIssue> issues) where T : class
    {
        var result = new List<T>();
        var dir = Path.Combine(contentDir, subDir);
        if (!Directory.Exists(dir))
            return result;
        foreach (var path in Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(contentDir, path).Replace('\\', '/');
            if (Read<T>(contentDir, relative, issues, required: true) is { } item)
                result.Add(item);
        }
        return result;
    }

    private static T? Read<T>(string contentDir, string relativePath, List<ContentIssue> issues, bool required)
        where T : class
    {
        var path = Path.Combine(contentDir, relativePath);
        if (!File.Exists(path))
        {
            if (required)
                issues.Add(new ContentIssue(Severity.Error, relativePath, "file is missing", 1));
            return null;
        }
        try
        {
            using var reader = new StreamReader(path);
            return Yaml.Deserialize<T>(reader);
        }
        catch (YamlException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            issues.Add(new ContentIssue(Severity.Error, relativePath, $"line {ex.Start.Line}: {detail}", 1));
            return null;
        }
    }
}
