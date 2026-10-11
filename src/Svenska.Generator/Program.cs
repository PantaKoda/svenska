using Svenska.Core.Content;

// Usage: Svenska.Generator [content-dir]
// Writes content/generated/<unit>.yaml for every unit with a `core` section.
// The frame filling itself is milestone M2; until then this only lists the work.

Console.OutputEncoding = System.Text.Encoding.UTF8;

var contentDir = args.Length > 0 ? args[0] : ContentLoader.FindContentDir(Directory.GetCurrentDirectory());
if (contentDir == null || !Directory.Exists(contentDir))
{
    Console.Error.WriteLine("No content folder found. Run from the repository or pass the path.");
    return 2;
}

var (course, issues) = ContentLoader.Load(contentDir);
foreach (var issue in issues)
    Console.Error.WriteLine(issue);
if (issues.Any(i => i.Severity == Severity.Error))
    return 1;

var units = course.Units.Where(u => u.Core != null).ToList();
if (units.Count == 0)
{
    Console.WriteLine("No units with a core section; nothing to generate.");
    return 0;
}

foreach (var unit in units)
    Console.Error.WriteLine($"{unit.Id}: drill generation is not implemented yet (M2).");
return 1;
