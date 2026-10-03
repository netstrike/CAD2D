using Cad.Document;
using Cad.Geometry;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.Plot;

/// <summary>
/// Disegna una scena su un foglio: la stessa funzione serve il PDF (unità in punti), l'SVG, l'anteprima e la stampante
/// (unità in pixel). Le coordinate si trasformano in doppia precisione prima di passare a Skia, che lavora in float.
/// </summary>
public static class PlotRenderer
{
    /// <summary>Spessore delle linee sottili, in millimetri: quelle senza spessore o con gli spessori disattivati.</summary>
    public const double ThinLine = 0.13;

    /// <summary>Lato del segno dei punti, in millimetri.</summary>
    private const double PointMarker = 0.5;

    /// <summary>Immagini decodificate per percorso, condivise tra anteprima e stampa.</summary>
    private static readonly Dictionary<string, (DateTime Modified, SKImage? Image)> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Disegna il foglio intero (fondo bianco compreso) su una tela con l'origine nell'angolo in alto a sinistra del foglio
    /// e <paramref name="unitsPerMm"/> unità della tela per millimetro.
    /// </summary>
    public static void Render(SKCanvas canvas, Scene scene, PlotLayout layout, double unitsPerMm)
    {
        var settings = layout.Settings;
        canvas.Save();
        canvas.Clear(SKColors.White);
        var (px, py, pw, ph) = layout.Printable;
        canvas.ClipRect(new SKRect((float)(px * unitsPerMm), (float)(py * unitsPerMm), (float)((px + pw) * unitsPerMm), (float)((py + ph) * unitsPerMm)));

        var m = layout.WorldToPaper * Matrix2D.Scaling(unitsPerMm, unitsPerMm);
        var visible = layout.VisibleWorld;
        DrawImages(canvas, scene, m, visible);

        using (var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            foreach (var batch in scene.Fills)
            {
                fill.Color = PaperColor(batch.Color, settings.Monochrome);
                using var path = new SKPath { FillType = SKPathFillType.Winding };
                foreach (var ring in batch.Rings)
                {
                    AddPolyline(path, ring, m, close: true);
                }

                canvas.DrawPath(path, fill);
            }
        }

        using (var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
        {
            foreach (var batch in scene.Batches)
            {
                var weight = settings.Lineweights ? Math.Max(batch.Weight, ThinLine) : ThinLine;
                stroke.StrokeWidth = (float)(weight * unitsPerMm);
                stroke.Color = PaperColor(batch.Color, settings.Monochrome);
                using var path = new SKPath();
                foreach (var polyline in batch.Polylines)
                {
                    if (polyline.Length > 1 && visible.Intersects(BoundingBox.FromPoints(polyline)))
                    {
                        AddPolyline(path, polyline, m, close: false);
                    }
                }

                canvas.DrawPath(path, stroke);
            }

            stroke.StrokeWidth = (float)(ThinLine * unitsPerMm);
            foreach (var point in scene.Points.Where(p => visible.Contains(p.Position)))
            {
                var p = m.Transform(point.Position);
                var r = (float)(PointMarker / 2 * unitsPerMm);
                stroke.Color = PaperColor(point.Color, settings.Monochrome);
                canvas.DrawLine((float)p.X - r, (float)p.Y, (float)p.X + r, (float)p.Y, stroke);
                canvas.DrawLine((float)p.X, (float)p.Y - r, (float)p.X, (float)p.Y + r, stroke);
            }
        }

        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, SubpixelText = true })
        {
            var scale = layout.PaperPerUnit * unitsPerMm;
            foreach (var text in scene.TextIndex.Query(visible))
            {
                var p = m.Transform(text.Position);
                paint.Color = PaperColor(text.Color, settings.Monochrome);
                TextPainter.Draw(canvas, text, p.X, p.Y, text.Height * scale, paint);
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// Colore sulla carta bianca: il bianco (colore 7) diventa nero come negli altri CAD; in bianco e nero tutto è nero.
    /// </summary>
    public static SKColor PaperColor(CadColor color, bool monochrome) =>
        monochrome || color is { R: 255, G: 255, B: 255 } ? SKColors.Black : new SKColor(color.R, color.G, color.B);

    private static void AddPolyline(SKPath path, Vector2[] points, Matrix2D m, bool close)
    {
        var first = m.Transform(points[0]);
        path.MoveTo((float)first.X, (float)first.Y);
        for (var i = 1; i < points.Length; i++)
        {
            var p = m.Transform(points[i]);
            path.LineTo((float)p.X, (float)p.Y);
        }

        if (close)
        {
            path.Close();
        }
    }

    private static void DrawImages(SKCanvas canvas, Scene scene, Matrix2D m, BoundingBox visible)
    {
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        foreach (var item in scene.Images)
        {
            if (!visible.Intersects(item.Bounds) || LoadImage(item.Path) is not { } image)
            {
                continue;
            }

            var t = item.PixelToWorld(image.Width, image.Height) * m;
            var matrix = new SKMatrix((float)t.M11, (float)t.M21, (float)t.OffsetX, (float)t.M12, (float)t.M22, (float)t.OffsetY, 0, 0, 1);
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(item.Opacity, 0, 1) * 255));
            canvas.Save();
            canvas.Concat(ref matrix);
            canvas.DrawImage(image, 0, 0, paint);
            canvas.Restore();
        }
    }

    /// <summary>Immagine decodificata, ricaricata se il file cambia; null se manca o non si legge (resta la cornice).</summary>
    public static SKImage? LoadImage(string path)
    {
        var modified = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (ImageCache)
        {
            if (ImageCache.TryGetValue(path, out var cached) && cached.Modified == modified)
            {
                return cached.Image;
            }

            SKImage? image = null;
            try
            {
                if (modified != DateTime.MinValue)
                {
                    using var data = SKData.Create(path);
                    image = data is null ? null : SKImage.FromEncodedData(data);
                }
            }
            catch (IOException)
            {
            }

            cached.Image?.Dispose();
            ImageCache[path] = (modified, image);
            return image;
        }
    }
}
