using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Cad.Document;
using Cad.Geometry;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// Disegna la scena con SkiaSharp sul thread di rendering di Avalonia.
/// </summary>
internal sealed class SceneDrawOperation(Rect bounds, SceneGeometry geometry, Matrix2D worldToScreen, BoundingBox visibleWorld)
    : ICustomDrawOperation
{
    /// <summary>Testi più bassi di così (in pixel) non sono leggibili e non si disegnano.</summary>
    private const double MinTextPixels = 2;

    private const float PointMarkerPixels = 3;

    private static readonly SKColor Background = new(0x21, 0x21, 0x21);
    private static readonly SKColor AxisXColor = new(0xC8, 0x3C, 0x3C);
    private static readonly SKColor AxisYColor = new(0x3C, 0xB4, 0x3C);
    private static readonly SKTypeface TextTypeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
    private static readonly float CapHeightRatio = MeasureCapHeightRatio();

    public Rect Bounds => bounds;

    public bool HitTest(Point p) => bounds.Contains(p);

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null)
        {
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
        canvas.Clear(Background);

        DrawOriginMarker(canvas);
        DrawPaths(canvas);
        DrawPoints(canvas);
        DrawTexts(canvas);

        canvas.Restore();
    }

    private void DrawPaths(SKCanvas canvas)
    {
        var m = Matrix2D.Translation(geometry.Origin) * worldToScreen;
        var matrix = new SKMatrix(
            (float)m.M11, (float)m.M21, (float)m.OffsetX,
            (float)m.M12, (float)m.M22, (float)m.OffsetY,
            0, 0, 1);

        canvas.Save();
        canvas.Concat(ref matrix);
        // StrokeWidth 0 = linea sottile di un pixel a qualunque livello di zoom.
        using var paint = new SKPaint { StrokeWidth = 0, Style = SKPaintStyle.Stroke, IsAntialias = true };
        foreach (var (color, path) in geometry.Paths)
        {
            paint.Color = color;
            canvas.DrawPath(path, paint);
        }

        canvas.Restore();
    }

    private void DrawPoints(SKCanvas canvas)
    {
        using var paint = new SKPaint { StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true };
        foreach (var point in geometry.Scene.Points)
        {
            if (!visibleWorld.Contains(point.Position))
            {
                continue;
            }

            var p = worldToScreen.Transform(point.Position);
            paint.Color = SceneGeometry.ToSkColor(point.Color);
            canvas.DrawLine((float)p.X - PointMarkerPixels, (float)p.Y, (float)p.X + PointMarkerPixels, (float)p.Y, paint);
            canvas.DrawLine((float)p.X, (float)p.Y - PointMarkerPixels, (float)p.X, (float)p.Y + PointMarkerPixels, paint);
        }
    }

    private void DrawTexts(SKCanvas canvas)
    {
        var scale = worldToScreen.TransformVector(Vector2.UnitX).Length;
        using var paint = new SKPaint { Typeface = TextTypeface, IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var text in geometry.Scene.TextIndex.Query(visibleWorld))
        {
            var pixels = text.Height * scale;
            if (pixels < MinTextPixels)
            {
                continue;
            }

            var p = worldToScreen.Transform(text.Position);
            paint.Color = SceneGeometry.ToSkColor(text.Color);
            paint.TextSize = (float)(pixels / CapHeightRatio);
            paint.TextAlign = text.Alignment switch
            {
                TextHorizontalAlignment.Center => SKTextAlign.Center,
                TextHorizontalAlignment.Right => SKTextAlign.Right,
                _ => SKTextAlign.Left,
            };

            canvas.Save();
            canvas.Translate((float)p.X, (float)p.Y);
            // Lo schermo ha la Y verso il basso: la rotazione antioraria del disegno diventa negativa.
            canvas.RotateRadians((float)-text.Rotation);
            canvas.Scale((float)text.WidthFactor, 1);
            canvas.DrawText(text.Text, 0, 0, paint);
            canvas.Restore();
        }
    }

    private void DrawOriginMarker(SKCanvas canvas)
    {
        const float length = 40;
        var origin = worldToScreen.Transform(Vector2.Zero);
        var x = (float)origin.X;
        var y = (float)origin.Y;
        using var paint = new SKPaint { StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
        paint.Color = AxisXColor;
        canvas.DrawLine(x, y, x + length, y, paint);
        paint.Color = AxisYColor;
        canvas.DrawLine(x, y, x, y - length, paint);
    }

    /// <summary>Altezza delle maiuscole rispetto alla dimensione del font: l'altezza dei testi CAD si riferisce alle maiuscole.</summary>
    private static float MeasureCapHeightRatio()
    {
        using var paint = new SKPaint { Typeface = TextTypeface, TextSize = 100 };
        var capHeight = paint.FontMetrics.CapHeight;
        return capHeight > 0 ? capHeight / 100 : 0.7f;
    }
}
