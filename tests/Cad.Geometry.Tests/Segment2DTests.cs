using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class Segment2DTests
{
    private static readonly Segment2D Horizontal = new(new Vector2(0, 0), new Vector2(10, 0));

    [Fact]
    public void Midpoint_is_halfway() => AssertGeo.Near(new Vector2(5, 0), Horizontal.Midpoint);

    [Fact]
    public void ClosestPoint_is_perpendicular_foot_inside_segment() =>
        AssertGeo.Near(new Vector2(4, 0), Horizontal.ClosestPoint(new Vector2(4, 3)));

    [Fact]
    public void ClosestPoint_is_clamped_to_endpoints() =>
        AssertGeo.Near(new Vector2(10, 0), Horizontal.ClosestPoint(new Vector2(20, 3)));

    [Fact]
    public void Distance_to_point_beyond_end_is_measured_from_endpoint() =>
        Assert.Equal(5, Horizontal.DistanceTo(new Vector2(13, 4)), 12);
}
