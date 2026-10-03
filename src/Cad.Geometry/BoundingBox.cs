namespace Cad.Geometry;

/// <summary>
/// Rettangolo allineato agli assi. <see cref="Empty"/> non contiene alcun punto ed è neutro per <see cref="Union(BoundingBox)"/>.
/// </summary>
public readonly record struct BoundingBox(Vector2 Min, Vector2 Max)
{
    public static readonly BoundingBox Empty = new(
        new Vector2(double.PositiveInfinity, double.PositiveInfinity),
        new Vector2(double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y;
    public double Width => IsEmpty ? 0 : Max.X - Min.X;
    public double Height => IsEmpty ? 0 : Max.Y - Min.Y;
    public Vector2 Center => (Min + Max) / 2;

    public static BoundingBox FromPoints(Vector2 a, Vector2 b) => new(
        new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
        new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    public static BoundingBox FromPoints(IEnumerable<Vector2> points)
    {
        var box = Empty;
        foreach (var p in points)
        {
            box = box.Union(p);
        }

        return box;
    }

    public BoundingBox Union(Vector2 p) => IsEmpty
        ? new BoundingBox(p, p)
        : new BoundingBox(
            new Vector2(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y)),
            new Vector2(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y)));

    public BoundingBox Union(BoundingBox other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        return IsEmpty ? other : Union(other.Min).Union(other.Max);
    }

    public BoundingBox Inflate(double amount) => IsEmpty
        ? this
        : new BoundingBox(Min - new Vector2(amount, amount), Max + new Vector2(amount, amount));

    public bool Contains(Vector2 p, double tolerance = Tolerance.Default) =>
        !IsEmpty &&
        p.X >= Min.X - tolerance && p.X <= Max.X + tolerance &&
        p.Y >= Min.Y - tolerance && p.Y <= Max.Y + tolerance;

    /// <summary>Vero se <paramref name="other"/> è interamente dentro questo rettangolo (selezione a finestra).</summary>
    public bool Contains(BoundingBox other) => !other.IsEmpty && Contains(other.Min) && Contains(other.Max);

    /// <summary>Vero se i due rettangoli si toccano o si sovrappongono (selezione interseca).</summary>
    public bool Intersects(BoundingBox other) =>
        !IsEmpty && !other.IsEmpty &&
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;
}
