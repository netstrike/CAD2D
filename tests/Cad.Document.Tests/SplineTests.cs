using Cad.Document;
using Cad.Geometry;

namespace Cad.Document.Tests;

public sealed class SplineTests
{
    private static readonly Layer Layer = new("0");

    /// <summary>Distanza minima tra un punto e la curva, campionata fitta.</summary>
    private static double DistanceToCurve(SplineEntity spline, Vector2 point)
    {
        const int samples = 4000;
        var best = double.PositiveInfinity;
        for (var i = 0; i <= samples; i++)
        {
            var u = spline.StartParameter + (spline.EndParameter - spline.StartParameter) * i / samples;
            best = Math.Min(best, Vector2.Distance(spline.PointAt(u), point));
        }

        return best;
    }

    [Fact]
    public void Open_spline_passes_through_every_point_and_starts_and_ends_on_them()
    {
        Vector2[] points = [new(0, 0), new(10, 8), new(25, -3), new(40, 6), new(55, 0)];
        var spline = SplineEntity.Through(Layer, points, closed: false);

        Assert.Equal(3, spline.Degree);
        Assert.False(spline.IsPeriodic);
        Assert.True(spline.StartPoint.IsAlmostEqual(points[0], 1e-9));
        Assert.True(spline.EndPoint.IsAlmostEqual(points[^1], 1e-9));
        Assert.All(points, p => Assert.True(DistanceToCurve(spline, p) < 0.01, $"{p} lontano dalla curva"));
        Assert.Equal(points, spline.Grips);
    }

    [Fact]
    public void Two_points_give_a_straight_segment_and_three_a_parabola()
    {
        var line = SplineEntity.Through(Layer, [new(0, 0), new(10, 0)], closed: false);
        Assert.Equal(1, line.Degree);
        Assert.Equal(10, line.Length, 9);

        var curve = SplineEntity.Through(Layer, [new(0, 0), new(5, 5), new(10, 0)], closed: false);
        Assert.Equal(2, curve.Degree);
        Assert.True(DistanceToCurve(curve, new Vector2(5, 5)) < 0.01);
    }

    [Fact]
    public void Closed_spline_is_periodic_smooth_and_passes_through_every_point()
    {
        Vector2[] points = [new(0, 0), new(20, 0), new(20, 10), new(0, 10)];
        var spline = SplineEntity.Through(Layer, points, closed: true);

        Assert.True(spline.IsClosed);
        Assert.True(spline.IsPeriodic);
        Assert.True(spline.StartPoint.IsAlmostEqual(spline.EndPoint, 1e-9));
        Assert.All(points, p => Assert.True(DistanceToCurve(spline, p) < 1e-6, $"{p} lontano dalla curva"));

        // Tangente continua nel punto di chiusura: le direzioni appena prima e appena dopo coincidono.
        var h = 1e-6;
        var after = spline.PointAt(spline.StartParameter + h) - spline.StartPoint;
        var before = spline.EndPoint - spline.PointAt(spline.EndParameter - h);
        Assert.True(Math.Abs(Vector2.Cross(after.Normalized(), before.Normalized())) < 1e-4);
        Assert.True(Vector2.Dot(after, before) > 0);

        // Simmetrica come il rettangolo: il centro dell'ingombro è il centro del rettangolo.
        Assert.True(spline.Bounds.Center.IsAlmostEqual(new Vector2(10, 5), 1e-6));
    }

    [Fact]
    public void Moving_a_fit_point_recomputes_the_curve_and_transforms_are_exact()
    {
        var spline = SplineEntity.Through(Layer, [new(0, 0), new(10, 8), new(20, 0), new(30, 8)], closed: false);
        var moved = (SplineEntity)spline.WithGripMoved(1, new Vector2(10, 20));
        Assert.True(DistanceToCurve(moved, new Vector2(10, 20)) < 0.01);
        Assert.Equal(4, moved.FitPoints.Count);

        var shifted = (SplineEntity)spline.Transformed(Matrix2D.Translation(new Vector2(100, 50)));
        Assert.True(shifted.PointAt(0.37).IsAlmostEqual(spline.PointAt(0.37) + new Vector2(100, 50), 1e-9));
        Assert.True(shifted.FitPoints[2].IsAlmostEqual(new Vector2(120, 50), 1e-9));
    }

    [Fact]
    public void Rational_weights_draw_an_exact_quarter_circle()
    {
        // Quarto di cerchio NURBS quadratico: peso √2/2 sul punto di controllo d'angolo.
        var arc = new SplineEntity(Layer, 2, [new(10, 0), new(10, 10), new(0, 10)], [0, 0, 0, 1, 1, 1], [1, Math.Sqrt(0.5), 1]);
        for (var i = 0; i <= 10; i++)
        {
            Assert.Equal(10, arc.PointAt(i / 10.0).Length, 9);
        }
    }

    [Fact]
    public void Invalid_knot_count_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new SplineEntity(Layer, 3, [new(0, 0), new(1, 1), new(2, 0), new(3, 1)], [0, 0, 0, 1, 1, 1]));
}
