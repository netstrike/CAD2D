using Cad.Document;
using Cad.Geometry;

namespace Cad.Document.Tests;

public class EntityBoundsTests
{
    private static readonly Layer Layer0 = new(Layer.DefaultName);

    [Fact]
    public void Polyline_bounds_include_bulged_segments()
    {
        // Mezzo cerchio sotto la corda da (0,0) a (2,0).
        var polyline = new PolylineEntity(Layer0, [new PolylineVertex(Vector2.Zero, 1), new PolylineVertex(new Vector2(2, 0))], isClosed: false);
        Assert.Equal(-1, polyline.Bounds.Min.Y, 9);
        Assert.Equal(0, polyline.Bounds.Max.Y, 9);
    }

    [Fact]
    public void Insert_bounds_follow_scale_and_position()
    {
        var block = new BlockDefinition("B");
        block.Entities.Add(new CircleEntity(Layer0, Vector2.Zero, 1));
        var insert = new InsertEntity(Layer0, block)
        {
            Transform = InsertEntity.BuildTransform(Vector2.Zero, new Vector2(10, 5), 2, 2, 0),
        };
        Assert.True(insert.Bounds.Min.IsAlmostEqual(new Vector2(8, 3)));
        Assert.True(insert.Bounds.Max.IsAlmostEqual(new Vector2(12, 7)));
    }

    [Fact]
    public void Insert_transform_moves_base_point_onto_position()
    {
        var m = InsertEntity.BuildTransform(new Vector2(5, 5), new Vector2(100, 0), 3, 3, Math.PI / 2);
        Assert.True(m.Transform(new Vector2(5, 5)).IsAlmostEqual(new Vector2(100, 0)));
        Assert.True(m.Transform(new Vector2(6, 5)).IsAlmostEqual(new Vector2(100, 3)));
    }

    [Fact]
    public void Full_ellipse_bounds_are_exact_when_rotated()
    {
        var ellipse = new EllipseEntity(Layer0, Vector2.Zero, new Vector2(0, 4), new Vector2(-2, 0), 0, Math.Tau);
        Assert.True(ellipse.Bounds.Max.IsAlmostEqual(new Vector2(2, 4)));
    }

    [Fact]
    public void Layer_is_visible_only_when_on_and_thawed()
    {
        Assert.True(new Layer("A").IsVisible);
        Assert.False(new Layer("B") { IsOn = false }.IsVisible);
        Assert.False(new Layer("C") { IsFrozen = true }.IsVisible);
    }
}
