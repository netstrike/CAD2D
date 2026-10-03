using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using Cad.Plot;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// Disegna la scena e la sovrapposizione con SkiaSharp sul thread di rendering di Avalonia.
/// </summary>
internal sealed class SceneDrawOperation(Rect bounds, SceneGeometry geometry, Overlay overlay, Matrix2D worldToScreen, BoundingBox visibleWorld, SceneRaster? raster = null)
    : ICustomDrawOperation
{
    /// <summary>Testi più bassi di così (in pixel) non sono leggibili e non si disegnano.</summary>
    private const double MinTextPixels = 2;

    private const float PointMarkerPixels = 3;
    private const float GripPixels = 4;
    private const float SnapMarkerPixels = 6;
    private const float PickBoxPixels = 4;

    private static readonly SKColor AxisXColor = new(0xC8, 0x3C, 0x3C);
    private static readonly SKColor AxisYColor = new(0x3C, 0xB4, 0x3C);
    private static readonly SKColor HighlightColor = new(0x4F, 0xA3, 0xFF);
    private static readonly SKColor GripColor = new(0x2F, 0x6F, 0xFF);
    private static readonly SKColor SnapColor = new(0xFF, 0xC0, 0x20);
    /// <summary>Pixel per millimetro di spessore quando gli spessori sono visibili.</summary>
    private const double LineweightPixelsPerMm = 4;

    private static readonly SKColor TrackingColor = new(0x60, 0xD0, 0x60);
    private static readonly SKTypeface LabelTypeface = SKTypeface.FromFamilyName("Segoe UI") ?? TextPainter.DefaultTypeface;

    private bool Light => geometry.Light;
    private SKColor Background => Light ? SKColors.White : new SKColor(0x21, 0x21, 0x21);
    private SKColor CrosshairColor => Light ? new SKColor(0x30, 0x30, 0x30) : new SKColor(0xC8, 0xC8, 0xC8);

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

        // Il disegno si ridisegna solo se cambiano scena o vista: muovere il cursore ridipinge solo la sovrapposizione.
        var device = canvas.TotalMatrix;
        if (raster is null || device.SkewX != 0 || device.SkewY != 0 ||
            !raster.Draw(canvas, new SceneRaster.Key(geometry, overlay.Highlight, worldToScreen, bounds.Width, bounds.Height, device.ScaleX, device.ScaleY, overlay.GridSpacing, overlay.ShowLineweights, lease.GrContext), DrawScene))
        {
            DrawScene(canvas);
        }

        DrawOverlay(canvas);
        canvas.Restore();
    }

    private void DrawScene(SKCanvas canvas)
    {
        canvas.Clear(Background);
        DrawGrid(canvas);
        DrawOriginMarker(canvas);
        DrawImages(canvas);
        DrawPaths(canvas);
        DrawPoints(canvas);
        DrawTexts(canvas);
        DrawSelection(canvas);
    }

    /// <summary>Selezione: le entità selezionate ripassate tratteggiate. Cambia solo con la selezione, quindi sta con il disegno.</summary>
    private void DrawSelection(SKCanvas canvas)
    {
        if (overlay.Highlight.Count == 0)
        {
            return;
        }

        using var dash = SKPathEffect.CreateDash([6, 4], 0);
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.5f, Color = HighlightColor, PathEffect = dash };
        DrawPolylines(canvas, overlay.Highlight, stroke);
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

        // StrokeWidth 0 = linea sottile di un pixel a qualunque livello di zoom. Con gli spessori visibili (LWT) lo
        // spessore è in pixel dello schermo, come in DraftSight: fino a 0,25 mm resta un pixel.
        var scale = Math.Sqrt(Math.Abs(m.Determinant));
        using var paint = new SKPaint { StrokeWidth = 0, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var (color, path, weight) in geometry.Paths)
        {
            paint.Color = color;
            var pixels = overlay.ShowLineweights && weight > 0.25 ? weight * LineweightPixelsPerMm : 0;
            paint.StrokeWidth = pixels > 1 && scale > 0 ? (float)(pixels / scale) : 0;
            canvas.DrawPath(path, paint);
        }

        canvas.Restore();
    }

    private void DrawImages(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        foreach (var item in geometry.Scene.Images)
        {
            if (!visibleWorld.Intersects(item.Bounds) || PlotRenderer.LoadImage(item.Path) is not { } image)
            {
                continue;
            }

            var m = item.PixelToWorld(image.Width, image.Height) * worldToScreen;
            var matrix = new SKMatrix(
                (float)m.M11, (float)m.M21, (float)m.OffsetX,
                (float)m.M12, (float)m.M22, (float)m.OffsetY,
                0, 0, 1);
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(item.Opacity, 0, 1) * 255));
            canvas.Save();
            canvas.Concat(ref matrix);
            canvas.DrawImage(image, 0, 0, paint);
            canvas.Restore();
        }
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
            paint.Color = SceneGeometry.ToSkColor(point.Color, Light);
            canvas.DrawLine((float)p.X - PointMarkerPixels, (float)p.Y, (float)p.X + PointMarkerPixels, (float)p.Y, paint);
            canvas.DrawLine((float)p.X, (float)p.Y - PointMarkerPixels, (float)p.X, (float)p.Y + PointMarkerPixels, paint);
        }
    }

    private void DrawTexts(SKCanvas canvas)
    {
        var scale = worldToScreen.TransformVector(Vector2.UnitX).Length;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var text in geometry.Scene.TextIndex.Query(visibleWorld))
        {
            var pixels = text.Height * scale;
            if (pixels < MinTextPixels)
            {
                continue;
            }

            var p = worldToScreen.Transform(text.Position);
            paint.Color = SceneGeometry.ToSkColor(text.Color, Light);
            TextPainter.Draw(canvas, text, p.X, p.Y, pixels, paint);
        }
    }

    private void DrawOverlay(SKCanvas canvas)
    {
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };

        // Oggetto sotto il cursore: ripassato più spesso, prima del clic.
        if (overlay.Hover.Count > 0)
        {
            stroke.Color = HighlightColor;
            stroke.StrokeWidth = 2.5f;
            DrawPolylines(canvas, overlay.Hover, stroke);
            stroke.StrokeWidth = 1;
        }

        foreach (var (color, points) in overlay.Preview)
        {
            stroke.Color = SceneGeometry.ToSkColor(color, Light);
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

        DrawTracking(canvas);

        if (overlay.Snap is { } snap)
        {
            DrawSnapMarker(canvas, snap);
            DrawSnapLabel(canvas, snap);
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

    /// <summary>
    /// Griglia a punti, con un punto più marcato ogni cinque. Se i punti sarebbero troppo fitti il passo si moltiplica
    /// per cinque finché restano leggibili, come negli altri CAD.
    /// </summary>
    private void DrawGrid(SKCanvas canvas)
    {
        var spacing = overlay.GridSpacing;
        if (spacing <= 0 || visibleWorld.IsEmpty)
        {
            return;
        }

        var pixels = worldToScreen.TransformVector(new Vector2(spacing, 0)).Length;
        var step = 1;
        while (pixels * step < 10 && step < 1_000_000)
        {
            step *= 5;
        }

        var world = spacing * step;
        var x0 = (long)Math.Floor(visibleWorld.Min.X / world);
        var x1 = (long)Math.Ceiling(visibleWorld.Max.X / world);
        var y0 = (long)Math.Floor(visibleWorld.Min.Y / world);
        var y1 = (long)Math.Ceiling(visibleWorld.Max.Y / world);
        if ((x1 - x0) * (y1 - y0) > 40_000)
        {
            return;
        }

        using var minor = new SKPaint { Style = SKPaintStyle.Fill, Color = Light ? new SKColor(0xC0, 0xC0, 0xC0) : new SKColor(0x5A, 0x5A, 0x5A) };
        using var major = new SKPaint { Style = SKPaintStyle.Fill, Color = Light ? new SKColor(0x90, 0x90, 0x90) : new SKColor(0x80, 0x80, 0x80) };
        for (var i = x0; i <= x1; i++)
        {
            for (var j = y0; j <= y1; j++)
            {
                var p = worldToScreen.Transform(new Vector2(i * world, j * world));
                var strong = i % 5 == 0 && j % 5 == 0;
                canvas.DrawRect((float)p.X - 0.5f, (float)p.Y - 0.5f, strong ? 2 : 1, strong ? 2 : 1, strong ? major : minor);
            }
        }
    }

    /// <summary>Guide di tracciamento punteggiate, croci sui punti acquisiti e didascalia con distanza e angolo.</summary>
    private void DrawTracking(SKCanvas canvas)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1, Color = TrackingColor };
        foreach (var point in overlay.Acquired)
        {
            var p = worldToScreen.Transform(point);
            canvas.DrawLine((float)p.X - 4, (float)p.Y, (float)p.X + 4, (float)p.Y, paint);
            canvas.DrawLine((float)p.X, (float)p.Y - 4, (float)p.X, (float)p.Y + 4, paint);
        }

        if (overlay.TrackingLines.Count == 0)
        {
            return;
        }

        using var dots = SKPathEffect.CreateDash([2, 4], 0);
        paint.PathEffect = dots;
        var reach = Math.Max(bounds.Width, bounds.Height) * 2;
        foreach (var line in overlay.TrackingLines)
        {
            var a = worldToScreen.Transform(line.Origin);
            var direction = worldToScreen.TransformVector(line.Direction).Normalized();
            var b = a + direction * reach;
            canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint);
        }

        paint.PathEffect = null;
        if (overlay.TrackingPoint is { } at)
        {
            var p = worldToScreen.Transform(at);
            canvas.DrawLine((float)p.X - 5, (float)p.Y - 5, (float)p.X + 5, (float)p.Y + 5, paint);
            canvas.DrawLine((float)p.X - 5, (float)p.Y + 5, (float)p.X + 5, (float)p.Y - 5, paint);
            if (overlay.TrackingLabel is { } label)
            {
                using var text = new SKPaint { Typeface = LabelTypeface, TextSize = 11, IsAntialias = true, Color = new SKColor(0x20, 0x20, 0x20) };
                var width = text.MeasureText(label);
                // Sopra il cursore, dove non copre l'inserimento rapido; vicino al bordo passa a sinistra.
                var x = (float)p.X + 14;
                if (x + width + 6 > bounds.Width)
                {
                    x = (float)p.X - 14 - width;
                }

                var y = (float)p.Y - 12;
                using var back = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0xB8, 0xF0, 0xB8, 0xE8) };
                canvas.DrawRoundRect(x - 3, y - 11, width + 6, 15, 2, 2, back);
                canvas.DrawText(label, x, y, text);
            }
        }
    }

    /// <summary>Nome dello snap accanto al simbolo, come la didascalia di DraftSight.</summary>
    private void DrawSnapLabel(SKCanvas canvas, SnapResult snap)
    {
        var label = snap.Kind switch
        {
            SnapModes.Endpoint => "Estremo",
            SnapModes.Midpoint => "Medio",
            SnapModes.Center => "Centro",
            SnapModes.Quadrant => "Quadrante",
            SnapModes.Intersection => "Intersezione",
            SnapModes.Perpendicular => "Perpendicolare",
            SnapModes.Tangent => "Tangente",
            SnapModes.Node => "Nodo",
            SnapModes.Nearest => "Vicino",
            _ => null,
        };
        if (label is null)
        {
            return;
        }

        var p = worldToScreen.Transform(snap.Point);
        using var text = new SKPaint { Typeface = LabelTypeface, TextSize = 11, IsAntialias = true, Color = new SKColor(0x20, 0x20, 0x20) };
        var width = text.MeasureText(label);
        var x = (float)p.X + 12;
        var y = (float)p.Y + 14;
        using var back = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xE0, 0x80, 0xE8) };
        canvas.DrawRoundRect(x - 3, y - 11, width + 6, 15, 2, 2, back);
        canvas.DrawText(label, x, y, text);
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
}

