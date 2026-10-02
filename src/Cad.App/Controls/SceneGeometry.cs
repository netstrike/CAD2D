using Cad.Geometry;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// Geometria della scena già convertita in un unico <see cref="SKPath"/> in coordinate mondo.
/// Pan e zoom cambiano solo la matrice di disegno, quindi il percorso si costruisce una volta per scena.
/// </summary>
internal sealed class SceneGeometry : IDisposable
{
    public static readonly SceneGeometry Empty = new(new SKPath(), BoundingBox.Empty, isShared: true);

    private readonly bool _isShared;

    private SceneGeometry(SKPath path, BoundingBox bounds, bool isShared = false)
    {
        Path = path;
        Bounds = bounds;
        _isShared = isShared;
    }

    public SKPath Path { get; }
    public BoundingBox Bounds { get; }

    public static SceneGeometry Build(IReadOnlyList<Segment2D> segments)
    {
        var path = new SKPath();
        var bounds = BoundingBox.Empty;
        foreach (var s in segments)
        {
            path.MoveTo((float)s.Start.X, (float)s.Start.Y);
            path.LineTo((float)s.End.X, (float)s.End.Y);
            bounds = bounds.Union(s.Bounds);
        }

        return new SceneGeometry(path, bounds);
    }

    public void Dispose()
    {
        if (!_isShared)
        {
            Path.Dispose();
        }
    }
}
