namespace Svenska.Tests;

/// <summary>A scratch folder that is deleted when the test ends.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svenska-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Write(string relativePath, string text)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary>Runs the drill generator on the course in this folder, as the Generator program does.</summary>
    public void WriteGenerated(Svenska.Core.Saldo.ISaldoLexicon saldo)
    {
        var (course, _) = Svenska.Core.Content.ContentLoader.Load(Path);
        foreach (var result in new Svenska.Core.Generation.DrillGenerator(course, saldo).GenerateAll())
            Write($"generated/{result.File.Unit}.yaml", Svenska.Core.Generation.DrillGenerator.ToYaml(result.File));
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
