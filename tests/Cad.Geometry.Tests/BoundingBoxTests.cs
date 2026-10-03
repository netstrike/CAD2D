using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void Empty_is_neutral_for_union()
    {
        var box = BoundingBox.FromPoints(new Vector2(1, 1), new Vector2(2, 3));
        Assert.Equal(box, BoundingBox.Empty.Union(box));
        Assert.Equal(box, box.Union(BoundingBox.Empty));
    }

    [Fact]
    public void FromPoints_orders_corners()
    {
        var box = BoundingBox.FromPoints(new Vector2(5, -1), new Vector2(-2, 4));
        Assert.Equal(new Vector2(-2, -1), box.Min);
        Assert.Equal(new Vector2(5, 4), box.Max);
    }

    [Fact]
    public void Window_contains_only_fully_inside_boxes()
    {
        var window = BoundingBox.FromPoints(Vector2.Zero, new Vector2(10, 10));
        Assert.True(window.Contains(BoundingBox.FromPoints(new Vector2(1, 1), new Vector2(9, 9))));
        Assert.False(window.Contains(BoundingBox.FromPoints(new Vector2(5, 5), new Vector2(15, 9))));
        Assert.True(window.Intersects(BoundingBox.FromPoints(new Vector2(5, 5), new Vector2(15, 9))));
    }

    [Fact]
    public void Empty_box_intersects_nothing() =>
        Assert.False(BoundingBox.Empty.Intersects(BoundingBox.FromPoints(Vector2.Zero, new Vector2(1, 1))));
}
