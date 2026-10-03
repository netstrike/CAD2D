using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class Arc2DTests
{
    [Fact]
    public void Equal_start_and_end_is_a_full_circle() =>
        Assert.Equal(Math.Tau, new Arc2D(Vector2.Zero, 1, 1, 1).Sweep, 12);

    [Fact]
    public void Sweep_wraps_through_zero() =>
        Assert.Equal(Math.PI, new Arc2D(Vector2.Zero, 1, 3 * Math.PI / 2, Math.PI / 2).Sweep, 12);

    [Fact]
    public void Bounds_include_crossed_quadrant_points()
    {
        // Arco da 45° a 135°: passa per la sommità (0, 1).
        var arc = new Arc2D(Vector2.Zero, 1, Math.PI / 4, 3 * Math.PI / 4);
        Assert.Equal(1, arc.Bounds.Max.Y, 12);
        Assert.Equal(Math.Sqrt(0.5), arc.Bounds.Min.Y, 12);
    }

    [Fact]
    public void Positive_bulge_of_one_is_a_counterclockwise_half_circle()
    {
        var arc = Arc2D.FromBulge(new Vector2(0, 0), new Vector2(2, 0), 1)!.Value;
        AssertGeo.Near(new Vector2(1, 0), arc.Center);
        Assert.Equal(1, arc.Radius, 12);
        // Antiorario da (0,0) a (2,0) passa sotto la corda.
        AssertGeo.Near(new Vector2(1, -1), arc.Midpoint);
    }

    [Fact]
    public void Negative_bulge_passes_on_the_other_side()
    {
        var arc = Arc2D.FromBulge(new Vector2(0, 0), new Vector2(2, 0), -1)!.Value;
        AssertGeo.Near(new Vector2(1, 1), arc.Midpoint);
    }

    [Fact]
    public void Quarter_circle_bulge_matches_fillet()
    {
        // Raccordo di raggio 20 come nel contorno di samples/demo.dxf.
        var bulge = Math.Tan(Math.PI / 8);
        var arc = Arc2D.FromBulge(new Vector2(180, 0), new Vector2(200, 20), bulge)!.Value;
        AssertGeo.Near(new Vector2(180, 20), arc.Center);
        Assert.Equal(20, arc.Radius, 9);
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);
    }

    [Fact]
    public void Zero_bulge_is_a_straight_segment() =>
        Assert.Null(Arc2D.FromBulge(Vector2.Zero, new Vector2(1, 0), 0));
}