/// <summary>
/// Il disegno già dipinto in una superficie fuori schermo, grande quanto la vista in pixel del dispositivo. Finché scena e
/// vista non cambiano basta ricopiarla: con centomila entità muovere il cursore non ridisegna tutto. Si usa solo dal thread
/// di rendering.
/// </summary>
internal sealed class SceneRaster : IDisposable
{
    private SKSurface? _surface;
    private Key? _key;

    public readonly record struct Key(
        SceneGeometry Geometry, IReadOnlyList<Vector2[]> Highlight, Matrix2D View, double Width, double Height, float ScaleX, float ScaleY, double Grid, bool Lineweights, GRContext? Context);

    /// <summary>Copia il disegno sulla tela, ridipingendolo prima se serve; false se la superficie non si può creare.</summary>
    public bool Draw(SKCanvas canvas, Key key, Action<SKCanvas> paint)
    {
        if (_key != key || _surface is null)
        {
            var width = (int)Math.Ceiling(key.Width * key.ScaleX);
            var height = (int)Math.Ceiling(key.Height * key.ScaleY);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (_surface is null || _key is not { } old || old.Context != key.Context ||
                (int)Math.Ceiling(old.Width * old.ScaleX) != width || (int)Math.Ceiling(old.Height * old.ScaleY) != height)
            {
                _surface?.Dispose();
                var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                _surface = key.Context is { } context ? SKSurface.Create(context, false, info) : SKSurface.Create(info);
                if (_surface is null)
                {
                    _key = null;
                    return false;
                }
            }

            var target = _surface.Canvas;
            target.ResetMatrix();
            target.Scale(key.ScaleX, key.ScaleY);
            paint(target);
            target.Flush();
            _key = key;
        }

        canvas.Save();
        canvas.Scale(1 / key.ScaleX, 1 / key.ScaleY);
        canvas.DrawSurface(_surface, 0, 0);
        canvas.Restore();
        return true;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
        _key = null;
    }
}
