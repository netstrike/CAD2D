using Cad.Geometry;

namespace Cad.Geometry.Tests;

internal static class AssertGeo
{
    public static void Near(Vector2 expected, Vector2 actual, double tolerance = 1e-9) =>
        Assert.True(expected.IsAlmostEqual(actual, tolerance), $"Atteso {expected}, ottenuto {actual}");
}
