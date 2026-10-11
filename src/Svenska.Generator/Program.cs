using Svenska.Core.Content;
using Svenska.Core.Generation;
using Svenska.Core.Saldo;

// Usage: Svenska.Generator [content-dir]
//   Writes content/generated/<unit>.yaml for every unit with a `core` section, and
//   content/generated/theme-<pack>.yaml for every theme pack (for Theme practice).
//
// Usage: Svenska.Generator [content-dir] --theme <name> [--level A1]
//   Prints the drills of one theme pack without writing them.

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

string? theme = null;
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--theme" && i + 1 < args.Length) theme = args[++i];
    else positional.Add(args[i]);
}

var contentDir = positional.Count > 0 ? positional[0] : ContentLoader.FindContentDir(Directory.GetCurrentDirectory());
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

var saldoDb = Path.Combine(contentDir, "..", "data", "saldo.db");
if (!File.Exists(saldoDb))
{
    Console.Error.WriteLine("data/saldo.db is missing; build it with tools/saldo_to_sqlite.py.");
    return 2;
}
using var saldo = new SaldoLexicon(saldoDb);
var generator = new DrillGenerator(course, saldo);

if (theme != null)
{
    var pack = course.Packs.FirstOrDefault(p => p.Theme == theme);
    if (pack == null)
    {
        Console.Error.WriteLine($"There is no theme pack '{theme}'.");
        return 2;
    }
    var preview = generator.GenerateTheme(pack);
    Console.Write(DrillGenerator.ToYaml(preview.File));
    Report($"theme {theme}", preview);
    return 0;
}

var outputDir = Path.Combine(contentDir, "generated");
Directory.CreateDirectory(outputDir);
var written = new HashSet<string>();
foreach (var result in generator.GenerateAll())
{
    var path = Path.Combine(outputDir, result.File.Unit + ".yaml");
    var yaml = DrillGenerator.ToYaml(result.File);
    var changed = !File.Exists(path) || File.ReadAllText(path).ReplaceLineEndings("\n") != yaml;
    if (changed)
        File.WriteAllText(path, yaml);
    written.Add(result.File.Unit);
    Report($"{result.File.Unit} ({(changed ? "written" : "unchanged")})", result);
}

// A generated file that nothing generates any more is stale.
foreach (var stale in Directory.EnumerateFiles(outputDir, "*.yaml")
             .Where(f => !written.Contains(Path.GetFileNameWithoutExtension(f))))
{
    File.Delete(stale);
    Console.Error.WriteLine($"{Path.GetFileName(stale)}: deleted, nothing generates it");
}
if (written.Count == 0)
    Console.WriteLine("No units with a core section and no theme drills; nothing to generate.");
return 0;

static void Report(string name, GenerationResult result)
{
    var byType = result.File.Items.GroupBy(i => i.Type).Select(g => $"{g.Key} {g.Count()}");
    Console.Error.WriteLine($"{name}: {result.File.Items.Count} items ({string.Join(", ", byType)}), " +
                            $"{result.File.Items.Count(i => i.Review)} with review: true");
    foreach (var note in result.Notes)
        Console.Error.WriteLine($"  note: {note}");
}
