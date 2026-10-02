namespace Cad.Geometry;

/// <summary>
/// Trasformazione affine 2D: x' = M11·x + M21·y + OffsetX, y' = M12·x + M22·y + OffsetY.
/// </summary>
public readonly record struct Matrix2D(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static readonly Matrix2D Identity = new(1, 0, 0, 1, 0, 0);

    public double Determinant => M11 * M22 - M12 * M21;

    public static Matrix2D Translation(Vector2 offset) => new(1, 0, 0, 1, offset.X, offset.Y);

    public static Matrix2D Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Matrix2D Scaling(double factor, Vector2 center) =>
        Translation(-center) * Scaling(factor, factor) * Translation(center);

    /// <summary>Rotazione antioraria di <paramref name="angle"/> radianti attorno all'origine.</summary>
    public static Matrix2D Rotation(double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return new(cos, sin, -sin, cos, 0, 0);
    }

    public static Matrix2D Rotation(double angle, Vector2 center) =>
        Translation(-center) * Rotation(angle) * Translation(center);

    /// <summary>Simmetria rispetto alla retta passante per <paramref name="a"/> e <paramref name="b"/>.</summary>
    public static Matrix2D Mirror(Vector2 a, Vector2 b)
    {
        var angle = (b - a).Angle;
        var cos2 = Math.Cos(2 * angle);
        var sin2 = Math.Sin(2 * angle);
        var reflect = new Matrix2D(cos2, sin2, sin2, -cos2, 0, 0);
        return Translation(-a) * reflect * Translation(a);
    }

    /// <summary>Composizione: applica prima <paramref name="first"/>, poi <paramref name="second"/>.</summary>
    public static Matrix2D operator *(Matrix2D first, Matrix2D second) => new(
        first.M11 * second.M11 + first.M12 * second.M21,
        first.M11 * second.M12 + first.M12 * second.M22,
        first.M21 * second.M11 + first.M22 * second.M21,
        first.M21 * second.M12 + first.M22 * second.M22,
        first.OffsetX * second.M11 + first.OffsetY * second.M21 + second.OffsetX,
        first.OffsetX * second.M12 + first.OffsetY * second.M22 + second.OffsetY);

    public Vector2 Transform(Vector2 point) => new(
        M11 * point.X + M21 * point.Y + OffsetX,
        M12 * point.X + M22 * point.Y + OffsetY);

    /// <summary>Trasforma una direzione, ignorando la traslazione.</summary>
    public Vector2 TransformVector(Vector2 vector) => new(
        M11 * vector.X + M21 * vector.Y,
        M12 * vector.X + M22 * vector.Y);

    public bool TryInvert(out Matrix2D inverse)
    {
        var det = Determinant;
        if (Tolerance.IsZero(det, 1e-15))
        {
            inverse = Identity;
            return false;
        }

        inverse = new Matrix2D(
            M22 / det,
            -M12 / det,
            -M21 / det,
            M11 / det,
            (M21 * OffsetY - M22 * OffsetX) / det,
            (M12 * OffsetX - M11 * OffsetY) / det);
        return true;
    }
}
