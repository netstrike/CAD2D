using Cad.Geometry;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// La scena convertita in un <see cref="SKPath"/> per colore. Le coordinate sono relative a <see cref="Origin"/>
/// (il centro del disegno): in float, coordinate grandi come quelle UTM perderebbero precisione.
/// </summary>
internal sealed class SceneGeometry : IDisposable
{
    private SceneGeometry(Scene scene, Vector2 origin, IReadOnlyList<(SKColor Color, SKPath Path)> paths, IReadOnlyList<(SKColor Color, SKPath Path)> fills)
    {
        Scene = scene;
        Origin = origin;
        Paths = paths;
        Fills = fills;
    }

    public static SceneGeometry Empty { get; } = new(Scene.Empty, Vector2.Zero, [], []);

    /// <summary>Aree piene per colore, con regola "diverso da zero" (gli anelli arrivano già orientati).</summary>
    public IReadOnlyList<(SKColor Color, SKPath Path)> Fills { get; }

    public Scene Scene { get; }
    public Vector2 Origin { get; }
    public IReadOnlyList<(SKColor Color, SKPath Path)> Paths { get; }

    public static SceneGeometry Build(Scene scene)
    {
        var origin = scene.Bounds.IsEmpty ? Vector2.Zero : scene.Bounds.Center;
        var paths = new List<(SKColor, SKPath)>(scene.Batches.Count);
        foreach (var batch in scene.Batches)
        {
            var path = new SKPath();
            foreach (var polyline in batch.Polylines)
            {
                var first = polyline[0] - origin;
                path.MoveTo((float)first.X, (float)first.Y);
                for (var i = 1; i < polyline.Length; i++)
                {
                    var p = polyline[i] - origin;
                    path.LineTo((float)p.X, (float)p.Y);
                }
            }

            paths.Add((ToSkColor(batch.Color), path));
        }

        var fills = new List<(SKColor, SKPath)>(scene.Fills.Count);
        foreach (var batch in scene.Fills)
        {
            var path = new SKPath { FillType = SKPathFillType.Winding };
            foreach (var ring in batch.Rings)
            {
                var first = ring[0] - origin;
                path.MoveTo((float)first.X, (float)first.Y);
                for (var i = 1; i < ring.Length; i++)
                {
                    var p = ring[i] - origin;
                    path.LineTo((float)p.X, (float)p.Y);
                }

                path.Close();
            }

            fills.Add((ToSkColor(batch.Color), path));
        }

        return new SceneGeometry(scene, origin, paths, fills);
    }

    /// <summary>Il nero puro è invisibile su fondo scuro: come negli altri CAD, si disegna bianco.</summary>
    public static SKColor ToSkColor(Cad.Document.CadColor color) =>
        color is { R: 0, G: 0, B: 0 } ? SKColors.White : new SKColor(color.R, color.G, color.B);

    public void Dispose()
    {
        foreach (var (_, path) in Paths.Concat(Fills))
        {
            path.Dispose();
        }
    }
}
