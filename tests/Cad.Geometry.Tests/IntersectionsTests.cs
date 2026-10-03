using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class IntersectionsTests
{
    [Fact]
    public void Crossing_segments_meet_in_one_point()
    {
        var a = new Segment2D(new Vector2(0, 0), new Vector2(10, 10));
        var b = new Segment2D(new Vector2(0, 10), new Vector2(10, 0));
        var points = Intersections.SegmentSegment(a, b);
        AssertGeo.Near(new Vector2(5, 5), Assert.Single(points));
    }

    [Fact]
    public void Segments_touching_at_an_endpoint_intersect()
    {
        var a = new Segment2D(new Vector2(0, 0), new Vector2(10, 0));
        var b = new Segment2D(new Vector2(10, 0), new Vector2(10, 5));
        AssertGeo.Near(new Vector2(10, 0), Assert.Single(Intersections.SegmentSegment(a, b)));
    }

    [Fact]
    public void Separate_segments_only_meet_when_extended()
    {
        var a = new Segment2D(new Vector2(0, 0), new Vector2(1, 0));
        var b = new Segment2D(new Vector2(5, 1), new Vector2(5, 2));
        Assert.Empty(Intersections.SegmentSegment(a, b));
        AssertGeo.Near(new Vector2(5, 0), Assert.Single(Intersections.SegmentSegment(a, b, extend: true)));
    }

    [Fact]
    public void Parallel_segments_do_not_intersect()
    {
        var a = new Segment2D(new Vector2(0, 0), new Vector2(10, 0));
        var b = new Segment2D(new Vector2(0, 1), new Vector2(10, 1));
        Assert.Empty(Intersections.SegmentSegment(a, b, extend: true));
    }

    [Fact]
    public void Segment_through_circle_gives_two_points_in_order()
    {
        var segment = new Segment2D(new Vector2(-10, 0), new Vector2(10, 0));
        var points = Intersections.SegmentCircle(segment, new Circle2D(Vector2.Zero, 5));
        Assert.Equal(2, points.Count);
        AssertGeo.Near(new Vector2(-5, 0), points[0]);
        AssertGeo.Near(new Vector2(5, 0), points[1]);
    }

    [Fact]
    public void Tangent_segment_gives_one_point()
    {
        var segment = new Segment2D(new Vector2(-10, 5), new Vector2(10, 5));
        AssertGeo.Near(new Vector2(0, 5), Assert.Single(Intersections.SegmentCircle(segment, new Circle2D(Vector2.Zero, 5))));
    }

    [Fact]
    public void Segment_ending_inside_circle_gives_one_point()
    {
        var segment = new Segment2D(new Vector2(-10, 0), new Vector2(0, 0));
        AssertGeo.Near(new Vector2(-5, 0), Assert.Single(Intersections.SegmentCircle(segment, new Circle2D(Vector2.Zero, 5))));
    }

    [Fact]
    public void Overlapping_circles_meet_in_two_points()
    {
        var points = Intersections.CircleCircle(new Circle2D(Vector2.Zero, 5), new Circle2D(new Vector2(8, 0), 5));
        Assert.Equal(2, points.Count);
        Assert.Contains(points, p => p.IsAlmostEqual(new Vector2(4, 3)));
        Assert.Contains(points, p => p.IsAlmostEqual(new Vector2(4, -3)));
    }

    [Fact]
    public void Externally_tangent_circles_meet_in_one_point() =>
        AssertGeo.Near(
            new Vector2(5, 0),
            Assert.Single(Intersections.CircleCircle(new Circle2D(Vector2.Zero, 5), new Circle2D(new Vector2(10, 0), 5))));

    [Fact]
    public void Concentric_and_distant_circles_do_not_meet()
    {
        Assert.Empty(Intersections.CircleCircle(new Circle2D(Vector2.Zero, 5), new Circle2D(Vector2.Zero, 3)));
        Assert.Empty(Intersections.CircleCircle(new Circle2D(Vector2.Zero, 1), new Circle2D(new Vector2(10, 0), 1)));
    }
}
