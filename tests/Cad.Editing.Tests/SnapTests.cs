using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class SnapTests
{
    private static readonly Layer Layer0 = new("0");

    [Fact]
    public void Endpoint_wins_over_midpoint_when_closer()
    {
        var line = new LineEntity(Layer0, Vector2.Zero, new Vector2(10, 0));
        var snap = SnapEngine.Find([line], new Vector2(9.5, 0.2), 1, SnapModes.Default);
        Assert.Equal(new SnapResult(new Vector2(10, 0), SnapModes.Endpoint), snap);
    }

    [Fact]
    public void Midpoint_of_a_line()
    {
        var line = new LineEntity(Layer0, Vector2.Zero, new Vector2(10, 0));
        Assert.Equal(SnapModes.Midpoint, SnapEngine.Find([line], new Vector2(5.2, 0.1), 1, SnapModes.Default)!.Value.Kind);
    }

    [Fact]
    public void Intersection_of_two_lines()
    {
        var a = new LineEntity(Layer0, new Vector2(0, 0), new Vector2(10, 10));
        var b = new LineEntity(Layer0, new Vector2(0, 4), new Vector2(10, 4));
        var snap = SnapEngine.Find([a, b], new Vector2(4.1, 4.1), 0.5, SnapModes.Default)!.Value;
        Assert.Equal(SnapModes.Intersection, snap.Kind);
        Assert.True(snap.Point.IsAlmostEqual(new Vector2(4, 4), 1e-9));
    }

    [Fact]
    public void Center_of_circle_from_its_circumference()
    {
        var circle = new CircleEntity(Layer0, new Vector2(3, 3), 5);
        var snap = SnapEngine.Find([circle], new Vector2(3 + 5.1 * Math.Cos(0.3), 3 + 5.1 * Math.Sin(0.3)), 0.5, SnapModes.Center)!.Value;
        Assert.Equal(new Vector2(3, 3), snap.Point);
    }

    [Fact]
    public void Perpendicular_from_base_point()
    {
        var line = new LineEntity(Layer0, Vector2.Zero, new Vector2(10, 0));
        var snap = SnapEngine.Find([line], new Vector2(4, 0.2), 1, SnapModes.Perpendicular, basePoint: new Vector2(4, 8))!.Value;
        Assert.True(snap.Point.IsAlmostEqual(new Vector2(4, 0)));
    }

    [Fact]
    public void Nothing_outside_aperture()
    {
        var line = new LineEntity(Layer0, Vector2.Zero, new Vector2(10, 0));
        Assert.Null(SnapEngine.Find([line], new Vector2(5, 3), 1, SnapModes.Default));
    }

    [Fact]
    public void Endpoints_inside_blocks_are_found()
    {
        var block = new BlockDefinition("B");
        block.Entities.Add(new LineEntity(Layer0, Vector2.Zero, new Vector2(1, 0)));
        var insert = new InsertEntity(Layer0, block) { Transform = Matrix2D.Translation(new Vector2(100, 0)) };
        var snap = SnapEngine.Find([insert], new Vector2(101.1, 0), 0.5, SnapModes.Endpoint)!.Value;
        Assert.Equal(new Vector2(101, 0), snap.Point);
    }

    [Fact]
    public void Arc_and_line_intersection_only_on_the_arc()
    {
        // Semicerchio superiore: la linea orizzontale y=0 lo tocca solo agli estremi.
        var arc = new ArcEntity(Layer0, Vector2.Zero, 5, 0, Math.PI);
        var line = new LineEntity(Layer0, new Vector2(-10, -3), new Vector2(10, -3));
        Assert.Null(SnapEngine.Find([arc, line], new Vector2(4, -3), 0.5, SnapModes.Intersection));
    }
}
