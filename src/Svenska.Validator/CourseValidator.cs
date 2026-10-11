using Svenska.Core.Content;

namespace Svenska.Validator;

/// <summary>
/// Checks a loaded course. M0 covers the base files (levels, grammar, tags, frame ids);
/// the unit, pack and generated-file rules of AGENTS.md are added with the milestones
/// that introduce that content.
/// </summary>
public static class CourseValidator
{
    private const int MaxCardLines = 15;

    public static List<ContentIssue> Validate(Course course)
    {
        var issues = new List<ContentIssue>();
        CheckGrammar(course, issues);
        CheckTags(course, issues);
        CheckFrameIds(course, issues);
        return issues;
    }

    private static void CheckGrammar(Course course, List<ContentIssue> issues)
    {
        const string file = "grammar.yaml";
        foreach (var id in Duplicates(course.Grammar.Select(g => g.Id)))
            issues.Add(Error(file, $"grammar id '{id}' is defined twice"));

        var byId = course.Grammar.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var (level, def) in course.Levels)
        {
            foreach (var id in Duplicates(def.Grammar))
                issues.Add(Error("levels.yaml", $"{level} lists '{id}' twice"));
            foreach (var id in def.Grammar)
            {
                if (!byId.TryGetValue(id, out var point))
                    issues.Add(Error("levels.yaml", $"{level} lists '{id}', which is not in grammar.yaml"));
                else if (point.Level != level)
                    issues.Add(Error("levels.yaml", $"{level} lists '{id}', but grammar.yaml says {point.Level}", 4));
            }
        }

        var noCard = new List<string>();
        foreach (var point in course.Grammar)
        {
            if (string.IsNullOrWhiteSpace(point.Id) || string.IsNullOrWhiteSpace(point.Title))
                issues.Add(Error(file, $"grammar point '{point.Id}' needs an id and a title"));
            if (!course.Levels.TryGetValue(point.Level, out var def) || !def.Grammar.Contains(point.Id))
                issues.Add(Error(file, $"'{point.Id}' is not listed under {point.Level} in levels.yaml", 4));

            foreach (var required in point.Requires)
            {
                if (!byId.TryGetValue(required, out var other))
                    issues.Add(Error(file, $"'{point.Id}' requires unknown grammar point '{required}'"));
                else if (other.Level > point.Level)
                    issues.Add(Error(file, $"'{point.Id}' ({point.Level}) requires '{required}' ({other.Level})", 4));
            }

            if (string.IsNullOrWhiteSpace(point.Card))
                noCard.Add(point.Id);
            else if (point.Card.TrimEnd().Split('\n').Length > MaxCardLines)
                issues.Add(Warning(file, $"card of '{point.Id}' is longer than {MaxCardLines} lines"));
        }
        if (noCard.Count > 0)
            issues.Add(Warning(file, $"{noCard.Count} grammar points have no card yet: {string.Join(", ", noCard)}"));
    }

    private static void CheckTags(Course course, List<ContentIssue> issues)
    {
        foreach (var tag in Duplicates(course.Tags.All()))
            issues.Add(Error("tags.yaml", $"tag '{tag}' is listed twice"));
    }

    private static void CheckFrameIds(Course course, List<ContentIssue> issues)
    {
        var ids = course.Frames.Select(f => f.Id).Concat(course.Packs.SelectMany(p => p.Frames).Select(f => f.Id));
        foreach (var id in Duplicates(ids))
            issues.Add(Error("frames", $"frame id '{id}' is used twice"));
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key);

    private static ContentIssue Error(string file, string message, int rule = 1) =>
        new(Severity.Error, file, message, rule);

    private static ContentIssue Warning(string file, string message) =>
        new(Severity.Warning, file, message);
}
