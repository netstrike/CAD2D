using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class EntityTransformTests
{
    private static readonly Layer Layer0 = new("0");

    [Fact]
    public void Rotated_arc_keeps_its_shape()
    {
        var arc = new ArcEntity(Layer0, Vector2.Zero, 2, 0, Math.PI / 2);
        var rotated = (ArcEntity)arc.Transformed(Matrix2D.Rotation(Math.PI / 2));
        Assert.True(rotated.Arc.StartPoint.IsAlmostEqual(new Vector2(0, 2), 1e-9));
        Assert.True(rotated.Arc.EndPoint.IsAlmostEqual(new Vector2(-2, 0), 1e-9));
    }

    [Fact]
    public void Mirrored_arc_swaps_its_ends()
    {
        var arc = new ArcEntity(Layer0, Vector2.Zero, 1, 0, Math.PI / 2);
        var mirrored = (ArcEntity)arc.Transformed(Matrix2D.Scaling(-1, 1));
        // L'arco da (1,0) a (0,1) diventa quello da (0,1) a (-1,0).
        Assert.True(mirrored.Arc.Midpoint.IsAlmostEqual(Vector2.FromPolar(1, 3 * Math.PI / 4), 1e-9));
    }

    [Fact]
    public void Transformed_entity_keeps_layer_and_color_but_not_source()
    {
        var layer = new Layer("X");
        var line = new LineEntity(layer, Vector2.Zero, Vector2.UnitX) { Color = EntityColor.Explicit(new CadColor(1, 2, 3)), SourceTag = "orig" };
        var moved = line.Transformed(Matrix2D.Translation(new Vector2(1, 1)));
        Assert.Same(layer, moved.Layer);
        Assert.Equal(line.Color, moved.Color);
        Assert.Null(moved.SourceTag);
    }

    [Fact]
    public void Circle_quadrant_grip_changes_radius()
    {
        var circle = new CircleEntity(Layer0, Vector2.Zero, 1);
        var bigger = (CircleEntity)circle.WithGripMoved(1, new Vector2(3, 0));
        Assert.Equal(3, bigger.Radius);
        Assert.Equal(Vector2.Zero, bigger.Center);
    }

    [Fact]
    public void Polyline_vertex_grip_moves_one_vertex()
    {
        var polyline = new PolylineEntity(Layer0, [new PolylineVertex(Vector2.Zero), new PolylineVertex(Vector2.UnitX), new PolylineVertex(Vector2.UnitY)], true);
        var edited = (PolylineEntity)polyline.WithGripMoved(1, new Vector2(5, 0));
        Assert.Equal(new Vector2(5, 0), edited.Vertices[1].Position);
        Assert.Equal(Vector2.UnitY, edited.Vertices[2].Position);
    }

    [Fact]
    public void Text_rotation_follows_transform()
    {
        var text = new TextEntity(Layer0, Vector2.Zero, 2.5, "A");
        var rotated = (TextEntity)text.Transformed(Matrix2D.Rotation(Math.PI / 6));
        Assert.Equal(Math.PI / 6, rotated.Rotation, 9);
        Assert.Equal(2.5, rotated.Height, 9);
    }
}
