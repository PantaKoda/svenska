using Svenska.Core.Content;
using Svenska.Validator;

// Usage: Svenska.Validator [content-dir]
// Exits 1 when there is at least one error.

Console.OutputEncoding = System.Text.Encoding.UTF8;

var contentDir = args.Length > 0 ? args[0] : ContentLoader.FindContentDir(Directory.GetCurrentDirectory());
if (contentDir == null || !Directory.Exists(contentDir))
{
    Console.Error.WriteLine("No content folder found. Run from the repository or pass the path.");
    return 2;
}

var (course, issues) = ContentLoader.Load(contentDir);
issues.AddRange(CourseValidator.Validate(course));

Console.WriteLine($"Content: {Path.GetFullPath(contentDir)}");
Console.WriteLine();

foreach (var unit in course.Units)
    Console.WriteLine($"{unit.Id}  {unit.Title}: {unit.NewWords.Count} new words, " +
                      $"{unit.NewGrammar.Count} new grammar, {unit.Exercises.Count} authored items");
if (course.Units.Count == 0)
    Console.WriteLine("No units yet.");
Console.WriteLine();

var saldoDb = Path.Combine(contentDir, "..", "data", "saldo.db");
if (!File.Exists(saldoDb))
    issues.Add(new ContentIssue(Severity.Warning, "data/saldo.db",
        "not built yet (tools/saldo_to_sqlite.py); word checks need it"));

foreach (var issue in issues.OrderByDescending(i => i.Severity))
    Console.WriteLine(issue);
if (issues.Count > 0)
    Console.WriteLine();

var words = course.Packs.SelectMany(p => p.Words).ToList();
var frames = course.Frames.Concat(course.Packs.SelectMany(p => p.Frames)).ToList();
var items = course.Units.SelectMany(u => u.Exercises).Concat(course.Generated.SelectMany(g => g.Items)).ToList();

Console.WriteLine("Totals");
Console.WriteLine($"  grammar points: {string.Join(", ", course.Levels.OrderBy(l => l.Key).Select(l => $"{l.Key} {l.Value.Grammar.Count}"))}");
Console.WriteLine($"  tags: {course.Tags.All().Count()}   packs: {course.Packs.Count}   units: {course.Units.Count}");
Console.WriteLine($"  words per level: {(words.Count == 0 ? "none" : string.Join(", ", words.GroupBy(w => w.Level).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")))}");
Console.WriteLine($"  frames: {frames.Count} ({frames.Count(f => f.Review)} with review: true)");
Console.WriteLine($"  items: {items.Count} ({items.Count(i => i.Review)} with review: true)");

var errors = issues.Count(i => i.Severity == Severity.Error);
Console.WriteLine();
Console.WriteLine($"{errors} errors, {issues.Count - errors} warnings");
return errors > 0 ? 1 : 0;
