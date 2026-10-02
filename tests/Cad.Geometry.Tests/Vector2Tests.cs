using Cad.Geometry;

namespace Cad.Geometry.Tests;

public class Vector2Tests
{
    [Fact]
    public void Length_of_3_4_is_5() => Assert.Equal(5, new Vector2(3, 4).Length, 12);

    [Fact]
    public void Cross_is_positive_when_second_vector_is_to_the_left() =>
        Assert.True(Vector2.Cross(Vector2.UnitX, Vector2.UnitY) > 0);

    [Fact]
    public void Normalized_zero_vector_stays_zero() => Assert.Equal(Vector2.Zero, Vector2.Zero.Normalized());

    [Fact]
    public void FromPolar_round_trips_angle_and_length()
    {
        var v = Vector2.FromPolar(2, Math.PI / 3);
        Assert.Equal(2, v.Length, 12);
        Assert.Equal(Math.PI / 3, v.Angle, 12);
    }
}
