using Cad.Geometry;
using Cad.Rendering;

namespace Cad.Rendering.Tests;

public class ViewTransformTests
{
    private static ViewTransform Create()
    {
        var view = new ViewTransform();
        view.SetViewport(800, 600);
        return view;
    }

    [Fact]
    public void World_y_axis_points_up_on_screen()
    {
        var view = Create();
        var above = view.WorldToScreen(new Vector2(0, 10));
        var below = view.WorldToScreen(new Vector2(0, -10));
        Assert.True(above.Y < below.Y);
    }

    [Fact]
    public void Screen_to_world_inverts_world_to_screen()
    {
        var view = Create();
        view.ZoomAt(new Vector2(123, 456), 3.7);
        view.PanByScreen(new Vector2(-40, 25));
        var p = new Vector2(12.5, -7.25);
        Assert.True(p.IsAlmostEqual(view.ScreenToWorld(view.WorldToScreen(p)), 1e-9));
    }

    [Fact]
    public void Zoom_keeps_point_under_cursor_fixed()
    {
        var view = Create();
        var cursor = new Vector2(600, 150);
        var before = view.ScreenToWorld(cursor);
        view.ZoomAt(cursor, 2.5);
        Assert.True(before.IsAlmostEqual(view.ScreenToWorld(cursor), 1e-9));
        Assert.Equal(2.5, view.Scale, 12);
    }

    [Fact]
    public void Pan_moves_content_with_the_mouse()
    {
        var view = Create();
        var p = new Vector2(3, 4);
        var before = view.WorldToScreen(p);
        view.PanByScreen(new Vector2(50, -20));
        var after = view.WorldToScreen(p);
        Assert.True(new Vector2(before.X + 50, before.Y - 20).IsAlmostEqual(after, 1e-9));
    }

    [Fact]
    public void Zoom_extents_fits_bounds_inside_margins()
    {
        var view = Create();
        var bounds = BoundingBox.FromPoints(new Vector2(100, 100), new Vector2(300, 200));
        view.ZoomExtents(bounds, marginPixels: 20);

        var min = view.WorldToScreen(bounds.Min);
        var max = view.WorldToScreen(bounds.Max);
        Assert.Equal(20, min.X, 9);
        Assert.Equal(780, max.X, 9);
        Assert.True(max.Y >= 20 && min.Y <= 580);
    }

    [Fact]
    public void Zoom_is_clamped()
    {
        var view = Create();
        view.ZoomAt(Vector2.Zero, 1e20);
        Assert.Equal(ViewTransform.MaxScale, view.Scale);
    }
}
