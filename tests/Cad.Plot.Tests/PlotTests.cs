using System.Text;
using Cad.Document;
using Cad.Geometry;
using Cad.Plot;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.Plot.Tests;

public class PlotTests
{
    private static Scene Drawing(params Entity[] entities)
    {
        var document = new CadDocument();
        document.Edit("test", e =>
        {
            foreach (var entity in entities)
            {
                e.Add(entity);
            }
        });
        return SceneBuilder.Build(document);
    }

    private static Layer Layer(CadColor color, int weight = LineWeight.Default)
    {
        var layer = new CadDocument().GetOrAddLayer("L");
        layer.Color = color;
        layer.LineWeight = weight;
        return layer;
    }

    [Fact]
    public void Fit_scales_the_area_to_the_printable_part_and_centres_it()
    {
        var settings = new PlotSettings { Paper = PaperSize.A4, Landscape = true, Margin = 5 };
        var layout = PlotLayout.Compute(settings, new BoundingBox(Vector2.Zero, new Vector2(100, 50)));

        // A4 orizzontale: 297 × 210, stampabile 287 × 200; vince l'altezza (200 / 50 = 4) sulla larghezza (2,87).
        Assert.Equal(2.87, layout.PaperPerUnit, 9);
        Assert.True(layout.Fits);
        var lowerLeft = layout.WorldToPaper.Transform(Vector2.Zero);
        var upperRight = layout.WorldToPaper.Transform(new Vector2(100, 50));
        Assert.Equal(5, lowerLeft.X, 9);
        Assert.Equal(210 - (210 - 50 * 2.87) / 2, lowerLeft.Y, 9);
        Assert.Equal(292, upperRight.X, 9);
        Assert.True(upperRight.Y < lowerLeft.Y);
    }

    [Fact]
    public void Fixed_scale_keeps_real_millimetres_and_reports_when_the_drawing_does_not_fit()
    {
        var settings = new PlotSettings { Paper = PaperSize.A3, Landscape = false, Fit = false, Scale = 2, Center = false };
        var layout = PlotLayout.Compute(settings, new BoundingBox(new Vector2(1000, 1000), new Vector2(1400, 1200)));
        Assert.Equal(0.5, layout.PaperPerUnit, 12);
        Assert.Equal(2, layout.Scale, 12);
        Assert.True(layout.Fits);

        // Senza centratura il disegno parte dall'angolo in alto a sinistra.
        var upperLeft = layout.WorldToPaper.Transform(new Vector2(1000, 1200));
        Assert.Equal(5, upperLeft.X, 9);
        Assert.Equal(5, upperLeft.Y, 9);

        settings.Scale = 0.5;
        Assert.False(PlotLayout.Compute(settings, new BoundingBox(Vector2.Zero, new Vector2(400, 200))).Fits);
    }

    [Fact]
    public void Pdf_has_one_page_of_the_paper_size()
    {
        var scene = Drawing(new LineEntity(Layer(new CadColor(255, 0, 0)), Vector2.Zero, new Vector2(100, 50)));
        var layout = PlotLayout.Compute(new PlotSettings { Paper = PaperSize.A3, Landscape = true }, scene.Bounds);
        using var stream = new MemoryStream();
        PlotExporter.Pdf(stream, scene, layout, "Prova");

        var text = Encoding.Latin1.GetString(stream.ToArray());
        Assert.StartsWith("%PDF", text);
        // A3 orizzontale: 420 × 297 mm = 1190,55 × 841,89 punti (Skia arrotonda al centesimo di punto o poco più).
        var box = System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
        Assert.True(box.Success);
        Assert.Equal(1190.55, double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0);
        Assert.Equal(841.89, double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 0);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page\\b"));
    }

    [Fact]
    public void Svg_has_the_paper_size_and_the_drawing()
    {
        var scene = Drawing(new CircleEntity(Layer(new CadColor(0, 0, 255)), Vector2.Zero, 20));
        var layout = PlotLayout.Compute(new PlotSettings { Paper = PaperSize.A4, Landscape = false }, scene.Bounds);
        using var stream = new MemoryStream();
        PlotExporter.Svg(stream, scene, layout);

        var svg = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("<svg", svg);
        Assert.Contains("width=\"210mm\" height=\"297mm\" viewBox=\"0 0 ", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("stroke=\"blue\"", svg);
    }

    [Fact]
    public void Lines_are_printed_in_scale_with_their_lineweight_and_white_becomes_black()
    {
        // 1:1 su A4 verticale senza centratura: la linea orizzontale a y = 50 cade 50 mm sopra il bordo inferiore dell'area.
        var thick = new LineEntity(Layer(new CadColor(255, 255, 255), 100), new Vector2(0, 50), new Vector2(100, 50));
        var corner = new PointEntity(Layer(new CadColor(255, 255, 255)), new Vector2(0, 0));
        var scene = Drawing(thick, corner);
        var settings = new PlotSettings { Paper = PaperSize.A4, Landscape = false, Fit = false, Scale = 1, Center = false, Margin = 0 };
        var layout = PlotLayout.Compute(settings, new BoundingBox(Vector2.Zero, new Vector2(100, 100)));
        using var bitmap = PlotExporter.Bitmap(scene, layout, 254);

        // A 254 dpi un millimetro è 10 pixel. La linea è alta 1 mm, quindi circa 10 pixel, a 50 mm dall'alto.
        var x = 500;
        var dark = Enumerable.Range(0, bitmap.Height).Where(y => bitmap.GetPixel(x, y).Red < 128).ToList();
        Assert.InRange(dark.Count, 9, 11);
        Assert.InRange(dark.Average(), 495, 505);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(x, 500).WithAlpha(255));

        settings.Lineweights = false;
        using var thin = PlotExporter.Bitmap(scene, PlotLayout.Compute(settings, layout.Area), 254);
        Assert.InRange(Enumerable.Range(0, thin.Height).Count(y => thin.GetPixel(x, y).Red < 200), 1, 3);
    }

