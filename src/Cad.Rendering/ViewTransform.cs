using Cad.Geometry;

namespace Cad.Rendering;

/// <summary>
/// Trasformazione tra coordinate mondo (Y verso l'alto) e coordinate schermo (pixel, Y verso il basso).
/// Il centro della vista e la scala definiscono cosa si vede; pan e zoom modificano solo questi due valori.
/// </summary>
public sealed class ViewTransform
{
    public const double MinScale = 1e-6;
    public const double MaxScale = 1e6;

    /// <summary>Punto mondo che appare al centro del viewport.</summary>
    public Vector2 Center { get; private set; } = Vector2.Zero;

    /// <summary>Pixel per unità di disegno.</summary>
    public double Scale { get; private set; } = 1;

    public double ViewportWidth { get; private set; } = 1;
    public double ViewportHeight { get; private set; } = 1;

    public void SetViewport(double width, double height)
    {
        ViewportWidth = Math.Max(1, width);
        ViewportHeight = Math.Max(1, height);
    }

    public Matrix2D WorldToScreenMatrix => new(
        Scale, 0,
        0, -Scale,
        ViewportWidth / 2 - Center.X * Scale,
        ViewportHeight / 2 + Center.Y * Scale);

    public Vector2 WorldToScreen(Vector2 world) => WorldToScreenMatrix.Transform(world);

    public Vector2 ScreenToWorld(Vector2 screen) => new(
        Center.X + (screen.X - ViewportWidth / 2) / Scale,
        Center.Y - (screen.Y - ViewportHeight / 2) / Scale);

    /// <summary>Area di mondo visibile nel viewport.</summary>
    public BoundingBox VisibleWorldBounds =>
        BoundingBox.FromPoints(ScreenToWorld(Vector2.Zero), ScreenToWorld(new Vector2(ViewportWidth, ViewportHeight)));

    /// <summary>Sposta la vista di un delta espresso in pixel (trascinamento del mouse).</summary>
    public void PanByScreen(Vector2 screenDelta)
    {
        Center -= new Vector2(screenDelta.X / Scale, -screenDelta.Y / Scale);
    }

    /// <summary>Zoom attorno a un punto dello schermo, che resta fermo sotto il cursore.</summary>
    public void ZoomAt(Vector2 screenPoint, double factor)
    {
        var anchor = ScreenToWorld(screenPoint);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        var moved = ScreenToWorld(screenPoint);
        Center += anchor - moved;
    }

    /// <summary>Riporta la vista a un centro e a una scala salvati (per esempio tornando a un disegno già aperto).</summary>
    public void SetView(Vector2 center, double scale)
    {
        Center = center;
        Scale = Math.Clamp(scale, MinScale, MaxScale);
    }

    /// <summary>Inquadra <paramref name="bounds"/> lasciando un margine in pixel su ogni lato.</summary>
    public void ZoomExtents(BoundingBox bounds, double marginPixels = 20)
    {
        if (bounds.IsEmpty)
        {
            Center = Vector2.Zero;
            Scale = 1;
            return;
        }

        var usableWidth = Math.Max(1, ViewportWidth - 2 * marginPixels);
        var usableHeight = Math.Max(1, ViewportHeight - 2 * marginPixels);
        var scaleX = bounds.Width > 0 ? usableWidth / bounds.Width : double.PositiveInfinity;
        var scaleY = bounds.Height > 0 ? usableHeight / bounds.Height : double.PositiveInfinity;
        var scale = Math.Min(scaleX, scaleY);

        Center = bounds.Center;
        Scale = double.IsInfinity(scale) ? 1 : Math.Clamp(scale, MinScale, MaxScale);
    }
}
