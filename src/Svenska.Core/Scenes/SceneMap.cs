using System.Xml.Linq;

namespace Svenska.Core.Scenes;

/// <summary>A rectangle in the coordinates of the scene drawing.</summary>
public readonly record struct SceneRect(double Left, double Top, double Right, double Bottom)
{
    public double Area => Math.Max(0, Right - Left) * Math.Max(0, Bottom - Top);

    public bool Contains(double x, double y, double padding = 0) =>
        x >= Left - padding && x <= Right + padding && y >= Top - padding && y <= Bottom + padding;
}

/// <summary>
/// Where the clickable things of a scene are. The app fills it from the drawn SVG; the
/// rule for "which thing was clicked" lives here so that it can be tested without a screen.
/// </summary>
public sealed class SceneMap(IReadOnlyDictionary<string, SceneRect> bounds)
{
    /// <summary>Scene elements that stand for a word have ids like w-mjolk.</summary>
    public const string WordPrefix = "w-";

    public IReadOnlyDictionary<string, SceneRect> Bounds { get; } = bounds;

    /// <summary>
    /// The thing at a point: the smallest one whose box (plus a little margin, so that small
    /// things are easy to hit) contains the point. Null if the click is on nothing.
    /// </summary>
    public string? Hit(double x, double y, double padding = 6) =>
        Bounds.Where(b => b.Value.Contains(x, y, padding))
            .OrderBy(b => b.Value.Area)
            .Select(b => b.Key)
            .FirstOrDefault();

    /// <summary>The word ids (w-…) in an SVG file, read from its XML.</summary>
    public static HashSet<string> WordIds(string svgPath) =>
        XDocument.Load(svgPath).Descendants()
            .Select(e => (string?)e.Attribute("id"))
            .Where(id => id != null && id.StartsWith(WordPrefix, StringComparison.Ordinal))
            .Select(id => id!)
            .ToHashSet();

    /// <summary>The id a word gets in a scene: w- plus the lemma in ASCII (å, ä → a; ö → o).</summary>
    public static string IdFor(string lemma) =>
        WordPrefix + string.Concat(lemma.ToLowerInvariant().Select(c => c switch
        {
            'å' or 'ä' => 'a',
            'ö' => 'o',
            'é' => 'e',
            ' ' => '-',
            _ => c,
        }));
}
