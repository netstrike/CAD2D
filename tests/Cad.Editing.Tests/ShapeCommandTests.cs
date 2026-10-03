using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

/// <summary>ELLISSE, SPLINE, POLIGONO e PUNTO.</summary>
public sealed class ShapeCommandTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;

    public ShapeCommandTests() =>
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false, TrackingEnabled = false };

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, double tolerance = 1e-9) =>
        Assert.True(expected.IsAlmostEqual(actual, tolerance), $"Atteso {expected}, ottenuto {actual}");

    [Fact]
    public void Ellipse_from_axis_endpoints_and_the_other_half_axis()
    {
        Type("ELLISSE", "0,0", "40,0", "10");
        var ellipse = Assert.Single(_document.ModelSpace.OfType<EllipseEntity>());
        AssertNear(new Vector2(20, 0), ellipse.Center);
        Assert.Equal(20, ellipse.MajorAxis.Length, 9);
        Assert.Equal(10, ellipse.MinorAxis.Length, 9);
        Assert.Equal(Math.Tau, ellipse.Sweep, 9);
        Assert.True(Vector2.Cross(ellipse.MajorAxis, ellipse.MinorAxis) > 0);
    }

    [Fact]
    public void Ellipse_by_center_keeps_the_longer_axis_as_major()
    {
        Type("EL", "Centro", "0,0", "0,5", "15");
        var ellipse = Assert.Single(_document.ModelSpace.OfType<EllipseEntity>());
        Assert.Equal(15, ellipse.MajorAxis.Length, 9);
        Assert.Equal(5, ellipse.MinorAxis.Length, 9);
        // L'asse maggiore è perpendicolare a quello indicato (verticale), quindi orizzontale.
        Assert.Equal(0, ellipse.MajorAxis.Y, 9);
        Assert.Equal(15 * 5 * Math.PI, Math.PI * ellipse.MajorAxis.Length * ellipse.MinorAxis.Length, 9);
    }

    [Fact]
    public void Elliptical_arc_goes_from_the_start_to_the_end_direction()
    {
        Type("ELLISSE", "Arco", "-20,0", "20,0", "10", "0", "90");
        var arc = Assert.Single(_document.ModelSpace.OfType<EllipseEntity>());
        AssertNear(new Vector2(20, 0), arc.PointAt(arc.StartParameter));
        AssertNear(new Vector2(0, 10), arc.PointAt(arc.EndParameter));
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);

        // Una direzione a 45° non è il parametro 45°: il punto deve stare proprio su quella direzione.
        var t = ShapeCommands.ParameterAt(arc, Math.PI / 4);
        var p = arc.PointAt(t) - arc.Center;
        Assert.Equal(Math.PI / 4, p.Angle, 9);

        // Oltre i 180°: l'arco finisce sotto l'asse, nella direzione indicata.
        Type("EL", "A", "-20,-80", "100,-80", "25", "0", "200");
        var wide = _document.ModelSpace.OfType<EllipseEntity>().Single(e => e.Center.Y < -50);
        var end = wide.PointAt(wide.EndParameter) - wide.Center;
        Assert.Equal(200, Arc2D.NormalizeAngle(end.Angle) * 180 / Math.PI, 6);
    }

    [Fact]
    public void Spline_with_close_option_is_periodic_and_undoes_in_one_step()
    {
        Type("SPLINE", "0,0", "20,0", "20,10", "Annulla", "20,10", "0,10", "Chiudi");
        var spline = Assert.Single(_document.ModelSpace.OfType<SplineEntity>());
        Assert.True(spline.IsClosed);
        Assert.Equal(4, spline.FitPoints.Count);

        Type("SPL", "0,50", "10,60", "20,50", "");
        Assert.Equal(2, _document.ModelSpace.OfType<SplineEntity>().Count());
        _editor.RunCommand("ANNULLA");
        Assert.Single(_document.ModelSpace.OfType<SplineEntity>());
    }

    [Fact]
    public void Polygon_inscribed_and_circumscribed_with_typed_radius()
    {
        Type("POLIGONO", "6", "0,0", "Inscritto", "10");
        var inscribed = Assert.Single(_document.ModelSpace.OfType<PolylineEntity>());
        Assert.True(inscribed.IsClosed);
        Assert.Equal(6, inscribed.Vertices.Count);
        Assert.All(inscribed.Vertices, v => Assert.Equal(10, v.Position.Length, 9));
        // Raggio scritto: il lato in basso è orizzontale.
        Assert.Equal(2, inscribed.Vertices.Count(v => Math.Abs(v.Position.Y - inscribed.Bounds.Min.Y) < 1e-9));

        // Invio accetta i 6 lati proposti; ora circoscritto: l'apotema è il raggio.
        Type("POL", "", "50,0", "C", "10");
        var circumscribed = _document.ModelSpace.OfType<PolylineEntity>().Single(p => p.Vertices[0].Position.X > 30);
        Assert.Equal(6, circumscribed.Vertices.Count);
        Assert.Equal(-10, circumscribed.Bounds.Min.Y, 9);
        Assert.All(circumscribed.Vertices, v => Assert.Equal(10 / Math.Cos(Math.PI / 6), Vector2.Distance(v.Position, new Vector2(50, 0)), 9));
    }

    [Fact]
    public void Polygon_by_edge_and_invalid_side_count()
    {
        var messages = new List<string>();
        _editor.Message += messages.Add;
        Type("POLIGONO", "2", "4.5", "4", "Lato", "0,0", "10,0");
        Assert.Contains(messages, m => m.Contains("da 3 a 1024", StringComparison.Ordinal));
        Assert.Equal(2, messages.Count(m => m.Contains("da 3", StringComparison.Ordinal)));

        var square = Assert.Single(_document.ModelSpace.OfType<PolylineEntity>());
        AssertNear(new Vector2(0, 0), square.Vertices[0].Position);
        AssertNear(new Vector2(10, 0), square.Vertices[1].Position);
        AssertNear(new Vector2(10, 10), square.Vertices[2].Position);
        AssertNear(new Vector2(0, 10), square.Vertices[3].Position);
    }

    [Fact]
    public void Point_places_points_until_enter()
    {
        Type("PUNTO", "1,1", "2,2", "3,3", "");
        Assert.Equal(3, _document.ModelSpace.OfType<PointEntity>().Count());
        Assert.False(_editor.IsCommandActive);
    }

    [Fact]
    public void Ellipse_snaps_to_quadrants_but_not_to_its_tessellation()
    {
        var ellipse = ShapeCommands.Full(_document.GetOrAddLayer("0"), Vector2.Zero, new Vector2(20, 0), 10);
        // Vertice della spezzata a 64 passi, lontano dai quadranti: niente estremi spuri.
        var vertex = ellipse.PointAt(Math.Tau * 3 / 64);
        Assert.Null(SnapEngine.Find([ellipse], vertex, 0.5, SnapModes.Endpoint | SnapModes.Midpoint | SnapModes.Intersection));

        var quadrant = SnapEngine.Find([ellipse], new Vector2(0.2, 10.1), 0.5, SnapModes.Default);
        Assert.Equal(SnapModes.Quadrant, quadrant!.Value.Kind);
        AssertNear(new Vector2(0, 10), quadrant.Value.Point);
    }

    [Fact]
    public void Spline_has_endpoint_snaps_properties_and_hatch_boundary()
    {
        Type("SPLINE", "0,0", "10,10", "20,0", "");
        var spline = Assert.Single(_document.ModelSpace.OfType<SplineEntity>());
        var end = SnapEngine.Find([spline], new Vector2(19.8, 0.2), 0.5, SnapModes.Default);
        Assert.Equal(SnapModes.Endpoint, end!.Value.Kind);
        AssertNear(new Vector2(20, 0), end.Value.Point);

        var rows = PropertySheet.For(_document, [spline]);
        Assert.Contains(rows, r => r.Name == "Grado" && r.Value == "2");
        Assert.Equal("Spline", PropertySheet.TypeName(spline));

        Type("SPLINE", "0,50", "20,50", "20,60", "0,60", "Chiudi");
        var closed = _document.ModelSpace.OfType<SplineEntity>().Single(s => s.IsClosed);
        Assert.NotNull(Hatching.ClosedLoop(closed));
    }
}
