namespace Cad.Geometry;

/// <summary>
/// Vettore o punto 2D in coordinate mondo (double precision).
/// </summary>
public readonly record struct Vector2(double X, double Y)
{
    public static readonly Vector2 Zero = new(0, 0);
    public static readonly Vector2 UnitX = new(1, 0);
    public static readonly Vector2 UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>Angolo in radianti rispetto all'asse X, nell'intervallo (-π, π].</summary>
    public double Angle => Math.Atan2(Y, X);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 v) => new(-v.X, -v.Y);
    public static Vector2 operator *(Vector2 v, double s) => new(v.X * s, v.Y * s);
    public static Vector2 operator *(double s, Vector2 v) => new(v.X * s, v.Y * s);
    public static Vector2 operator /(Vector2 v, double s) => new(v.X / s, v.Y / s);

    public static double Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Componente Z del prodotto vettoriale: positiva se b è a sinistra di a.</summary>
    public static double Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vector2 a, Vector2 b) => (b - a).Length;

    public static Vector2 Lerp(Vector2 a, Vector2 b, double t) => a + (b - a) * t;

    public static Vector2 FromPolar(double length, double angle) =>
        new(length * Math.Cos(angle), length * Math.Sin(angle));

    public Vector2 Normalized()
    {
        var length = Length;
        return Tolerance.IsZero(length) ? Zero : this / length;
    }

    /// <summary>Vettore ruotato di 90° in senso antiorario.</summary>
    public Vector2 Perpendicular() => new(-Y, X);

    public bool IsAlmostEqual(Vector2 other, double tolerance = Tolerance.Default) =>
        Distance(this, other) <= tolerance;

    public override string ToString() => FormattableString.Invariant($"({X:0.####}, {Y:0.####})");
}
