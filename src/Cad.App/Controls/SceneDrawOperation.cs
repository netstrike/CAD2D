using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Cad.Geometry;
using SkiaSharp;

namespace Cad.App.Controls;

/// <summary>
/// Disegna la scena con SkiaSharp sul thread di rendering di Avalonia.
/// </summary>
internal sealed class SceneDrawOperation(Rect bounds, SceneGeometry geometry, Matrix2D worldToScreen) : ICustomDrawOperation
{
    private static readonly SKColor Background = new(0x21, 0x21, 0x21);
    private static readonly SKColor LineColor = new(0xE6, 0xE6, 0xE6);
    private static readonly SKColor AxisXColor = new(0xC8, 0x3C, 0x3C);
    private static readonly SKColor AxisYColor = new(0x3C, 0xB4, 0x3C);

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

        var matrix = new SKMatrix(
            (float)worldToScreen.M11, (float)worldToScreen.M21, (float)worldToScreen.OffsetX,
            (float)worldToScreen.M12, (float)worldToScreen.M22, (float)worldToScreen.OffsetY,
            0, 0, 1);
        canvas.Concat(ref matrix);

        // StrokeWidth 0 = linea sottile di un pixel a qualunque livello di zoom.
        using var paint = new SKPaint { Color = LineColor, StrokeWidth = 0, Style = SKPaintStyle.Stroke, IsAntialias = true };
        canvas.DrawPath(geometry.Path, paint);

        canvas.Restore();
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
