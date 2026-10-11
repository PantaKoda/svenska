using SkiaSharp;
using Svenska.Core.Scenes;
using Svg.Skia;

namespace Svenska.App.Services;

/// <summary>
/// A scene drawn as SVG (content/svg/*.svg), loaded with Svg.Skia: renders it to a picture
/// and tells which word element (id w-…) is at a point.
/// </summary>
public sealed class SvgScene : IDisposable
{
    private readonly SKSvg _svg = new();
    private readonly SKPicture _picture;

    public double Width { get; }
    public double Height { get; }

    /// <summary>The boxes of the word elements, in the coordinates of the drawing.</summary>
    public SceneMap Map { get; }

    public SvgScene(string path)
    {
        _picture = _svg.Load(path) ?? throw new InvalidDataException($"Cannot read the scene {path}.");
        Width = _picture.CullRect.Width;
        Height = _picture.CullRect.Height;

        var bounds = new Dictionary<string, SceneRect>();
        foreach (var id in SceneMap.WordIds(path))
            if (_svg.TryGetRetainedSceneNodeById(id, out var node) && node != null)
            {
                var box = node.TransformedBounds;
                bounds[id] = new SceneRect(box.Left, box.Top, box.Right, box.Bottom);
            }
        Map = new SceneMap(bounds);
    }

    /// <summary>
    /// The word element at a point of the drawing: the smallest one whose box contains the
    /// point, so the card lying on the counter is the card and a click between the legs of
    /// the table is the table, not the room behind it. A click just beside a thing still counts.
    /// (Svg.Skia can also test the exact shape with HitTestElements; boxes are kinder to click.)
    /// </summary>
    public string? HitTest(double x, double y) => Map.Hit(x, y, padding: 0) ?? Map.Hit(x, y);

    /// <summary>
    /// The scene as a PNG that fits in the given size, with a frame around some elements
    /// (the right answer in green, a wrong click in red).
    /// </summary>
    public byte[] RenderPng(int width, int height, IEnumerable<(string Id, bool Right)>? frames = null)
    {
        var scale = (float)Scale(width, height);
        using var bitmap = new SKBitmap(Math.Max(1, (int)(Width * scale)), Math.Max(1, (int)(Height * scale)));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(_picture);

        foreach (var (id, right) in frames ?? [])
        {
            if (!Map.Bounds.TryGetValue(id, out var box))
                continue;
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 5,
                IsAntialias = true,
                Color = right ? new SKColor(0x2E, 0x9E, 0x5B) : new SKColor(0xD9, 0x53, 0x4F),
            };
            canvas.DrawRoundRect(new SKRect((float)box.Left - 6, (float)box.Top - 6, (float)box.Right + 6, (float)box.Bottom + 6), 8, 8, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>How much the drawing is enlarged to fit in a box of the given size.</summary>
    public double Scale(double width, double height) => Math.Min(width / Width, height / Height);

    public void Dispose() => _svg.Dispose();
}
