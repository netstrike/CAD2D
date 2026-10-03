namespace Cad.Geometry;

/// <summary>
/// Tolleranza unica usata da tutte le operazioni geometriche.
/// </summary>
public static class Tolerance
{
    /// <summary>Distanza sotto la quale due punti sono considerati coincidenti.</summary>
    public const double Default = 1e-9;

    public static bool IsZero(double value, double tolerance = Default) => Math.Abs(value) <= tolerance;

    public static bool AreEqual(double a, double b, double tolerance = Default) => Math.Abs(a - b) <= tolerance;
}
