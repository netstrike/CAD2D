using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class Matrix2DTests
{
    [Fact]
    public void Rotation_by_90_degrees_maps_x_axis_to_y_axis() =>
        AssertGeo.Near(new Vector2(0, 1), Matrix2D.Rotation(Math.PI / 2).Transform(new Vector2(1, 0)));

    [Fact]
    public void Rotation_about_center_keeps_center_fixed()
    {
        var center = new Vector2(5, 7);
        AssertGeo.Near(center, Matrix2D.Rotation(1.234, center).Transform(center));
    }

    [Fact]
    public void Composition_applies_left_operand_first()
    {
        var m = Matrix2D.Translation(new Vector2(10, 0)) * Matrix2D.Scaling(2, 2);
        AssertGeo.Near(new Vector2(22, 0), m.Transform(new Vector2(1, 0)));
    }

    [Fact]
    public void Mirror_about_diagonal_swaps_coordinates() =>
        AssertGeo.Near(new Vector2(3, 2), Matrix2D.Mirror(Vector2.Zero, new Vector2(1, 1)).Transform(new Vector2(2, 3)));

    [Fact]
    public void Inverse_undoes_the_transform()
    {
        var m = Matrix2D.Rotation(0.7, new Vector2(1, 2)) * Matrix2D.Scaling(3, new Vector2(-4, 5));
        Assert.True(m.TryInvert(out var inverse));
        var p = new Vector2(12.5, -3.25);
        AssertGeo.Near(p, inverse.Transform(m.Transform(p)));
    }

    [Fact]
    public void Singular_matrix_cannot_be_inverted() => Assert.False(Matrix2D.Scaling(0, 1).TryInvert(out _));
}
