using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// Disegna la scena e la sovrapposizione con SkiaSharp sul thread di rendering di Avalonia.
/// </summary>
internal sealed class SceneDrawOperation(Rect bounds, SceneGeometry geometry, Overlay overlay, Matrix2D worldToScreen, BoundingBox visibleWorld)
    : ICustomDrawOperation
{
    /// <summary>Testi più bassi di così (in pixel) non sono leggibili e non si disegnano.</summary>
    private const double MinTextPixels = 2;

    private const float PointMarkerPixels = 3;
    private const float GripPixels = 4;
    private const float SnapMarkerPixels = 6;
    private const float PickBoxPixels = 4;

    private static readonly SKColor Background = new(0x21, 0x21, 0x21);
    private static readonly SKColor AxisXColor = new(0xC8, 0x3C, 0x3C);
    private static readonly SKColor AxisYColor = new(0x3C, 0xB4, 0x3C);
    private static readonly SKColor HighlightColor = new(0x4F, 0xA3, 0xFF);
    private static readonly SKColor GripColor = new(0x2F, 0x6F, 0xFF);
    private static readonly SKColor SnapColor = new(0xFF, 0xC0, 0x20);
    private static readonly SKColor CrosshairColor = new(0xC8, 0xC8, 0xC8);
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
        DrawOverlay(canvas);

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
        using (var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            foreach (var (color, path) in geometry.Fills)
            {
                fill.Color = color;
                canvas.DrawPath(path, fill);
            }
        }

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

    private void DrawOverlay(SKCanvas canvas)
    {
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };

        // Selezione: le entità selezionate ripassate tratteggiate.
        if (overlay.Highlight.Count > 0)
        {
            using var dash = SKPathEffect.CreateDash([6, 4], 0);
            stroke.Color = HighlightColor;
            stroke.StrokeWidth = 1.5f;
            stroke.PathEffect = dash;
            DrawPolylines(canvas, overlay.Highlight, stroke);
            stroke.PathEffect = null;
            stroke.StrokeWidth = 1;
        }

        foreach (var (color, points) in overlay.Preview)
        {
            stroke.Color = SceneGeometry.ToSkColor(color);
            DrawPolylines(canvas, [points], stroke);
        }

        if (overlay.RubberBandFrom is { } from && overlay.RubberBandTo is { } to)
        {
            using var dash = SKPathEffect.CreateDash([4, 4], 0);
            stroke.Color = CrosshairColor.WithAlpha(160);
            stroke.PathEffect = dash;
            var a = worldToScreen.Transform(from);
            var b = worldToScreen.Transform(to);
            canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, stroke);
            stroke.PathEffect = null;
        }

        if (overlay.Window is { } window)
        {
            DrawSelectionWindow(canvas, window.Start, window.End);
        }

        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = GripColor, IsAntialias = false };
        foreach (var grip in overlay.Grips)
        {
            var p = worldToScreen.Transform(grip);
            canvas.DrawRect((float)p.X - GripPixels, (float)p.Y - GripPixels, 2 * GripPixels, 2 * GripPixels, fill);
        }

        if (overlay.Snap is { } snap)
        {
            DrawSnapMarker(canvas, snap);
        }

        if (overlay.Crosshair is { } cross)
        {
            var c = worldToScreen.Transform(cross);
            stroke.Color = CrosshairColor;
            canvas.DrawLine(0, (float)c.Y, (float)bounds.Width, (float)c.Y, stroke);
            canvas.DrawLine((float)c.X, 0, (float)c.X, (float)bounds.Height, stroke);
            canvas.DrawRect((float)c.X - PickBoxPixels, (float)c.Y - PickBoxPixels, 2 * PickBoxPixels, 2 * PickBoxPixels, stroke);
        }
    }

    private void DrawPolylines(SKCanvas canvas, IReadOnlyList<Vector2[]> polylines, SKPaint paint)
    {
        using var path = new SKPath();
        foreach (var polyline in polylines)
        {
            for (var i = 0; i < polyline.Length; i++)
            {
                var p = worldToScreen.Transform(polyline[i]);
                if (i == 0)
                {
                    path.MoveTo((float)p.X, (float)p.Y);
                }
                else
                {
                    path.LineTo((float)p.X, (float)p.Y);
                }
            }
        }

        canvas.DrawPath(path, paint);
    }

    /// <summary>Finestra (da sinistra a destra): blu continua. Interseca (da destra a sinistra): verde tratteggiata.</summary>
    private void DrawSelectionWindow(SKCanvas canvas, Vector2 start, Vector2 end)
    {
        var a = worldToScreen.Transform(start);
        var b = worldToScreen.Transform(end);
        var rect = SKRect.Create((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Abs(b.X - a.X), (float)Math.Abs(b.Y - a.Y));
        var crossing = end.X < start.X;
        var color = crossing ? new SKColor(0x40, 0xC0, 0x60) : new SKColor(0x40, 0x80, 0xFF);
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(40) };
        using var border = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 1 };
        using var dash = crossing ? SKPathEffect.CreateDash([5, 3], 0) : null;
        border.PathEffect = dash;
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, border);
    }

    /// <summary>Simboli come nei CAD: quadrato estremo, triangolo medio, cerchio centro, X intersezione, angolo retto perpendicolare.</summary>
    private void DrawSnapMarker(SKCanvas canvas, SnapResult snap)
    {
        var p = worldToScreen.Transform(snap.Point);
        var x = (float)p.X;
        var y = (float)p.Y;
        const float s = SnapMarkerPixels;
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, Color = SnapColor, StrokeWidth = 2, IsAntialias = true };
        switch (snap.Kind)
        {
            case SnapModes.Endpoint:
                canvas.DrawRect(x - s, y - s, 2 * s, 2 * s, paint);
                break;
            case SnapModes.Midpoint:
                using (var triangle = new SKPath())
                {
                    triangle.MoveTo(x, y - s);
                    triangle.LineTo(x + s, y + s);
                    triangle.LineTo(x - s, y + s);
                    triangle.Close();
                    canvas.DrawPath(triangle, paint);
                }

                break;
            case SnapModes.Center:
                canvas.DrawCircle(x, y, s, paint);
                break;
            case SnapModes.Intersection:
                canvas.DrawLine(x - s, y - s, x + s, y + s, paint);
                canvas.DrawLine(x - s, y + s, x + s, y - s, paint);
                break;
            case SnapModes.Perpendicular:
                canvas.DrawLine(x - s, y + s, x + s, y + s, paint);
                canvas.DrawLine(x - s, y + s, x - s, y - s, paint);
                canvas.DrawLine(x - s, y, x, y, paint);
                canvas.DrawLine(x, y, x, y + s, paint);
                break;
            case SnapModes.Quadrant:
                using (var diamond = new SKPath())
                {
                    diamond.MoveTo(x, y - s);
                    diamond.LineTo(x + s, y);
                    diamond.LineTo(x, y + s);
                    diamond.LineTo(x - s, y);
                    diamond.Close();
                    canvas.DrawPath(diamond, paint);
                }

                break;
            case SnapModes.Tangent:
                canvas.DrawCircle(x, y, s * 0.8f, paint);
                canvas.DrawLine(x - s, y - s, x + s, y - s, paint);
                break;
            case SnapModes.Nearest:
                using (var hourglass = new SKPath())
                {
                    hourglass.MoveTo(x - s, y - s);
                    hourglass.LineTo(x + s, y - s);
                    hourglass.LineTo(x - s, y + s);
                    hourglass.LineTo(x + s, y + s);
                    hourglass.Close();
                    canvas.DrawPath(hourglass, paint);
                }

                break;
            default:
                canvas.DrawCircle(x, y, s, paint);
                canvas.DrawLine(x - s, y - s, x + s, y + s, paint);
                canvas.DrawLine(x - s, y + s, x + s, y - s, paint);
                break;
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