    [Fact]
    public void Monochrome_turns_colours_black()
    {
        var scene = Drawing(new LineEntity(Layer(new CadColor(255, 0, 0), 50), new Vector2(0, 10), new Vector2(100, 10)));
        var settings = new PlotSettings { Paper = PaperSize.A4, Fit = false, Center = false, Margin = 0, Landscape = false };
        var area = new BoundingBox(Vector2.Zero, new Vector2(100, 20));
        using (var colour = PlotExporter.Bitmap(scene, PlotLayout.Compute(settings, area), 254))
        {
            var p = colour.GetPixel(500, 100);
            Assert.True(p.Red > 200 && p.Green < 60, $"atteso rosso, ottenuto {p}");
        }

        settings.Monochrome = true;
        using var mono = PlotExporter.Bitmap(scene, PlotLayout.Compute(settings, area), 254);
        Assert.True(mono.GetPixel(500, 100).Red < 60);
    }

    [Fact]
    public void Bands_put_together_give_the_whole_sheet()
    {
        var scene = Drawing(new CircleEntity(Layer(new CadColor(0, 0, 0)), Vector2.Zero, 30), new TextEntity(Layer(new CadColor(0, 0, 0)), new Vector2(-20, 0), 8, "CAD2D"));
        var layout = PlotLayout.Compute(new PlotSettings { Paper = PaperSize.A4, Landscape = true }, scene.Bounds);
        using var whole = PlotExporter.Bitmap(scene, layout, 50);
        var rows = 37;
        for (var top = 0; top < whole.Height; top += rows)
        {
            var count = Math.Min(rows, whole.Height - top);
            using var band = PlotExporter.Band(scene, layout, 50, top, count);
            for (var y = 0; y < count; y += 5)
            {
                for (var x = 0; x < whole.Width; x += 7)
                {
                    // L'antialiasing può differire di un'unità sui bordi delle strisce.
                    var a = whole.GetPixel(x, top + y);
                    var b = band.GetPixel(x, y);
                    Assert.True(Math.Abs(a.Red - b.Red) <= 3 && Math.Abs(a.Green - b.Green) <= 3 && Math.Abs(a.Blue - b.Blue) <= 3, $"({x},{top + y}): {a} contro {b}");
                }
            }
        }
    }

    [Fact]
    public void At_a_fixed_scale_what_lies_around_the_window_is_printed_too()
    {
        // Finestra piccola a 1:1 su un A4: il testo accanto alla finestra cade sul foglio e deve comparire.
        var black = Layer(new CadColor(0, 0, 0));
        var scene = Drawing(new LineEntity(black, Vector2.Zero, new Vector2(20, 0)), new TextEntity(black, new Vector2(60, 0), 10, "XXXX"));
        var settings = new PlotSettings { Paper = PaperSize.A4, Fit = false, Scale = 1, Area = PlotArea.Window, Window = new BoundingBox(new Vector2(0, -5), new Vector2(20, 5)) };
        var layout = PlotLayout.Compute(settings, PlotExporter.AreaOf(settings, scene));
        Assert.True(layout.VisibleWorld.Contains(new Vector2(80, 5)));

        using var bitmap = PlotExporter.Bitmap(scene, layout, 100);
        var text = layout.WorldToPaper.Transform(new Vector2(60, 0));
        var factor = 100 / 25.4;
        var dark = 0;
        for (var x = (int)(text.X * factor); x < (int)((text.X + 30) * factor); x++)
        {
            for (var y = (int)((text.Y - 10) * factor); y < (int)(text.Y * factor); y++)
            {
                dark += bitmap.GetPixel(x, y).Red < 128 ? 1 : 0;
            }
        }

        Assert.True(dark > 50, $"testo non stampato ({dark} pixel scuri)");
    }
}
